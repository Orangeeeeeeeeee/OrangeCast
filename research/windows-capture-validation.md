# Windows 屏幕采集 + 系统音频采集技术验证

## 结论摘要

| 技术 | 可行性 | 权限要求 | 主要限制 |
|------|--------|----------|----------|
| DXGI Desktop Duplication | ✅ 可行 | 普通用户 | UAC 桌面黑屏、HDCP 黑块 |
| WASAPI loopback capture | ✅ 可行 | 普通用户 | 静音时无数据需填充静音帧 |

---

## 1. DXGI Desktop Duplication API

### 原理

DXGI Desktop Duplication（`IDXGIOutputDuplication`）是 Windows 8+ 提供的官方屏幕采集接口，通过 Direct3D 11 共享纹理实现零拷贝整屏截帧。

**采集流程：**
```
D3D11CreateDevice()
  → IDXGIDevice → IDXGIAdapter → IDXGIOutput1
  → IDXGIOutput1::DuplicateOutput()
  → IDXGIOutputDuplication::AcquireNextFrame()
  → ID3D11Texture2D (BGRA / DXGI_FORMAT_B8G8R8A8_UNORM)
  → CopyResource() 到 CPU 可读 Staging Texture
  → Map() 读取像素数据
  → ReleaseFrame()
```

### 帧率上限

- 受显示器刷新率限制，通常 60fps（144Hz 显示器可达 144fps）
- `AcquireNextFrame(timeout)` 阻塞等待下一帧，timeout 建议 33ms（30fps）或 16ms（60fps）
- 实际投屏建议锁定 30fps，降低 CPU/GPU 负载

### 支持能力

- ✅ 主屏整屏采集（含鼠标光标，可选）
- ✅ 多显示器（通过枚举 `IDXGIAdapter::EnumOutputs` 选择主屏）
- ✅ 普通用户权限，无需管理员
- ✅ Windows 8 / 10 / 11 均支持

### 已知限制

| 场景 | 行为 | 首版处理 |
|------|------|----------|
| UAC 提权对话框 / 安全桌面 | 采集帧为**全黑**（系统安全隔离） | 记录限制，不解决 |
| HDCP 保护内容（蓝光播放器、部分流媒体） | 受保护区域显示**黑块** | 记录限制，不解决 |
| 独占全屏游戏（D3D Exclusive Mode） | 部分游戏可能导致采集失败或黑屏 | 记录限制，不解决 |
| 远程桌面会话（RDP） | `DuplicateOutput` 返回 `DXGI_ERROR_UNSUPPORTED` | 记录限制，不解决 |
| 系统休眠/锁屏唤醒 | 需重新初始化 `IDXGIOutputDuplication` | 需在发送端实现重连逻辑 |

### 推荐编码管道

由于 SIPSorcery H.264 仅支持 FFmpeg 软件编码（无 GPU 硬编），推荐以下管道：

```
DXGI AcquireNextFrame
  → ID3D11Texture2D (BGRA)
  → CPU Map → BGRA 像素数据
  → FFmpeg sws_scale: BGRA → YUV420P（或 NV12）
  → FFmpeg avcodec_encode_video2: H.264 软件编码（libx264）
  → SIPSorcery RTP 封包发送
```

**编码参数建议（软编 CPU 可控）：**
- 分辨率：720p（1280×720）@ 30fps（首版，CPU 占用约 15-25%）
- 码率：2-4 Mbps（CBR，低延迟模式）
- 编码预设：`ultrafast` 或 `superfast`（降低延迟）
- GOP：30（每秒一个关键帧，便于快速恢复）
- 颜色格式：YUV420P

---

## 2. WASAPI Loopback Capture

### 原理

WASAPI（Windows Audio Session API）loopback 模式通过 `AUDCLNT_STREAMFLAGS_LOOPBACK` 标志采集系统渲染端点（扬声器/耳机）的输出音频流，即"听到什么就采集什么"。

**采集流程：**
```
CoCreateInstance(CLSID_MMDeviceEnumerator)
  → IMMDeviceEnumerator::GetDefaultAudioEndpoint(eRender, eConsole)
  → IMMDevice::Activate(IAudioClient)
  → IAudioClient::Initialize(AUDCLNT_SHAREMODE_SHARED,
                              AUDCLNT_STREAMFLAGS_LOOPBACK, ...)
  → IAudioClient::GetService(IAudioCaptureClient)
  → IAudioClient::Start()
  → 循环 IAudioCaptureClient::GetBuffer() → 处理 PCM 数据 → ReleaseBuffer()
```

### 音频格式

- 采样率：通常 48000 Hz（系统默认），部分设备 44100 Hz
- 位深：16-bit 或 32-bit float（取决于系统设置）
- 声道：立体声（2 声道）
- 建议：初始化时查询 `WAVEFORMATEX`，按实际格式采集，再重采样到 48000Hz/16bit/2ch

### 权限要求

- ✅ **普通用户权限即可**，无需管理员或特殊权限
- ✅ Windows Vista+ 均支持（Windows 10/11 完全支持）

### 已知限制

| 场景 | 行为 | 处理方式 |
|------|------|----------|
| 系统完全静音 | `GetBuffer` 返回 `AUDCLNT_S_BUFFER_EMPTY`，无数据 | 填充静音帧（全零 PCM）发送，保持 RTP 时间戳连续 |
| 音频设备切换（插拔耳机） | `IAudioClient` 失效，需重新初始化 | 监听 `IMMNotificationClient::OnDefaultDeviceChanged`，重建采集链 |
| 独占模式音频（ASIO/WASAPI Exclusive） | loopback 无法采集独占模式输出 | 记录限制，首版不解决 |

### 推荐编码管道

```
WASAPI loopback PCM（48000Hz / 16bit / 立体声）
  → SIPSorcery Opus 编码器（内置）
  → RTP 封包（payload type 111，Opus）
  → WebRTC 音频轨道发送
```

**编码参数：**
- 编码器：Opus（SIPSorcery 内置，低延迟模式）
- 采样率：48000 Hz
- 码率：64-128 kbps（立体声）
- 帧长：20ms（Opus 标准帧）

---

## 3. UAC / 安全桌面场景

- **现象**：当 Windows 显示 UAC 提权对话框时，系统切换到"安全桌面"（Secure Desktop），DXGI 采集帧变为全黑
- **原因**：安全桌面运行在独立的 Session 0 隔离环境，普通进程无法访问
- **首版处理**：记录为已知限制，不解决。TV 端显示黑屏属预期行为

---

## 4. 独占全屏游戏 / HDCP 场景

- **独占全屏游戏**：部分 D3D 游戏使用独占全屏模式，`AcquireNextFrame` 可能返回 `DXGI_ERROR_ACCESS_LOST`，需重新初始化采集链
- **HDCP 保护内容**：受 HDCP 保护的视频（蓝光、部分流媒体 DRM）在采集帧中显示为黑块
- **首版处理**：记录为已知限制，不解决

---

## 5. 推荐技术选型总结

| 组件 | 选型 | 理由 |
|------|------|------|
| 屏幕采集 | DXGI Desktop Duplication | 官方 API，零拷贝，普通权限，Win8+ |
| 音频采集 | WASAPI loopback | 官方 API，普通权限，系统全局音频 |
| 视频编码 | FFmpeg libx264（软件编码） | SIPSorcery 无 GPU 硬编支持，软编可控 |
| 音频编码 | Opus（SIPSorcery 内置） | 低延迟，WebRTC 标准 |
| 分辨率 | 720p @ 30fps（首版） | 软编 CPU 可控，延迟可控 |
| 视频码率 | 2-4 Mbps（CBR） | 局域网带宽充足，低延迟优先 |
| 音频码率 | 64-128 kbps | Opus 立体声标准 |

---

## 参考资料

- [DXGI Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api)
- [WASAPI Loopback Recording](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
- [SIPSorcery WebRTC](https://github.com/sipsorcery-org/sipsorcery)
- [SIPSorceryMedia.FFmpeg](https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg)
