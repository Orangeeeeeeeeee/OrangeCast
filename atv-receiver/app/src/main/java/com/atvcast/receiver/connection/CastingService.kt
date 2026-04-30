package com.atvcast.receiver.connection

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.Binder
import android.os.IBinder
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

class CastingService : Service() {

    inner class LocalBinder : Binder() {
        fun getService(): CastingService = this@CastingService
    }

    private val tag = "CastingService"
    private val channelId = "atvcast_channel"
    private val notificationId = 1001
    private val binder = LocalBinder()

    private var heartbeatJob: Job? = null
    private var lastHeartbeatMs = System.currentTimeMillis()
    private val serviceScope = CoroutineScope(Dispatchers.IO)

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        startForeground(notificationId, buildNotification("等待连接..."))
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        return START_STICKY
    }

    override fun onDestroy() {
        stopHeartbeatMonitor()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder = binder

    fun onIceConnected() {
        updateNotification("投屏中...")
        lastHeartbeatMs = System.currentTimeMillis()
        startHeartbeatMonitor()
    }

    fun onIceDisconnected() {
        stopHeartbeatMonitor()
        dispatchDisconnected()
    }

    fun onHeartbeatReceived() {
        lastHeartbeatMs = System.currentTimeMillis()
    }

    private fun startHeartbeatMonitor() {
        stopHeartbeatMonitor()
        heartbeatJob = serviceScope.launch {
            while (isActive) {
                delay(5000)
                val elapsed = System.currentTimeMillis() - lastHeartbeatMs
                if (elapsed > 15000) {
                    Log.w(tag, "Heartbeat timeout (${elapsed}ms), disconnecting")
                    dispatchDisconnected()
                    break
                }
            }
        }
    }

    private fun stopHeartbeatMonitor() {
        heartbeatJob?.cancel()
        heartbeatJob = null
    }

    private fun dispatchDisconnected() {
        updateNotification("等待连接...")
        sendBroadcast(Intent("com.atvcast.receiver.DISCONNECTED"))
    }

    fun updateNotification(text: String) {
        val nm = getSystemService(NOTIFICATION_SERVICE) as NotificationManager
        nm.notify(notificationId, buildNotification(text))
    }

    private fun buildNotification(text: String): Notification {
        return Notification.Builder(this, channelId)
            .setContentTitle("ATV Screen Cast")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.ic_media_play)
            .build()
    }

    private fun createNotificationChannel() {
        val channel = NotificationChannel(channelId, "ATV Cast", NotificationManager.IMPORTANCE_LOW)
        (getSystemService(NOTIFICATION_SERVICE) as NotificationManager).createNotificationChannel(channel)
    }
}
