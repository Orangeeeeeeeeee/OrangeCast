# win-sender

橙子投屏 Windows 投屏端。将本机屏幕和系统音频通过 WebRTC 实时推流到 Android 接收端。

## 技术栈

| 组件 | 库 |
|---|---|
| 框架 | C# .NET 8 · WinForms |
| WebRTC | SIPSorcery 8.0.23 |
| 硬件编码 | SIPSorceryMedia.Encoders（NVENC / QSV / AMF） |
| 屏幕采集 | DXGI Desktop Duplication（`Capture/ScreenCapture.cs`） |
| 音频采集 | NAudio WASAPI loopback（`Audio/SystemAudioCapture.cs`） |
| 视频处理 | FFmpeg 7.0.x（avcodec-61 / avformat-61 / avutil-59） |
| 设备发现 | mDNS（Makaretu.Dns.Multicast），service type `_atvcast._tcp` |
| SVG 图标 | Svg.NET 3.4.7，Lucide 图标集 |
| 日志 | Serilog → 文件 + 控制台 |

## 目录结构

```
src/WinSender/
├── Abr/            自适应码率控制
├── Audio/          系统音频采集（WASAPI loopback）
├── Capture/        屏幕采集（DXGI）
├── Diagnostics/    日志初始化
├── Discovery/      mDNS 设备发现
├── Settings/       编码器配置（分辨率、码率、硬件加速）
├── Signaling/      WebSocket 信令、PIN 配对、Token 存储
├── UI/             WinForms 界面
│   └── Controls/   自定义控件（设备卡片、IP 输入框、设置弹窗等）
├── WebRTC/         WebRTC 推流、硬件编码器检测、帧处理
├── Assets/         图标（SVG）、应用图标（ICO）
└── Program.cs      入口，单实例 Mutex，CLI 参数解析
```

## 构建

**依赖**：.NET 8 SDK、Windows 10 1809+ x64

```bash
dotnet build src/WinSender/WinSender.csproj -c Release
```

**发布（单文件，framework-dependent）**：

```bash
dotnet publish src/WinSender/WinSender.csproj -c Release -r win-x64
```

发布产物在 `bin/Release/net8.0-windows10.0.17763.0/win-x64/publish/`，包含：
- `OrangeCast.exe`（~39 MB）
- FFmpeg DLL：`avcodec-61.dll` / `avformat-61.dll` / `avutil-59.dll` / `swscale-8.dll` / `swresample-5.dll` / `vpxmd.dll`
- `Assets/` 图标目录

> 目标机器无 .NET 8 Desktop Runtime 时，Windows 会自动弹窗引导安装。

## 运行

```bash
# GUI 模式（默认）
OrangeCast.exe

# 发现局域网设备
OrangeCast.exe --discover

# 手动连接
OrangeCast.exe --connect 192.168.1.100:8765 --code 1234

# 屏幕采集测试
OrangeCast.exe --test-capture --duration 5
```

## 日志

运行日志写入 `%LocalAppData%\OrangeCast\logs\`，按日期轮转。
