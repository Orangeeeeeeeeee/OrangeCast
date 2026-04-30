package com.atvcast.receiver

import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.ServiceConnection
import android.os.Bundle
import android.os.IBinder
import android.view.KeyEvent
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.atvcast.receiver.connection.CastingService
import com.atvcast.receiver.discovery.NsdRegistrar
import com.atvcast.receiver.signaling.MessageType
import com.atvcast.receiver.signaling.PairingManager
import com.atvcast.receiver.signaling.SignalingListener
import com.atvcast.receiver.signaling.SignalingMessage
import com.atvcast.receiver.signaling.SignalingServer
import com.atvcast.receiver.signaling.TrustedDeviceStore
import com.atvcast.receiver.webrtc.WebRtcReceiver
import org.webrtc.PeerConnection
import org.webrtc.SurfaceViewRenderer
import java.net.Inet4Address
import java.net.NetworkInterface

class MainActivity : AppCompatActivity(), SignalingListener {

    private lateinit var signalingServer: SignalingServer
    private lateinit var pairingManager: PairingManager
    private lateinit var webRtcReceiver: WebRtcReceiver
    private lateinit var trustedStore: TrustedDeviceStore
    private lateinit var nsdRegistrar: NsdRegistrar

    private lateinit var waitingLayout: android.view.View
    private lateinit var errorLayout: android.view.View
    private lateinit var surfaceView: SurfaceViewRenderer
    private lateinit var ipText: TextView
    private lateinit var pairingCodeText: TextView
    private lateinit var statusText: TextView
    private lateinit var errorMessage: TextView
    private lateinit var pinDigits: Array<TextView>

    private val signalingPort: Int get() = 8765

    private var castingService: CastingService? = null
    private val serviceConnection = object : ServiceConnection {
        override fun onServiceConnected(name: ComponentName?, service: IBinder?) {
            castingService = (service as CastingService.LocalBinder).getService()
            if (webRtcReceiver.isConnected) castingService?.onIceConnected()
        }
        override fun onServiceDisconnected(name: ComponentName?) {
            castingService = null
        }
    }

    private val disconnectReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            showWaitingState()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        setContentView(R.layout.activity_main)

        waitingLayout = findViewById(R.id.waitingLayout)
        errorLayout = findViewById(R.id.errorLayout)
        surfaceView = findViewById(R.id.surfaceView)
        ipText = findViewById(R.id.ipText)
        pairingCodeText = findViewById(R.id.pairingCodeText)
        statusText = findViewById(R.id.statusText)
        errorMessage = findViewById(R.id.errorMessage)
        pinDigits = arrayOf(
            findViewById(R.id.pinDigit1),
            findViewById(R.id.pinDigit2),
            findViewById(R.id.pinDigit3),
            findViewById(R.id.pinDigit4)
        )

        pairingManager = PairingManager()
        trustedStore = TrustedDeviceStore(this)
        val identity = trustedStore.localIdentity(this)
        renderPairingCode(pairingManager.getCurrentCode())
        val ip = getLocalIpAddress()
        ipText.text = if (ip == getString(R.string.ip_unknown)) ip else "$ip:$signalingPort"
        android.util.Log.i("MainActivity", "===== ATV Cast Ready =====")
        android.util.Log.i("MainActivity", "IP: $ip")
        android.util.Log.i("MainActivity", "DEVICE: ${identity.deviceName} (${identity.deviceId})")
        android.util.Log.i("MainActivity", "PAIRING_CODE: ${pairingManager.getCurrentCode()}")
        android.util.Log.i("MainActivity", "==========================")

        signalingServer = SignalingServer(signalingPort, pairingManager, trustedStore, identity, this)
        try {
            signalingServer.start()
            android.util.Log.i("MainActivity", "SignalingServer.start() called")
        } catch (e: Exception) {
            android.util.Log.e("MainActivity", "SignalingServer start failed", e)
        }

        nsdRegistrar = NsdRegistrar(this)
        nsdRegistrar.register(identity.deviceName, signalingPort, identity.deviceId)

        webRtcReceiver = WebRtcReceiver(this, signalingServer)
        webRtcReceiver.initialize(surfaceView)
        webRtcReceiver.onVideoTrackReceived = { showCasting() }
        webRtcReceiver.onIceConnectionStateChanged = { state ->
            when (state) {
                PeerConnection.IceConnectionState.CONNECTED,
                PeerConnection.IceConnectionState.COMPLETED -> {
                    castingService?.onIceConnected() ?: android.util.Log.w("MainActivity", "castingService null on ICE CONNECTED")
                }
                PeerConnection.IceConnectionState.DISCONNECTED,
                PeerConnection.IceConnectionState.FAILED,
                PeerConnection.IceConnectionState.CLOSED -> {
                    castingService?.onIceDisconnected() ?: showWaitingState()
                }
                else -> {}
            }
        }

        val serviceIntent = Intent(this, CastingService::class.java)
        startService(serviceIntent)
        bindService(serviceIntent, serviceConnection, Context.BIND_AUTO_CREATE)

        registerReceiver(disconnectReceiver, IntentFilter("com.atvcast.receiver.DISCONNECTED"))
    }

    override fun onDestroy() {
        super.onDestroy()
        try { nsdRegistrar.unregister() } catch (_: Exception) {}
        signalingServer.stop()
        webRtcReceiver.dispose()
        try { unbindService(serviceConnection) } catch (_: Exception) {}
        try { unregisterReceiver(disconnectReceiver) } catch (_: Exception) {}
    }

    override fun onKeyDown(keyCode: Int, event: KeyEvent?): Boolean {
        if (keyCode == KeyEvent.KEYCODE_BACK) {
            showWaitingState()
            return true
        }
        return super.onKeyDown(keyCode, event)
    }

    override fun onClientConnected() = runOnUiThread {
        statusText.text = getString(R.string.status_connected)
    }

    override fun onClientDisconnected() = runOnUiThread {
        showWaitingState()
    }

    override fun onOfferReceived(sdp: String) {
        webRtcReceiver.handleOffer(sdp)
    }

    override fun onIceCandidateReceived(candidate: String) {
        webRtcReceiver.handleIceCandidate(candidate)
    }

    override fun onHeartbeat() {
        castingService?.onHeartbeatReceived()
    }

    fun showWaitingState() {
        runOnUiThread {
            pairingManager.generateCode()
            renderPairingCode(pairingManager.getCurrentCode())
            android.util.Log.i("MainActivity", "PAIRING_CODE: ${pairingManager.getCurrentCode()}")
            waitingLayout.visibility = android.view.View.VISIBLE
            errorLayout.visibility = android.view.View.GONE
            statusText.text = getString(R.string.status_waiting)
            showSystemBars()
        }
    }

    fun showError(title: String, message: String) {
        runOnUiThread {
            waitingLayout.visibility = android.view.View.GONE
            errorLayout.visibility = android.view.View.VISIBLE
            findViewById<TextView>(R.id.errorTitle).text = title
            errorMessage.text = message
            showSystemBars()
        }
    }

    fun showCasting() {
        runOnUiThread {
            waitingLayout.visibility = android.view.View.GONE
            errorLayout.visibility = android.view.View.GONE
            hideSystemBars()
        }
    }

    private fun hideSystemBars() {
        val controller = WindowInsetsControllerCompat(window, window.decorView)
        controller.hide(WindowInsetsCompat.Type.systemBars())
        controller.systemBarsBehavior =
            WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
    }

    private fun showSystemBars() {
        WindowInsetsControllerCompat(window, window.decorView)
            .show(WindowInsetsCompat.Type.systemBars())
    }

    private fun renderPairingCode(code: String) {
        val padded = code.padEnd(4, '·').take(4)
        for (i in pinDigits.indices) {
            pinDigits[i].text = padded[i].toString()
        }
        pairingCodeText.text = code
    }

    private fun getLocalIpAddress(): String {
        try {
            val interfaces = NetworkInterface.getNetworkInterfaces()
            for (intf in interfaces) {
                for (addr in intf.inetAddresses) {
                    if (!addr.isLoopbackAddress && addr is Inet4Address) {
                        return addr.hostAddress ?: getString(R.string.ip_unknown)
                    }
                }
            }
        } catch (e: Exception) {
        }
        return getString(R.string.ip_unknown)
    }
}
