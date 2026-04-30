package com.atvcast.receiver.signaling

import android.content.Context
import android.content.SharedPreferences
import android.os.Build
import android.provider.Settings
import android.util.Log
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.google.gson.Gson
import java.security.SecureRandom
import java.util.UUID

data class TrustedDevice(
    val deviceId: String,
    val deviceName: String,
    val token: String,
    val lastSeenUtc: Long
)

data class LocalIdentity(
    val deviceId: String,
    val deviceName: String
)

class TrustedDeviceStore(context: Context) {
    private val tag = "TrustedDeviceStore"
    private val gson = Gson()
    private val prefs: SharedPreferences = try {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()
        EncryptedSharedPreferences.create(
            context,
            "atvcast_trusted",
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    } catch (e: Exception) {
        Log.e(tag, "EncryptedSharedPreferences failed, fallback to plain prefs: ${e.message}")
        context.getSharedPreferences("atvcast_trusted_plain", Context.MODE_PRIVATE)
    }

    @Synchronized
    fun localIdentity(context: Context): LocalIdentity {
        val savedId = prefs.getString(KEY_SELF_ID, null)
        val savedName = prefs.getString(KEY_SELF_NAME, null)
        if (savedId != null && savedName != null) return LocalIdentity(savedId, savedName)

        @Suppress("HardwareIds")
        val androidId = try {
            Settings.Secure.getString(context.contentResolver, Settings.Secure.ANDROID_ID)
        } catch (_: Exception) { null }
        val suffix = androidId?.takeLast(8) ?: UUID.randomUUID().toString().substring(0, 8)
        val id = "${Build.MODEL.replace(' ', '-')}-$suffix"
        val name = Build.MODEL
        prefs.edit().putString(KEY_SELF_ID, id).putString(KEY_SELF_NAME, name).apply()
        return LocalIdentity(id, name)
    }

    @Synchronized
    fun isTrusted(deviceId: String, token: String): Boolean {
        val json = prefs.getString(devKey(deviceId), null) ?: return false
        return try {
            val dev = gson.fromJson(json, TrustedDevice::class.java)
            dev.token == token
        } catch (_: Exception) { false }
    }

    @Synchronized
    fun upsert(dev: TrustedDevice) {
        prefs.edit().putString(devKey(dev.deviceId), gson.toJson(dev)).apply()
    }

    @Synchronized
    fun remove(deviceId: String) {
        prefs.edit().remove(devKey(deviceId)).apply()
    }

    fun newToken(): String {
        val bytes = ByteArray(32)
        SecureRandom().nextBytes(bytes)
        return bytes.joinToString("") { "%02x".format(it) }
    }

    private fun devKey(deviceId: String) = "dev_$deviceId"

    companion object {
        private const val KEY_SELF_ID = "self_device_id"
        private const val KEY_SELF_NAME = "self_device_name"
    }
}
