# Miracast / Wi-Fi Display (WFD) 可行性深度研究

**结论：NO-GO（普通 App 权限下）/ CONDITIONAL（系统签名或替代协议方案）**

---

## 1. Windows 11 "连接到无线显示器" 底层协议

Windows 11 的"连接到无线显示器"功能使用两条路径：

### 1.1 标准 Miracast（Wi-Fi Direct 模式）
- **发现阶段**：Windows 充当 WFD Source，通过 Wi-Fi Direct P2P Probe Request/Probe Response 帧发现 Sink 设备。Sink 在 Wi-Fi Direct 帧中通过 WFD IE（Information Element）广播自身能力（设备类型 PRIMARY_SINK 或 SOURCE_OR_PRIMARY_SINK）。
- **连接阶段**：建立 Wi-Fi Direct 点对点网络（P2P Group）。
- **流媒体阶段**：
  - RTSP 控制通道：端口 7236（能力协商、分辨率、编解码器）
  - RTP 媒体流：H.264 Baseline/High Profile 视频 + AAC/LPCM 音频
  - 可选 UIBC（User Input Back Channel）：将 Sink 端的触摸/鼠标事件回传给 Source

### 1.2 Miracast over Infrastructure（MS-MICE 协议）
当 Source 和 Sink 在同一 Wi-Fi 网络时，Windows 优先尝试：
- 通过 mDNS/Beacon 发现支持 MS-MICE 的 Sink
- TCP 端口 7250 协商：SOURCE_READY / STOP_PROJECTION / SECURITY_HANDSHAKE（DTLS 可选加密）
- 回退策略：若 MS-MICE 握手失败则自动降级为标准 Wi-Fi Direct Miracast

**官方参考**：
- [Microsoft Learn - Supporting Miracast wireless displays](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/wireless-displays--miracast-)
- [MS-MICE 协议规范](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-mice/9598ca72-d937-466c-95f6-70401bb10bdb)
- [Wireless Projection over existing network](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/wireless-projection-implementing-over-existing-network)

---

## 2. Android 8+ 普通 App 能否实现 WFD Sink（接收端）

### 2.1 结论：**不能**（Android 8.0 起）

在 Android 8.0（API 26）之前，曾有开发者通过反射调用 `WifiP2pManager.setWFDInfo()` 实现 Sink 端，但需要自定义系统/修改权限级别（如修改 CyanogenMod）。

**Android 8.0 起的封锁**：
- `WifiP2pManager.setWFDInfo()` 内部调用 `checkConfigureWifiDisplayPermission()`
- 该检查要求 `android.permission.CONFIGURE_WIFI_DISPLAY` 权限
- 此权限的 `protectionLevel` 为 **`signatureOrSystem`**（系统签名级），普通第三方 App 无法声明

运行时报错：
```
SecurityException: Wifi Display Permission denied for uid = 10104
```

**AOSP 源码验证**（`WifiP2pServiceImpl.java`）：
```java
boolean getWfdPermission(int uid) {
    return wifiPermissionsWrapper.getUidPermission(
        android.Manifest.permission.CONFIGURE_WIFI_DISPLAY, uid)
            != PackageManager.PERMISSION_DENIED;
}
```

实际影响：AirScreen 等主流投屏软件在 Android 8+ 上已弹出提示"Miracast WFD Sink 功能已被 Google 禁用"。

**AOSP 代码引用**：
- [WifiDisplayController.java（AOSP main）](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/display/WifiDisplayController.java)
- [Miracast技术详解（五）：Permission 问题处理](https://codezjx.com/posts/miracast-permission-issues/)

### 2.2 WFD Sink 所需的完整权限列表

| 权限 | 保护级别 | 普通 App 可获取 |
|------|----------|----------------|
| `CONFIGURE_WIFI_DISPLAY` | signatureOrSystem | ❌ 否 |
| `CONTROL_WIFI_DISPLAY` | signatureOrSystem | ❌ 否 |
| `ACCESS_FINE_LOCATION` | dangerous（运行时） | ✅ 是（但不够） |
| `ACCESS_WIFI_STATE` | normal | ✅ 是 |
| `CHANGE_WIFI_STATE` | normal | ✅ 是 |

---

## 3. WifiDisplayManager 系统隐藏 API 分析

### 3.1 API 状态

`android.hardware.display.DisplayManager`（公开类）中与 WFD 相关的方法：

```java
/** @hide */
@UnsupportedAppUsage
public void startWifiDisplayScan() { ... }

/** @hide */
@UnsupportedAppUsage
public void connectWifiDisplay(String deviceAddress) { ... }
```

- 所有 WFD 相关方法均标注 `@hide`
- `@UnsupportedAppUsage` 注解意味着在 Android 9+ 的非系统应用中调用会被 StrictMode 拦截（黑名单 API）
- 反射调用这些方法在 targetSdkVersion >= 28（Android 9）时会产生 `NoSuchMethodException` 或静默失败
- `WifiDisplayManager` 类本身也是 `@hide`，无法从公开 SDK 访问

**官方参考**：
- [DisplayManager.java AOSP源码（含@hide WFD方法）](https://android.googlesource.com/platform/frameworks/base/+/master/core/java/android/hardware/display/DisplayManager.java)
- [WifiDisplay.java AOSP（@hide类）](https://android.googlesource.com/platform/frameworks/base/+/master/core/java/android/hardware/display/WifiDisplay.java)

---

## 4. 主流 Android TV 设备 Miracast 实际支持情况

| 设备 | 支持 Miracast | 备注 |
|------|--------------|------|
| **Chromecast with Google TV**（2020-2023） | ❌ 不支持 | 仅支持 Google Cast；Google 官方社区明确确认不支持 Miracast |
| **Google TV Streamer**（2023）| ❌ 不支持 | 继承 Chromecast with Google TV 定位，无 Miracast |
| **Sony Bravia 2020+ Google TV 型号** | ❌ 不支持 | 官方文档明确指出：2020 年及之后 Android TV/Google TV 型号不支持 Screen Mirroring（Miracast） |
| **Sony Bravia 2013-2020 Android TV 型号** | ✅ 支持 | 通过 Wi-Fi Direct 实现 Screen Mirroring |
| **Xiaomi TV（小米电视/TV Stick）** | ✅ 支持 | 内置 Miracast 应用，支持连接同一 Wi-Fi 网络设备 |
| **Fire TV Stick**（Amazon） | ✅ 支持 | 原生支持 Miracast，可接收 Windows 投屏 |

**关键发现**：市场主流新款设备（Chromecast with Google TV、Sony 新款）已**放弃 Miracast**，转向 Google Cast / AirPlay 生态。若目标用户设备为 Chromecast with Google TV，Miracast A 轨方案对其完全无效。

**官方参考**：
- [Sony 官方：Screen mirroring 不适用于 2020 后型号](https://www.sony.com/electronics/support/televisions-projectors-lcd-tvs-android-/kd-65x80j/articles/00044548)
- [Google Nest 社区确认 Chromecast 不支持 Miracast](https://www.googlenestcommunity.com/t5/Streaming/Does-the-Google-Chromecast-TV-HD-support-Miracast-from-Windows-PC/m-p/470687)
- [Xiaomi 官方 Miracast 说明](https://www.mi.com/global/support/article/KA-124762/)

---

## 5. 综合结论

### 结论：**NO-GO**（作为普通第三方 Android App）

| 评估维度 | 结果 | 说明 |
|----------|------|------|
| 技术可行性（普通 App） | ❌ 不可行 | Android 8.0+ 系统权限封锁，`setWFDInfo()` 无法调用 |
| API 可访问性 | ❌ 不可行 | WifiDisplayManager 全部 @hide，Android 9+ 黑名单 API |
| 目标设备覆盖率 | ❌ 差 | Chromecast with Google TV（主流）完全不支持 Miracast |
| 用户体验 | ⚠️ 差 | 即使成功，Wi-Fi Direct 建连需用户多步手动操作，延迟 > 150ms |

### 条件例外（CONDITIONAL，不建议）

以下条件**全部满足**时可能成立，但门槛极高：
1. App 拥有系统签名或厂商白名单豁免
2. 目标设备运行 Android 7.1 或更低版本，或厂商修改了 `getWfdPermission()` 逻辑
3. 用户设备原生支持 Miracast Sink（排除 Chromecast with Google TV、新款 Sony 等）

对于本项目（Windows → Android TV 投屏），推荐转向 **B 轨（WebSocket/自定义协议）**，在应用层实现屏幕录制+编码+传输，完全规避系统 API 限制，并可支持 Miracast 不可用的主流设备。

---

## 6. 参考链接汇总

| 来源 | 链接 |
|------|------|
| Microsoft - Miracast 驱动要求 | https://learn.microsoft.com/en-us/windows-hardware/drivers/display/wireless-displays--miracast- |
| MS-MICE 协议规范 | https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-mice/9598ca72-d937-466c-95f6-70401bb10bdb |
| Miracast over existing network | https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/wireless-projection-implementing-over-existing-network |
| AOSP WifiDisplayController.java | https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/display/WifiDisplayController.java |
| AOSP WifiDisplay.java（@hide） | https://android.googlesource.com/platform/frameworks/base/+/master/core/java/android/hardware/display/WifiDisplay.java |
| Miracast Permission 分析（中文详解） | https://codezjx.com/posts/miracast-permission-issues/ |
| Sony Bravia 屏幕镜像说明 | https://www.sony.com/electronics/support/televisions-projectors-lcd-tvs-android-/kd-65x80j/articles/00044548 |
| Google Nest 社区 Miracast 讨论 | https://www.googlenestcommunity.com/t5/Streaming/Does-the-Google-Chromecast-TV-HD-support-Miracast-from-Windows-PC/m-p/470687 |
| Windows 11 Connect to wireless display | https://support.microsoft.com/en-us/windows/screen-mirroring-and-projecting-to-your-pc-or-wireless-display-5af9f371-c704-1c7f-8f0d-fa607551d09c |
| Android WifiP2pManager 公开文档 | https://developer.android.com/reference/android/net/wifi/p2p/WifiP2pManager |

---

*研究日期：2026-04-24*
*研究范围：Android 8.0-15、Windows 11、主流 Android TV 设备（2020-2025）*
