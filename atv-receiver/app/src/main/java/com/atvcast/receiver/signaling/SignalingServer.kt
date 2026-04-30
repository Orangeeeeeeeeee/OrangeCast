package com.atvcast.receiver.signaling

import android.util.Log
import com.google.gson.Gson
import org.java_websocket.WebSocket
import org.java_websocket.handshake.ClientHandshake
import org.java_websocket.server.WebSocketServer
import java.net.InetSocketAddress

interface SignalingListener {
    fun onOfferReceived(sdp: String)
    fun onIceCandidateReceived(candidate: String)
    fun onClientConnected()
    fun onClientDisconnected()
    fun onHeartbeat() {}
}

class SignalingServer(
    port: Int = 8765,
    private val pairingManager: PairingManager,
    private val trustedStore: TrustedDeviceStore,
    private val selfIdentity: LocalIdentity,
    private var listener: SignalingListener? = null
) : WebSocketServer(InetSocketAddress("0.0.0.0", port)) {

    init {
        isReuseAddr = true
        isTcpNoDelay = true
    }

    private val tag = "SignalingServer"
    private val gson = Gson()
    private var activeConnection: WebSocket? = null

    fun setListener(l: SignalingListener) {
        listener = l
    }

    override fun onOpen(conn: WebSocket, handshake: ClientHandshake) {
        // 单连接策略: 替换旧连接而不是拒绝新连接
        // 原因: 网络闪断时 TCP 半开,旧 conn 仍 isOpen 但已死,直接拒新连接会让客户端永远重连不上
        // 业内做法 (TeamViewer/AnyDesk): 新连接到来=客户端意图重连,主动关闭旧的、接受新的
        val old = activeConnection
        if (old != null && old.isOpen && old != conn) {
            try {
                old.close(1000, "Replaced by new connection from ${conn.remoteSocketAddress}")
                Log.w(tag, "Closed stale connection, accepting new from ${conn.remoteSocketAddress}")
            } catch (e: Exception) {
                Log.e(tag, "Failed to close stale connection: ${e.message}")
            }
            // 立即清理 activeConnection 引用,防止 onClose 异步回调误清新 conn
            activeConnection = null
        }
        Log.i(tag, "Client connected: ${conn.remoteSocketAddress}")
    }

    override fun onMessage(conn: WebSocket, message: String) {
        val msg = SignalingMessage.fromJson(message)
        if (msg == null) {
            Log.w(tag, "Invalid message: $message")
            return
        }

        when (msg.type) {
            MessageType.CONNECT_REQUEST -> handleConnectRequest(conn, msg.payload)
            MessageType.OFFER -> msg.payload?.let { listener?.onOfferReceived(it) }
            MessageType.ICE_CANDIDATE -> msg.payload?.let { listener?.onIceCandidateReceived(it) }
            MessageType.HEARTBEAT -> {
                conn.send(SignalingMessage(MessageType.HEARTBEAT).toJson())
                listener?.onHeartbeat()
            }
            MessageType.DISCONNECT -> handleDisconnect(conn)
            else -> Log.w(tag, "Unhandled message type: ${msg.type}")
        }
    }

    private fun handleConnectRequest(conn: WebSocket, payload: String?) {
        if (payload.isNullOrEmpty()) {
            reject(conn, "missing payload")
            return
        }

        val req = try {
            gson.fromJson(payload, ConnectRequestPayload::class.java)
        } catch (e: Exception) {
            // 兼容旧客户端: 纯 PIN 字符串
            val pinOnly = payload.trim().trim('"')
            if (pinOnly.length in 4..8 && pinOnly.all { it.isDigit() }) {
                if (pinOnly == pairingManager.getCurrentCode()) {
                    activeConnection = conn
                    conn.send(SignalingMessage(MessageType.CONNECT_ACCEPT).toJson())
                    listener?.onClientConnected()
                    Log.i(tag, "Legacy PIN accepted")
                    return
                }
            }
            reject(conn, "invalid payload")
            return
        }

        when (req.auth.type) {
            "pin" -> {
                if (req.auth.value == pairingManager.getCurrentCode()) {
                    val token = trustedStore.newToken()
                    trustedStore.upsert(TrustedDevice(req.deviceId, req.deviceName, token, System.currentTimeMillis()))
                    activeConnection = conn
                    val accept = ConnectAcceptPayload(token, selfIdentity.deviceId, selfIdentity.deviceName)
                    conn.send(SignalingMessage(MessageType.CONNECT_ACCEPT, gson.toJson(accept)).toJson())
                    listener?.onClientConnected()
                    Log.i(tag, "PIN accepted for ${req.deviceName} (${req.deviceId}), token issued")
                } else {
                    reject(conn, "wrong PIN")
                }
            }
            "token" -> {
                if (trustedStore.isTrusted(req.deviceId, req.auth.value)) {
                    activeConnection = conn
                    // 续期 token: 沿用旧 token,只更新 lastSeen
                    trustedStore.upsert(TrustedDevice(req.deviceId, req.deviceName, req.auth.value, System.currentTimeMillis()))
                    val accept = ConnectAcceptPayload(req.auth.value, selfIdentity.deviceId, selfIdentity.deviceName)
                    conn.send(SignalingMessage(MessageType.CONNECT_ACCEPT, gson.toJson(accept)).toJson())
                    listener?.onClientConnected()
                    Log.i(tag, "Token accepted for ${req.deviceName} (${req.deviceId})")
                } else {
                    reject(conn, "token expired or invalid")
                    Log.w(tag, "Token rejected for ${req.deviceId}")
                }
            }
            else -> reject(conn, "unknown auth type: ${req.auth.type}")
        }
    }

    private fun reject(conn: WebSocket, reason: String) {
        val payload = gson.toJson(ConnectRejectPayload(reason))
        conn.send(SignalingMessage(MessageType.CONNECT_REJECT, payload).toJson())
        Log.w(tag, "Rejected: $reason")
    }

    override fun onClose(conn: WebSocket, code: Int, reason: String, remote: Boolean) {
        // 仅在关闭的是当前 active conn 时清理,避免被替换的旧连接异步关闭误清新 conn
        if (conn == activeConnection) {
            handleDisconnect(conn)
        }
        Log.i(tag, "Connection closed code=$code reason=$reason remote=$remote")
    }

    override fun onStart() {
        Log.i(tag, "SignalingServer started on port ${address.port}")
    }

    override fun onError(conn: WebSocket?, ex: Exception) {
        Log.e(tag, "WebSocket error: ${ex.javaClass.simpleName}: ${ex.message}", ex)
    }

    fun sendMessage(msg: SignalingMessage) {
        activeConnection?.send(msg.toJson())
    }

    private fun handleDisconnect(conn: WebSocket) {
        if (conn == activeConnection) {
            activeConnection = null
            listener?.onClientDisconnected()
        }
    }
}
