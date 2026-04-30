# WebRTC 传输方案库选型报告

**任务**: T3 WebRTC 传输方案确认预研  
**日期**: 2026-04-24  
**范围**: Windows 端（C# .NET 8）+ Android 端（Kotlin, minSdk 26）  
**目标**: H.264 视频 + Opus 音频，WebRTC PeerConnection，WebSocket 自定义信令

---

## 一、Windows 端推荐

### ✅ 唯一推荐：SIPSorcery + SIPSorceryMedia.FFmpeg

| 属性 | 值 |
|------|-----|
| NuGet 包（核心） | `SIPSorcery` v8.0.23 |
| NuGet 包（编解码） | `SIPSorceryMedia.FFmpeg` v8.0.12 |
| 最新稳定版发布日期 | 2025-07-15（SIPSorcery 8.0.23） |
| .NET 8 兼容性 | ✅ 原生支持（.NET Standard 2.0 ~ .NET 10） |
| 协议支持 | WebRTC PeerConnection、ICE、DTLS-SRTP、SDP |
| 视频编解码 | H.264（via FFmpeg）、VP8 |
| 音频编解码 | Opus、G711、G722 |
| 维护状态 | 活跃（2026-02 完成单仓库合并，持续发布） |

#### 推荐理由

1. **集成复杂度最低**：纯 NuGet 引用，无需 P/Invoke 或 COM 桥接，直接 `dotnet add package`；提供标准 `RTCPeerConnection` API，适配自定义 WebSocket 信令无需额外适配层。

2. **H.264 支持**：通过 `SIPSorceryMedia.FFmpeg`（FFmpeg.AutoGen 绑定）支持 H.264 编解码；`SIPSorceryMedia.Windows` 提供屏幕/摄像头捕获。H.264 与 Opus 均在活跃测试中（参考 issue #1247，2024-12）。

3. **维护活跃度最高**：GitHub 星数 3.1k+，2026年2月完成子项目整合，持续跟进 .NET 版本（已支持 .NET 10）；社区活跃，issue 响应及时。

#### 已知限制（H.264 硬编 GPU 兼容性）

- **H.264 GPU 硬件编码尚未支持**：SIPSorceryMedia.FFmpeg 在 2023 年合并了硬件解码加速 PR（hwaccel），但**编码端的 GPU 加速（如 NVENC/AMF/QSV）尚未实现**——2024年8月 issue #58 中有开发者提问，官方无明确时间表。**当前只能使用 FFmpeg 软件编码**，在高分辨率（1080p@60fps）场景下 CPU 占用较高（建议使用 720p@30fps 或等待官方 hwaccel 编码支持）。
- FFmpeg 需要单独安装到系统 PATH（`winget install "FFmpeg (Shared)" --version 7.0` 或更高），不含在 NuGet 包内。

---

### 排除选项说明

| 选项 | 排除原因 |
|------|---------|
| `Microsoft.MixedReality.WebRTC` | 2022-03 官方宣布 Deprecated，仓库已 Archive，无任何更新，不可用 |
| GStreamer WebRTC Plugin（gstreamer-sharp） | gstreamer-sharp 最后更新 2021年；仅支持 MinGW 构建（不支持 MSVC）；需要 P/Invoke + 大量 native 依赖部署；集成复杂度极高，不适合纯 .NET 8 项目 |

---

## 二、Android 端推荐

### ✅ 唯一推荐：io.github.webrtc-sdk:android

| 属性 | 值 |
|------|-----|
| Maven 坐标 | `io.github.webrtc-sdk:android:144.7559.01` |
| Maven Central | https://mvnrepository.com/artifact/io.github.webrtc-sdk/android |
| 最新稳定版 | 144.7559.01（2026-03-13） |
| minSdk 要求 | API 21（兼容 minSdk 26） |
| Android 8.0（API 26）兼容性 | ✅ 支持 |
| 维护方 | LiveKit 团队（davidliu + cloudwebrtc） |
| 协议支持 | WebRTC 标准 PeerConnection API（org.webrtc.*） |
| 视频编解码 | H.264（MediaCodec 硬件）、VP8、VP9、H.265（v137+） |
| 音频编解码 | Opus |
| 发布频率 | 跟随 Google WebRTC Chromium milestone 版本，2025-2026 年持续更新 |

#### 推荐理由

1. **Android 8+ 兼容性最佳**：直接基于 Google WebRTC 官方源码编译的 AAR，对 Android API 26+ 的 MediaCodec API 支持完整，minSdk 低至 21，无兼容性适配负担。

2. **MediaCodec 深度集成**：直接调用 Android MediaCodec API 实现 H.264 硬件编解码，不依赖 FFmpeg 软件编码，CPU 占用低；提供 `HardwareVideoEncoderFactory` / `HardwareVideoDecoderFactory`，可直接传入 EGL Context 使用。

3. **维护活跃度最高**：Maven Central 已发布 32 个版本（2023~2026），跟随 Chromium M114/M125/M137/M144 里程碑版本更新，截至 2026-03 为最新版 144.7559.01；GitHub 仓库仍在活跃维护（最后 push 2026-03-13）。

4. **轻量、底层、无服务端绑定**：相比 LiveKit SDK（`io.livekit:livekit-android`），本库仅提供原始 `PeerConnectionFactory`/`PeerConnection` 等底层 API，**完全适配本项目的自定义 WebSocket 信令方案**，无需引入 LiveKit Server 依赖，包大小更小（AAR ~10MB vs LiveKit SDK 封装层）。

#### Gradle 集成

```kotlin
// settings.gradle.kts
dependencyResolutionManagement {
    repositories {
        mavenCentral()
    }
}

// app/build.gradle.kts
dependencies {
    implementation("io.github.webrtc-sdk:android:144.7559.01")
}
```

#### 已知限制（Android 8+ MediaCodec 已知问题）

1. **H.264 High Profile 与浏览器互操作问题**：部分 Android 设备（已知 Samsung Galaxy S22 等）的 MediaCodec H.264 编码器默认输出 High Profile Level 3.1，而 WebRTC 规范仅要求支持 Constrained Baseline/Main Profile——接收端（如 Windows Chrome）可能出现颜色偏差（绿/紫色调）。  
   **规避方案**：创建 `VideoEncoderFactory` 时禁用 H.264 High Profile：
   ```kotlin
   DefaultVideoEncoderFactory(eglContext, true, /* enableH264HighProfile = */ false)
   ```

2. **Android 8.0（API 26）极少数设备 MediaCodec 初始化失败**：已知某些 Vivo/Meizu Android 8 设备上，WebRTC native 层初始化 MediaCodec H.264 编码器时可能崩溃（与设备厂商 MediaCodec 驱动实现不规范有关，非 AAR 本身 Bug）。  
   **规避方案**：增加 `try/catch` fallback 逻辑，崩溃时切换 VP8 软件编码作为降级。

3. **H.265（HEVC）从 v137 开始支持**，但 Android 8 上部分老设备硬件解码器不支持 HEVC，不影响本项目（目标编码为 H.264）。

---

### 排除选项说明

| 选项 | 排除原因 |
|------|---------|
| `io.livekit:livekit-android` | 封装了 webrtc-sdk AAR，额外引入 LiveKit Room/Track/Server 抽象层；本项目使用自定义 WebSocket 信令，LiveKit 封装层为冗余依赖；包大小更大，底层 WebRTC API 访问受限；应直接使用底层 webrtc-sdk |
| 依赖 ExoPlayer 自定义 SRTP 解包（自研） | 需要自行实现 DTLS-SRTP 握手、ICE（STUN/TURN）、SDP 协商、RTP packetization 等完整 WebRTC 协议栈，工作量极大且难以维护，不具备实际可行性 |

---

## 三、选型汇总

| 端 | 推荐库 | 包标识 | 版本 |
|----|--------|--------|------|
| Windows（C# .NET 8） | SIPSorcery | `SIPSorcery` | 8.0.23 |
| Windows（编解码层） | SIPSorceryMedia.FFmpeg | `SIPSorceryMedia.FFmpeg` | 8.0.12 |
| Android（Kotlin, minSdk 26） | WebRTC Android SDK | `io.github.webrtc-sdk:android` | 144.7559.01 |

---

## 四、关键风险与缓解措施

| 风险 | 严重度 | 缓解措施 |
|------|--------|---------|
| Windows H.264 软件编码 CPU 占用高 | 中 | 限制分辨率 ≤ 720p@30fps；等待 SIPSorceryMedia.FFmpeg hwaccel 编码支持；或通过 FFmpeg CLI 管道调用 NVENC |
| Android H.264 High Profile 颜色兼容性 | 中 | 强制禁用 High Profile（`enableH264HighProfile=false`） |
| Android 8 极少数设备 MediaCodec 崩溃 | 低 | fallback 到 VP8 软件编码；minSdk 26 目标设备以 Android TV 为主，此类问题罕见 |
| SIPSorcery 版本号跨越（v8 → v10） | 低 | .NET 8 对应锁定 v8.0.x；v10.x 为 .NET 10 系列，不影响 .NET 8 项目 |

---

## 五、参考链接

- SIPSorcery NuGet: https://www.nuget.org/packages/SIPSorcery
- SIPSorceryMedia.FFmpeg NuGet: https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg
- SIPSorcery GitHub: https://github.com/sipsorcery-org/sipsorcery
- Microsoft.MixedReality.WebRTC Deprecation Notice: https://github.com/microsoft/MixedReality-WebRTC/issues/861
- webrtc-sdk/android GitHub: https://github.com/webrtc-sdk/android
- webrtc-sdk:android Maven Central: https://mvnrepository.com/artifact/io.github.webrtc-sdk/android
- H.264 High Profile issue (LiveKit): https://github.com/livekit/client-sdk-android/issues/176
- SIPSorcery hwaccel 编码 PR 讨论: https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg/pull/58
