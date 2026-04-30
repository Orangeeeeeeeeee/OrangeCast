package com.atvcast.receiver.signaling

data class AuthInfo(val type: String, val value: String)

data class ConnectRequestPayload(
    val deviceId: String,
    val deviceName: String,
    val auth: AuthInfo
)

data class ConnectAcceptPayload(
    val token: String,
    val deviceId: String,
    val deviceName: String
)

data class ConnectRejectPayload(val reason: String)
