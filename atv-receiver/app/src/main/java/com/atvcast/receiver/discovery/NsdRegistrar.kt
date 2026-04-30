package com.atvcast.receiver.discovery

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log

class NsdRegistrar(private val context: Context) {
    private val tag = "NsdRegistrar"
    private var nsdManager: NsdManager? = null
    private var listener: NsdManager.RegistrationListener? = null

    fun register(serviceName: String, port: Int, deviceId: String) {
        val info = NsdServiceInfo().apply {
            this.serviceName = serviceName
            this.serviceType = "_atvcast._tcp."
            this.port = port
            setAttribute("deviceId", deviceId)
            setAttribute("name", serviceName)
            setAttribute("ver", "1")
        }

        val l = object : NsdManager.RegistrationListener {
            override fun onServiceRegistered(srv: NsdServiceInfo) {
                Log.i(tag, "Registered: ${srv.serviceName} on port ${srv.port}")
            }
            override fun onRegistrationFailed(srv: NsdServiceInfo, errorCode: Int) {
                Log.e(tag, "Registration failed: $errorCode")
            }
            override fun onServiceUnregistered(srv: NsdServiceInfo) {
                Log.i(tag, "Unregistered: ${srv.serviceName}")
            }
            override fun onUnregistrationFailed(srv: NsdServiceInfo, errorCode: Int) {
                Log.e(tag, "Unregistration failed: $errorCode")
            }
        }
        listener = l

        try {
            nsdManager = (context.getSystemService(Context.NSD_SERVICE) as NsdManager).also {
                it.registerService(info, NsdManager.PROTOCOL_DNS_SD, l)
            }
        } catch (e: Exception) {
            Log.e(tag, "registerService failed: ${e.message}", e)
        }
    }

    fun unregister() {
        try { listener?.let { nsdManager?.unregisterService(it) } }
        catch (e: Exception) { Log.w(tag, "unregister failed: ${e.message}") }
        listener = null
    }
}
