# ── WebRTC (io.github.webrtc-sdk:android) ──────────────────────────────────
# Native JNI bridge classes must not be renamed or removed; the .so calls them
# by exact name at runtime.
-keep class org.webrtc.** { *; }
-dontwarn org.webrtc.**
# jni_zero is the JNI bootstrap used internally by the WebRTC .so (JNI_OnLoad
# looks up org.jni_zero.JniInit by name via JNIEnv::FindClass).
-keep class org.jni_zero.** { *; }
-dontwarn org.jni_zero.**

# ── Java-WebSocket ──────────────────────────────────────────────────────────
# Reflection-free but ships with javax.websocket stubs that are absent on Android.
-keep class org.java_websocket.** { *; }
-dontwarn org.java_websocket.**
-dontwarn javax.websocket.**

# ── Gson ─────────────────────────────────────────────────────────────────────
# Data classes serialised/deserialised via Gson need field names preserved.
-keepattributes Signature
-keepattributes *Annotation*
-keep class com.google.gson.** { *; }
-keep class * implements com.google.gson.TypeAdapterFactory
-keep class * implements com.google.gson.JsonSerializer
-keep class * implements com.google.gson.JsonDeserializer
# Preserve our own signaling message model so Gson field mapping is stable.
-keep class com.atvcast.receiver.signaling.** { *; }
# IceCandidateDto is an inner data class used by Gson; R8 must not rename it.
-keep class com.atvcast.receiver.webrtc.WebRtcReceiver$IceCandidateDto { *; }

# ── androidx.security.crypto / Tink ──────────────────────────────────────────
# EncryptedSharedPreferences uses Tink internally via reflection; all Tink
# primitives, key types, and registry entries must survive shrinking or the
# AES256-GCM key scheme will fail to initialize and stored tokens become unreadable.
-keep class androidx.security.crypto.** { *; }
-dontwarn androidx.security.crypto.**
-keep class com.google.crypto.tink.** { *; }
-dontwarn com.google.crypto.tink.**
-dontwarn javax.annotation.**
-dontwarn javax.annotation.concurrent.**

# ── Kotlin coroutines / metadata ─────────────────────────────────────────────
-keepattributes RuntimeVisibleAnnotations
-dontwarn kotlinx.coroutines.**
