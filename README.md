# 橙子投屏 OrangeCast

> Windows → Android 无线投屏工具，低延迟、高画质。

![platform](https://img.shields.io/badge/Windows-10%2B-blue?logo=windows)
![platform](https://img.shields.io/badge/Android-8.0%2B-green?logo=android)
![release](https://img.shields.io/github/v/release/Orangeeeeeeeeee/OrangeCast)
![license](https://img.shields.io/badge/License-GPL--3.0-blue)

## 功能

- 📺 将 Windows 屏幕实时投屏到 Android 设备
- 🔒 4 位 PIN 配对，局域网内点对点传输
- ⚡ 硬件编码加速（NVENC / QSV / AMF）
- 🔊 同步传输系统音频
- 📡 mDNS 自动发现局域网内设备

## 下载

前往 [Releases](https://github.com/Orangeeeeeeeeee/OrangeCast/releases/latest) 下载最新版本：

| 文件 | 说明 |
|---|---|
| `OrangeCast-win-x64-*.zip` | Windows 投屏端，解压即用 |
| `OrangeCast-android-*.apk` | Android 接收端 |

> Windows 端需要 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)，首次运行未安装时 Windows 会自动弹窗引导安装。

## 使用方法

1. Android 设备安装 APK，打开应用
2. Windows 解压 zip，运行 `OrangeCast.exe`
3. 应用显示 4 位 PIN 码
4. Windows 端输入 PIN 码连接
5. 开始投屏

## 技术栈

| 端 | 技术 |
|---|---|
| Windows | C# .NET 8 · WinForms · WebRTC · FFmpeg |
| Android | Kotlin · WebRTC · mDNS |

## 系统要求

- **Windows**：Windows 10 1809+ x64
- **Android**：Android 8.0+（API 26+），arm / arm64

## License

本项目基于 [GNU General Public License v3.0](LICENSE) 开源。
