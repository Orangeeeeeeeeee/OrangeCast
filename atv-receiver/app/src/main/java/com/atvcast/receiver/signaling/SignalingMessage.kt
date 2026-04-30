package com.atvcast.receiver.signaling

import com.google.gson.Gson

enum class MessageType {
    CONNECT_REQUEST, CONNECT_ACCEPT, CONNECT_REJECT,
    OFFER, ANSWER, ICE_CANDIDATE,
    DISCONNECT, HEARTBEAT
}

data class SignalingMessage(
    val type: MessageType,
    val payload: String? = null
) {
    fun toJson(): String = Gson().toJson(this)

    companion object {
        fun fromJson(json: String): SignalingMessage? = try {
            Gson().fromJson(json, SignalingMessage::class.java)
        } catch (e: Exception) {
            null
        }
    }
}
