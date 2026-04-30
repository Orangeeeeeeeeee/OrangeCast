## [T1] Android 工程初始化
- gradle 版本：8.7
- 构建结果：需要 Android SDK/JDK 环境才能实际编译

## [T2] Miracast 可行性结论
- 结论：NO-GO（普通 App）/ CONDITIONAL（系统签名）
- 原因摘要：
  1. Android 8.0+ 封锁 `setWFDInfo()`，`CONFIGURE_WIFI_DISPLAY` 权限为 signatureOrSystem，第三方 App 无法获取
  2. WifiDisplayManager 全部 @hide + @UnsupportedAppUsage，Android 9+ 黑名单 API，反射失效
  3. 主流目标设备（Chromecast with Google TV、Sony Bravia 2020+）已不支持 Miracast，放弃转向 Google Cast/AirPlay
  4. Windows 11 WFD Source 使用 Wi-Fi Direct P2P + RTSP:7236 + RTP(H.264/AAC)；同网络时优先 MS-MICE(TCP:7250)
  5. 建议：A 轨 Miracast 放弃，专注 B 轨（自定义协议/WebSocket）

## [T3] WebRTC 库选型
- Windows: SIPSorcery v8.0.23 + SIPSorceryMedia.FFmpeg v8.0.12
- Android: io.github.webrtc-sdk:android v144.7559.01
- 关键限制：
  - Windows H.264 仅软件编码（FFmpeg），GPU 硬编码未支持，建议限制 ≤720p@30fps
  - Android H.264 High Profile 与 Chrome 有颜色兼容性问题，需禁用（enableH264HighProfile=false）
  - Android 8.0 极少数设备 MediaCodec 崩溃，需 fallback 到 VP8
  - Microsoft.MixedReality.WebRTC 已于 2022-03 废弃，不可用
  - LiveKit Android SDK 封装过重，直接用底层 webrtc-sdk AAR

## [T4] Windows 采集技术验证
- DXGI Desktop Duplication：可行，普通用户权限，Win8+，帧率受显示器刷新率限制
- WASAPI loopback：可行，普通用户权限，48000Hz/立体声，静音时需填充静音帧
- 已知限制：UAC 安全桌面黑屏、HDCP 黑块、独占全屏 DXGI_ERROR_ACCESS_LOST（需重新初始化）
- 推荐编码管道：DXGI BGRA → FFmpeg YUV420P → libx264 软编 H.264 → SIPSorcery RTP
- 音频管道：WASAPI PCM → SIPSorcery Opus → WebRTC 音频轨道
- NAudio 库（v2.2.1）可简化 WASAPI loopback 实现（WasapiLoopbackCapture 类）

## [T5] 信令服务
- SignalingServer 继承 org.java_websocket.server.WebSocketServer
- 单连接限制：activeConnection 字段，重复连接直接 close(1008)
- 配对流程：CONNECT_REQUEST(code) → PairingManager.validateCode → ACCEPT/REJECT
- 断连后调用 pairingManager.resetAfterPairing() 重新生成连接码
- Gson 2.10.1 用于 JSON 序列化

## [T6] Android TV UI 骨架
- 布局：FrameLayout 叠加 waitingLayout + SurfaceView + statusBar
- IP 获取：NetworkInterface.getNetworkInterfaces() 枚举，取第一个非 loopback IPv4
- Back 键：onKeyDown KEYCODE_BACK → showWaitingState()
- SurfaceView 留给 T9 集成 WebRTC 视频渲染

## [T7] Windows 发送端框架
- CLI 框架：System.CommandLine（.NET 内置）
- 依赖：SIPSorcery 8.0.23, SIPSorceryMedia.FFmpeg 8.0.12, NAudio 2.2.1, Makaretu.Dns.Multicast
- 发布：dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true
- --simulate-bw 参数预留给 T12 ABR 验证

## [T8] 局域网发现模块
- Android：NsdManager 注册 _atvcast._tcp，端口 8765
- Windows：Makaretu.Dns.Multicast 的 ServiceDiscovery 扫描
- mDNS 服务类型：_atvcast._tcp
- 手动 IP 备选：--connect <ip:port>
