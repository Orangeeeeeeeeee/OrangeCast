package com.atvcast.receiver.discovery

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.util.Log

class MdnsRegistrar(private val context: Context) {

    private val tag = "MdnsRegistrar"
    private val serviceType = "_atvcast._tcp"
    private var nsdManager: NsdManager? = null
    private var registrationListener: NsdManager.RegistrationListener? = null

    fun register(serviceName: String, port: Int) {
        val serviceInfo = NsdServiceInfo().apply {
            this.serviceName = serviceName
            this.serviceType = this@MdnsRegistrar.serviceType
            this.port = port
        }

        registrationListener = object : NsdManager.RegistrationListener {
            override fun onRegistrationFailed(info: NsdServiceInfo, errorCode: Int) {
                Log.e(tag, "mDNS registration failed: $errorCode")
            }

            override fun onUnregistrationFailed(info: NsdServiceInfo, errorCode: Int) {
                Log.e(tag, "mDNS unregistration failed: $errorCode")
            }

            override fun onServiceRegistered(info: NsdServiceInfo) {
                Log.i(tag, "mDNS registered: ${info.serviceName}")
            }

            override fun onServiceUnregistered(info: NsdServiceInfo) {
                Log.i(tag, "mDNS unregistered: ${info.serviceName}")
            }
        }

        nsdManager = (context.getSystemService(Context.NSD_SERVICE) as NsdManager).also {
            it.registerService(serviceInfo, NsdManager.PROTOCOL_DNS_SD, registrationListener)
        }
    }

    fun unregister() {
        registrationListener?.let { nsdManager?.unregisterService(it) }
    }
}
