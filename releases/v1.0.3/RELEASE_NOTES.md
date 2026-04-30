# OrangeCast v1.0.3

## 发布包

| 文件 | 说明 |
|---|---|
| `OrangeCast-win-x64-v1.0.3.zip` | Windows 投屏端（解压即用，需 .NET 8 Desktop Runtime） |
| `OrangeCast-android-v1.0.3.apk` | Android 接收端 |

## 本版变更

### Windows 端
- 进程单实例检测：重复启动时激活已有窗口，不再开多个
- 修复设置按钮图标不显示（cog 图标）
- 版本号改为动态读取，不再固定显示 v1.0.0
- 发布包从 ~550MB 压缩至 51.7MB（去重 FFmpeg、改为 framework-dependent）

### Android 端
- APK 从 52MB 压缩至 20.2MB（ABI 只保留 arm/arm64，启用 R8 混淆）
- 修复 release build 下 ICE 候选解析崩溃

## 运行要求

- **Windows**：Windows 10 1809+ x64，[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Android**：Android 8.0+（API 26+）
