package com.atvcast.receiver.webrtc

import android.content.Context
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.view.SurfaceHolder
import com.atvcast.receiver.signaling.MessageType
import com.atvcast.receiver.signaling.SignalingMessage
import com.atvcast.receiver.signaling.SignalingServer
import com.google.gson.Gson
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import org.webrtc.AudioTrack
import org.webrtc.DataChannel
import org.webrtc.DefaultVideoDecoderFactory
import org.webrtc.EglBase
import org.webrtc.IceCandidate
import org.webrtc.MediaConstraints
import org.webrtc.MediaStream
import org.webrtc.PeerConnection
import org.webrtc.PeerConnectionFactory
import org.webrtc.RTCStatsCollectorCallback
import org.webrtc.RTCStatsReport
import org.webrtc.RendererCommon
import org.webrtc.RtpReceiver
import org.webrtc.SdpObserver
import org.webrtc.SessionDescription
import org.webrtc.SurfaceViewRenderer
import org.webrtc.VideoDecoderFactory
import org.webrtc.VideoTrack

class WebRtcReceiver(
    private val context: Context,
    private val signalingServer: SignalingServer
) {
    private val tag = "WebRtcReceiver"
    // Reused across calls; Gson is thread-safe for reads after construction.
    private val gson = Gson()
    private var peerConnectionFactory: PeerConnectionFactory? = null
    private var peerConnection: PeerConnection? = null
    private var eglBase: EglBase? = null
    private var videoRenderer: SurfaceViewRenderer? = null

    var onVideoTrackReceived: (() -> Unit)? = null
    var onIceConnectionStateChanged: ((PeerConnection.IceConnectionState) -> Unit)? = null
    @Volatile var isConnected: Boolean = false

    @Volatile private var remoteDescriptionSet: Boolean = false
    private val pendingCandidates = java.util.concurrent.ConcurrentLinkedQueue<IceCandidate>()

    @Volatile private var rendererReady: Boolean = false
    private val pendingVideoTracks = java.util.concurrent.ConcurrentLinkedQueue<VideoTrack>()
    private val mainHandler = Handler(Looper.getMainLooper())

    private var telemetryChannel: DataChannel? = null
    @Volatile private var telemetryChannelOpen: Boolean = false

    private val ioScope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private var statsRunning = false

    fun initialize(renderer: SurfaceViewRenderer) {
        eglBase = EglBase.create()

        PeerConnectionFactory.initialize(
            PeerConnectionFactory.InitializationOptions.builder(context)
                .setEnableInternalTracer(false)
                .createInitializationOptions()
        )

        val decoderFactory: VideoDecoderFactory = DefaultVideoDecoderFactory(eglBase!!.eglBaseContext)

        peerConnectionFactory = PeerConnectionFactory.builder()
            .setVideoDecoderFactory(decoderFactory)
            .createPeerConnectionFactory()

        videoRenderer = renderer
        renderer.holder.addCallback(object : SurfaceHolder.Callback {
            private var inited = false
            override fun surfaceCreated(holder: SurfaceHolder) {
                if (inited) return
                renderer.init(eglBase!!.eglBaseContext, null)
                renderer.setMirror(false)
                renderer.setScalingType(RendererCommon.ScalingType.SCALE_ASPECT_FIT)
                renderer.setEnableHardwareScaler(true)
                rendererReady = true
                Log.i(tag, "SurfaceViewRenderer init() done (SCALE_ASPECT_FIT, hwScaler=on)")
                while (true) {
                    val t = pendingVideoTracks.poll() ?: break
                    t.addSink(renderer)
                    Log.i(tag, "Flushed pending video track sink")
                }
            }
            override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {}
            override fun surfaceDestroyed(holder: SurfaceHolder) {}
        })

        Log.i(tag, "WebRTC initialized (renderer init deferred to surfaceCreated)")
    }

    fun handleOffer(sdpString: String) {
        val oldPc = peerConnection
        if (oldPc != null) {
            Log.i(tag, "handleOffer: closing stale PeerConnection before creating new one")
            stopStatsLoop()
            try { oldPc.close() } catch (e: Throwable) { Log.w(tag, "close stale PC: $e") }
            peerConnection = null
        }

        remoteDescriptionSet = false
        pendingCandidates.clear()

        val rtcConfig = PeerConnection.RTCConfiguration(emptyList<PeerConnection.IceServer>()).apply {
            iceTransportsType = PeerConnection.IceTransportsType.ALL
            bundlePolicy = PeerConnection.BundlePolicy.MAXBUNDLE
            rtcpMuxPolicy = PeerConnection.RtcpMuxPolicy.REQUIRE
        }

        var thisPC: PeerConnection? = null
        val observer = object : PeerConnection.Observer {
            override fun onIceCandidate(candidate: IceCandidate) {
                val json = gson.toJson(candidate)
                signalingServer.sendMessage(SignalingMessage(MessageType.ICE_CANDIDATE, json))
            }

            override fun onIceConnectionChange(state: PeerConnection.IceConnectionState?) {
                Log.i(tag, "ICE state: $state")
                state ?: return
                if (thisPC != peerConnection) return
                isConnected = (state == PeerConnection.IceConnectionState.CONNECTED ||
                        state == PeerConnection.IceConnectionState.COMPLETED)
                mainHandler.post { onIceConnectionStateChanged?.invoke(state) }
            }

            override fun onAddTrack(receiver: RtpReceiver, streams: Array<out MediaStream>) {
                val track = receiver.track()
                if (track is VideoTrack) {
                    mainHandler.post {
                        if (rendererReady && videoRenderer != null) {
                            track.addSink(videoRenderer)
                            Log.i(tag, "Video track sink attached")
                        } else {
                            pendingVideoTracks.add(track)
                            Log.i(tag, "Video track queued (renderer not ready)")
                        }
                        onVideoTrackReceived?.invoke()
                        startStatsLoop()
                    }
                }
                if (track is AudioTrack) {
                    track.setEnabled(true)
                    Log.i(tag, "Audio track added")
                }
            }

            override fun onSignalingChange(state: PeerConnection.SignalingState?) {}
            override fun onIceConnectionReceivingChange(receiving: Boolean) {}
            override fun onIceGatheringChange(state: PeerConnection.IceGatheringState?) {}
            override fun onIceCandidatesRemoved(candidates: Array<out IceCandidate>?) {}
            override fun onAddStream(stream: MediaStream?) {}
            override fun onRemoveStream(stream: MediaStream?) {}
            override fun onDataChannel(channel: DataChannel?) {
                channel ?: return
                Log.i(tag, "onDataChannel received: label=${channel.label()}")
                telemetryChannel = channel
                channel.registerObserver(object : DataChannel.Observer {
                    override fun onStateChange() {
                        val state = channel.state()
                        telemetryChannelOpen = (state == DataChannel.State.OPEN)
                        Log.i(tag, "telemetry channel state: $state")
                    }
                    override fun onMessage(buffer: DataChannel.Buffer?) {}
                    override fun onBufferedAmountChange(p0: Long) {}
                })
            }
            override fun onRenegotiationNeeded() {}
        }
        peerConnection = peerConnectionFactory?.createPeerConnection(rtcConfig, observer)
        thisPC = peerConnection

        val offer = SessionDescription(SessionDescription.Type.OFFER, sdpString)
        peerConnection?.setRemoteDescription(object : SdpObserver {
            override fun onSetSuccess() {
                remoteDescriptionSet = true
                Log.i(tag, "setRemoteDescription OK, flushing ${pendingCandidates.size} pending ICE candidates")
                while (true) {
                    val c = pendingCandidates.poll() ?: break
                    try { peerConnection?.addIceCandidate(c) } catch (e: Throwable) { Log.e(tag, "flush addIceCandidate failed: $e") }
                }
                createAnswer()
            }
            override fun onSetFailure(error: String?) {
                Log.e(tag, "setRemoteDescription failed: $error")
            }
            override fun onCreateSuccess(sdp: SessionDescription?) {}
            override fun onCreateFailure(error: String?) {}
        }, offer)
    }

    fun handleIceCandidate(candidateJson: String) {
        try {
            val obj = gson.fromJson(candidateJson, IceCandidateDto::class.java)
            val sdp = obj.sdp?.ifEmpty { null } ?: obj.candidate?.ifEmpty { null }
            if (sdp.isNullOrEmpty()) {
                Log.w(tag, "handleIceCandidate: empty sdp, skip ($candidateJson)")
                return
            }
            val candidate = IceCandidate(obj.sdpMid ?: "", obj.sdpMLineIndex ?: 0, sdp)
            if (!remoteDescriptionSet) {
                Log.i(tag, "Buffering ICE candidate (remoteDescription not set yet): mid=${obj.sdpMid} idx=${obj.sdpMLineIndex}")
                pendingCandidates.add(candidate)
                return
            }
            peerConnection?.addIceCandidate(candidate)
        } catch (e: Throwable) {
            Log.e(tag, "handleIceCandidate parse error: $e", e)
        }
    }

    private data class IceCandidateDto(
        val sdpMid: String?,
        val sdpMLineIndex: Int?,
        val sdp: String?,
        val candidate: String?
    )

    private fun createAnswer() {
        val constraints = MediaConstraints().apply {
            mandatory.add(MediaConstraints.KeyValuePair("OfferToReceiveVideo", "true"))
            mandatory.add(MediaConstraints.KeyValuePair("OfferToReceiveAudio", "true"))
        }

        peerConnection?.createAnswer(object : SdpObserver {
            override fun onCreateSuccess(sdp: SessionDescription?) {
                sdp ?: return
                peerConnection?.setLocalDescription(object : SdpObserver {
                    override fun onSetSuccess() {
                        signalingServer.sendMessage(SignalingMessage(MessageType.ANSWER, sdp.description))
                        Log.i(tag, "Answer sent")
                    }
                    override fun onSetFailure(error: String?) { Log.e(tag, "setLocalDescription failed: $error") }
                    override fun onCreateSuccess(sdp: SessionDescription?) {}
                    override fun onCreateFailure(error: String?) {}
                }, sdp)
            }
            override fun onCreateFailure(error: String?) { Log.e(tag, "createAnswer failed: $error") }
            override fun onSetSuccess() {}
            override fun onSetFailure(error: String?) {}
        }, constraints)
    }

    fun dispose() {
        stopStatsLoop()
        ioScope.cancel()
        try { telemetryChannel?.dispose() } catch (_: Throwable) {}
        peerConnection?.close()
        peerConnectionFactory?.dispose()
        eglBase?.release()
        videoRenderer?.release()
    }

    private fun startStatsLoop() {
        if (statsRunning) return
        statsRunning = true
        scheduleStatsTick()
    }

    private fun stopStatsLoop() {
        statsRunning = false
    }

    private fun scheduleStatsTick() {
        ioScope.launch {
            kotlinx.coroutines.delay(3000L)
            if (!statsRunning) return@launch
            val pc = peerConnection ?: return@launch
            try {
                pc.getStats(RTCStatsCollectorCallback { report ->
                    handleStats(report)
                    if (statsRunning) scheduleStatsTick()
                })
            } catch (e: Throwable) {
                Log.w(tag, "getStats failed: $e")
                if (statsRunning) scheduleStatsTick()
            }
        }
    }

    private fun handleStats(report: RTCStatsReport) {
        var rttSec = -1.0
        for ((_, stat) in report.statsMap) {
            if (stat.type == "candidate-pair") {
                val members = stat.members
                val nominated = members["nominated"] as? Boolean ?: false
                val state = members["state"] as? String
                if (nominated || state == "succeeded") {
                    (members["currentRoundTripTime"] as? Number)?.let {
                        val v = it.toDouble()
                        if (v >= 0) rttSec = v
                    }
                }
            }
        }
        if (rttSec >= 0) {
            val ms = (rttSec * 1000.0).toLong()
            sendTelemetry(ms)
        }
    }

    private fun sendTelemetry(latencyMs: Long) {
        val ch = telemetryChannel ?: return
        if (!telemetryChannelOpen) return
        try {
            val json = """{"latency_ms":$latencyMs}"""
            val bytes = json.toByteArray(Charsets.UTF_8)
            ch.send(DataChannel.Buffer(java.nio.ByteBuffer.wrap(bytes), false))
        } catch (e: Throwable) {
            Log.w(tag, "sendTelemetry failed: $e")
        }
    }
}
