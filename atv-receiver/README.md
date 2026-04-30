# atv-receiver

橙子投屏 Android 接收端。接收 Windows 端推送的屏幕画面和系统音频，通过 WebRTC 实时播放。

## 技术栈

| 组件 | 库 |
|---|---|
| 语言 | Kotlin · Android SDK 34 |
| WebRTC | io.github.webrtc-sdk:android 144.7559.01 |
| 信令 | Java-WebSocket 1.5.6（内嵌 WebSocket Server） |
| 设备发现 | Android NSD（mDNS），service type `_atvcast._tcp` |
| Token 存储 | EncryptedSharedPreferences（androidx.security-crypto） |
| 序列化 | Gson 2.10.1 |
| UI | Leanback · ConstraintLayout |

## 目录结构

```
app/src/main/java/com/atvcast/receiver/
├── connection/
│   └── CastingService.kt       投屏会话管理（启动/停止 WebRTC）
├── discovery/
│   ├── MdnsRegistrar.kt        mDNS 服务注册（NSD API 封装）
│   └── NsdRegistrar.kt         NSD 底层实现
├── signaling/
│   ├── SignalingServer.kt       WebSocket 信令服务器（监听 8765 端口）
│   ├── PairingManager.kt        PIN 配对逻辑
│   ├── TrustedDeviceStore.kt    已配对设备 Token 持久化
│   ├── AuthPayloads.kt          配对消息数据类
│   └── SignalingMessage.kt      信令消息格式
├── webrtc/
│   └── WebRtcReceiver.kt        WebRTC PeerConnection，视频/音频轨道渲染
└── MainActivity.kt              主界面，PIN 展示，连接状态
```

## 构建

**依赖**：Android Studio / Gradle，JDK 17

**release 构建（需 keystore）**：

1. 复制 `keystore.properties.template` 为 `keystore.properties`，填入真实密钥信息
2. 将 `.jks` 文件放到 `keystore/` 目录
3. 执行：

```bash
./gradlew assembleRelease
```

产物在 `app/build/outputs/apk/release/app-release.apk`

**debug 构建**：

```bash
./gradlew assembleDebug
# 包名后缀 .debug，可与 release 版共存安装
```

## 运行要求

- Android 8.0+（API 26+）
- 架构：arm64-v8a / armeabi-v7a（不支持 x86/x86_64）
- 与 Windows 端在同一局域网

## 工作流程

1. 启动后自动通过 mDNS 广播设备名称
2. WebSocket 信令服务器在 `:8765` 监听
3. Windows 端发现设备后发送 `CONNECT_REQUEST`，携带 4 位 PIN
4. PIN 验证通过后颁发 Token，后续免 PIN 自动连接
5. WebRTC PeerConnection 建立，接收视频/音频轨道并渲染

## 日志

debug 构建可通过 `adb logcat -s OrangeCast` 查看运行日志。
