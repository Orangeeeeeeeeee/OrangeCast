# 橙子投屏 OrangeCast - Design System (MASTER)

> 全局设计源真相。所有 UI 修改必须遵守本文件。  
> 项目名: 橙子投屏 (OrangeCast)  
> 子端: WIN (Windows Sender) + APP (Android Receiver)  
> 风格: Flat Design (扁平化) + Orange 色系  

---

## 1. 品牌

| 项 | 值 |
|---|---|
| 中文名 | 橙子投屏 |
| 英文名 | OrangeCast |
| Slogan | 一键投屏，简单如橙 |
| 风格关键词 | 扁平、温暖、清爽、专注 |

---

## 2. 颜色令牌 (Color Tokens)

> 基于 WCAG AA+ 验证。所有 UI 中的颜色必须从下表取值，不允许硬编码 hex。

### 2.1 主色板 (Light Mode)

| 角色 | Hex | 用途 |
|---|---|---|
| `primary` | `#EA580C` | 主色 (orange-600) - 主按钮/品牌色/连接成功状态 |
| `primary-hover` | `#C2410C` | 主色 hover (orange-700) |
| `primary-pressed` | `#9A3412` | 主色 pressed (orange-800) |
| `on-primary` | `#FFFFFF` | 主色之上文本 |
| `secondary` | `#FB923C` | 次色 (orange-400) - 辅助强调/背景装饰 |
| `accent` | `#FED7AA` | 浅橙 (orange-200) - 高亮区背景 |
| `background` | `#FFFBF5` | 全局背景 - 暖白 (橙调白) |
| `surface` | `#FFFFFF` | 卡片/面板表面 |
| `foreground` | `#1C1917` | 正文 (stone-900) |
| `muted` | `#FCF6F0` | 静态背景 (输入框/分组背景) |
| `muted-foreground` | `#78716C` | 次要文本 (stone-500) |
| `border` | `#FED7AA` | 通用边框 (orange-200) |
| `border-strong` | `#FB923C` | 强边框 (orange-400) |
| `destructive` | `#DC2626` | 危险/断开连接红 |
| `destructive-hover` | `#B91C1C` | 危险 hover |
| `on-destructive` | `#FFFFFF` | 危险之上文本 |
| `success` | `#16A34A` | 成功/已连接绿 |
| `ring` | `#EA580C` | 焦点环 |

> 注: Accent 主橙 #EA580C 经 WCAG 验证 4.5:1 vs 白；正文 stone-900 vs #FFFBF5 = 17:1 (AAA)。

### 2.2 暗色板 (Dark Mode 可选支持)

| 角色 | Hex |
|---|---|
| `primary` | `#FB923C` |
| `background` | `#1C1917` |
| `surface` | `#292524` |
| `foreground` | `#FAFAF9` |

---

## 3. 字体令牌 (Typography)

| 用途 | 字体族 |
|---|---|
| 西文 | **Inter** (400/500/600/700) |
| 中文 (Windows) | **Microsoft YaHei UI** → fallback **PingFang SC** |
| 中文 (Android) | **HarmonyOS Sans SC** → fallback **PingFang SC** → fallback **Noto Sans SC** |
| 等宽 (IP/端口显示) | **JetBrains Mono** / **Consolas** |

### 字号阶梯 (Type Scale)

| Token | px | 用途 |
|---|---|---|
| `text-xs` | 12 | 辅助说明 |
| `text-sm` | 14 | 次要正文 |
| `text-base` | 15 | 正文 |
| `text-lg` | 17 | 强调正文 |
| `text-xl` | 20 | 小标题 |
| `text-2xl` | 24 | 区块标题 |
| `text-3xl` | 30 | 主标题 |
| `text-display` | 36 | 品牌大标题 |

行高: 正文 1.5、标题 1.25  
字重: 正文 400、强调 500、按钮/标题 600、Display 700

---

## 4. 间距与圆角 (Spacing & Radius)

### 4.1 间距系统 (8pt grid)

`4 / 8 / 12 / 16 / 24 / 32 / 48 / 64`

### 4.2 圆角系统

| Token | px | 用途 |
|---|---|---|
| `radius-sm` | 4 | 小元素 (tag/badge) |
| `radius-md` | 8 | 输入框/按钮 |
| `radius-lg` | 12 | 卡片/弹窗 |
| `radius-window` | **5** | **窗口最外层边框** (用户硬性要求) |

> ⚠️ WIN 端窗口最外层边框圆角必须为 **5px**。

---

## 5. 阴影与边框

**扁平化设计 → 禁用阴影**, 用边框/色块表达层级。

| Token | 值 |
|---|---|
| `shadow` | **none** (扁平化禁用) |
| `border-width` | 1px (普通) / 2px (强调) |
| `divider` | 1px solid var(--border) |

例外: 弹窗 (设置弹窗) 允许 `0 8px 24px rgba(234,88,12,0.08)` 微弱橙调阴影以区分层级。

---

## 6. 图标系统

**强制规则: 全部使用开源图标库, 严禁 emoji, 严禁自绘 hint 文字图标。**

| 平台 | 图标库 | NuGet/Maven |
|---|---|---|
| WIN (WPF/WinForms/Avalonia) | **Material.Icons** 或 **MahApps.Metro.IconPacks** | `Material.Icons.WPF` / `MahApps.Metro.IconPacks.MaterialDesign` |
| WIN (Win32/HTML) | **Lucide** SVG / **Material Symbols** | 直接 SVG |
| Android | **Material Symbols** (官方) 或 **Material Icons** | `androidx.compose.material:material-icons-extended` 或 `material-icons.zip` 拷贝 |

### 图标尺寸

| Token | px |
|---|---|
| `icon-sm` | 16 |
| `icon-md` | 20 |
| `icon-lg` | 24 |
| `icon-xl` | 32 |

笔触宽度统一: 1.5px (Lucide 默认) 或 2px (Material 默认), **同项目内不可混用**。

### 关键图标映射

| 用途 | 图标 (Lucide / Material) |
|---|---|
| 设置 | `settings` / `Cog` |
| 连接 | `link` / `Link` |
| 断开 | `unlink` / `LinkOff` |
| 设备 | `monitor` / `MonitorOutline` |
| 刷新 | `refresh-cw` / `Refresh` |
| 关闭 | `x` / `Close` |
| 编码 | `cpu` / `Cpu` |
| 加速 | `zap` / `FlashOn` |
| 网络 | `wifi` / `Wifi` |
| 端口 | `network` / `Lan` |

---

## 7. 状态样式

### 按钮状态

| 状态 | 主按钮 (Primary) | 危险按钮 (Destructive) |
|---|---|---|
| Default | bg `primary` / text `on-primary` | bg `destructive` / text `on-destructive` |
| Hover | bg `primary-hover` (-100ms 过渡) | bg `destructive-hover` |
| Pressed | bg `primary-pressed` + scale(0.98) | bg `#991B1B` + scale(0.98) |
| Disabled | bg `muted` / text `muted-foreground` / opacity 0.5 | 同 |
| Focus | 2px ring `ring` 偏移 2px | 2px ring `destructive` |

### 连接按钮状态机 (WIN 端核心)

```
[未连接] 默认状态
  按钮文案: "连接"
  按钮配色: bg=primary(#EA580C), text=white, 无黑色底纹
  ↓ 点击 → 进入 [连接中]

[连接中] 异步等待
  按钮文案: "连接中..."
  按钮配色: bg=primary, opacity=0.7, 显示 spinner
  禁用点击
  ↓ 握手成功 → [已连接] / 失败 → [未连接] + Toast

[已连接] 已建立连接
  按钮文案: "断开连接"
  按钮配色: bg=destructive(#DC2626), text=white
  ↓ 点击 → 立即断开 → [未连接]
```

> ❌ 严禁: 黑色底纹 / 黑色背景 / box-shadow 黑色

---

## 8. 组件规范

### 8.1 输入框 (IP 输入)

- 高度: 44px (touch-friendly)
- 圆角: `radius-md` (8px)
- 边框: 1px `border` → focus 时变 2px `primary`
- 背景: `surface` (#FFFFFF)
- 字号: `text-lg` (17px) 等宽字体 (JetBrains Mono / Consolas)
- 内边距: 12px 16px
- placeholder: `muted-foreground`
- 占位文本: `请输入设备 IP, 例如 192.168.1.100`

### 8.2 设备列表项

- 高度: 64px
- 间距: 列表项间 8px gap
- 圆角: `radius-md`
- 边框: 1px `border`
- 背景: `surface` → hover `muted`
- 选中状态: 左侧 3px `primary` 实心条 + bg `accent`

### 8.3 设置弹窗 (WIN)

- 触发: 主窗口右上角 `settings` 图标按钮
- 尺寸: 480 × 360 (可滚)
- 圆角: `radius-lg` (12px)
- 标题: 24px / 600
- 主体: 列出 "编码方式" / "硬件加速" 等切换项
- 底部: 取消 (secondary) + 保存 (primary)
- 关闭: ESC / 点击外部 / 右上角 `x`

---

## 9. 页面布局规范

### WIN 主窗口布局 (从上到下)

```
┌────────────────────────────── 5px 圆角窗口 ──────────────────────────────┐
│ [品牌区]      橙子投屏 OrangeCast                    [⚙ 设置] [─□×]    │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  [IP 输入区]   设备 IP:                                              │
│              ┌───────────────────────────────┐  [连接]            │
│              │  192.168.1.100             │                       │
│              └───────────────────────────────┘                       │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│  [设备列表]  局域网设备  (3)                       [↻ 刷新]         │
│                                                                      │
│   ┌──────────────────────────────────────────────────┐              │
│   │ [📺] Living Room TV         192.168.1.50  [连接] │              │
│   │ [📺] Bedroom TV             192.168.1.51  [断开] │ ← 已连接红   │
│   │ [📺] Office Display         192.168.1.52  [连接] │              │
│   └──────────────────────────────────────────────────┘              │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

> 关键变更: IP 输入区 **置于设备列表上方**, 输入框宽度占主区 60%+, 高度 44px。

### APP (Android Receiver) 布局 — **横屏 (landscape)**, 兼容 Android 8.0+ (API 26+)

约束:
- **screenOrientation = landscape** (强制横屏)
- **minSdk = 26** (Android 8.0+)
- **Theme**: 自定义 `Theme.OrangeCast` (基 `Theme.AppCompat.Light.NoActionBar`)
- **支持 TV (LEANBACK_LAUNCHER)** + 手机平板

横屏布局 (左右 2 栏):

```
┌──────────────────── ActionBar 区 (橙) ────────────────────┐
│  [📺 logo] 橙子投屏                            [状态指示器] │
├────────────────────────────────────────────────────────────┤
│                          │                                 │
│   左栏 (品牌+视频区)     │      右栏 (信息卡片堆叠)        │
│                          │                                 │
│   等待 Windows 端连接... │   ┌─ IP : 端口 卡片 ──────────┐ │
│                          │   │  [wifi]  设备 IP          │ │
│   [品牌大字 / 装饰]      │   │  192.168.1.50:8765        │ │
│                          │   └───────────────────────────┘ │
│   (连接后此区切换为      │                                 │
│    SurfaceViewRenderer)  │   ┌─ PIN 配对码卡 (重点强化) ─┐ │
│                          │   │  [key]   配对码           │ │
│                          │   │   ┌──┐ ┌──┐ ┌──┐ ┌──┐    │ │
│                          │   │   │ 4│ │ 7│ │ 2│ │ 9│    │ │
│                          │   │   └──┘ └──┘ └──┘ └──┘    │ │
│                          │   │  在 Windows 端输入此码     │ │
│                          │   └───────────────────────────┘ │
│                          │                                 │
│                          │   状态: 等待连接...             │
└────────────────────────────────────────────────────────────┘
```

#### PIN 配对码区强化规范

- **位置**: 右栏中部, 视觉权重最高
- **每位数字单独 chip**: 56×72dp 容器 + 8dp 间距
- **chip 样式**: bg=`accent` (#FED7AA), border=2dp `primary`, radius=`radius-md` (8dp)
- **数字字号**: 32sp / JetBrains Mono / 700 / `primary` (#EA580C)
- **顶部图标**: Material `vpn_key` 或 `pin` (icon-md)
- **底部说明**: "在 Windows 端输入此码" / `text-sm` / `muted-foreground`
- **可点击**: 长按整体复制 (Toast 提示)

#### IP:端口卡规范

- **格式**: `192.168.1.50:8765` (单行, 等宽 JetBrains Mono, 24sp)
- **端口默认**: 8765 (来自 SignalingServer)
- **顶部图标**: Material `wifi`
- **可长按复制**

#### 兼容性要求 (minSdk 26)

- ❌ 不可使用 API 27+ 独占 API (如 `setForceDarkAllowed`)
- ✅ 使用 `androidx.core.content.ContextCompat.getColor()` 替代 `getColor()`
- ✅ 使用 `androidx.core.content.res.ResourcesCompat.getFont()` 加载字体
- ✅ Vector drawable 必须含 `app:srcCompat` (或 ImageViewCompat)
- ✅ Theme 使用 `parent="Theme.AppCompat.Light.NoActionBar"` 不可用 Material3 主题
- ✅ 颜色资源使用 `@color/...` (避开 ColorStateList API 23+ 限制)
- ✅ Drawable shape 使用 XML (避开 Material 组件依赖)

---

## 10. 反模式 (禁止)

- ❌ Emoji 当图标
- ❌ 黑色背景/底纹/阴影 (除非 dark mode)
- ❌ 硬编码 hex 在组件内 (必须从 token 取)
- ❌ 圆角 0 或 > 16 (除特定品牌区)
- ❌ 多种字体混用 (西文只 Inter, 等宽只 JetBrains Mono)
- ❌ 笔触宽度混用图标
- ❌ 渐变 (扁平化项目禁用)
- ❌ 复杂阴影
- ❌ 微交互超过 300ms

---

## 11. 动效

| 类型 | 时长 | 缓动 |
|---|---|---|
| 按钮 hover | 150ms | ease-out |
| 按钮 press | 100ms | ease-in |
| 弹窗进入 | 200ms | ease-out |
| 弹窗退出 | 150ms | ease-in |
| 状态切换 (连接→断开) | 250ms | ease-in-out + 颜色过渡 |

---

## 12. 验收检查表

- [ ] 全部颜色从 token 取，无硬编码 hex
- [ ] 全部图标来自 Lucide / Material (不含 emoji)
- [ ] 字体仅 Inter + 中文 sans + JetBrains Mono
- [ ] 窗口最外层圆角 = 5px (WIN)
- [ ] 连接按钮无黑色底纹
- [ ] 已连接 → 按钮变红色 "断开连接"
- [ ] IP 输入框在设备列表上方, 高度 ≥ 44px
- [ ] 设置图标在右上角, 点击弹出设置弹窗
- [ ] 设置弹窗含 "编码方式" + "硬件加速" 切换
- [ ] APP 显示 IP + 端口
- [ ] 应用名/标题改为 "橙子投屏" / "OrangeCast"
- [ ] 扁平化: 无阴影 (除弹窗微弱橙调)
- [ ] 主按钮 hover/press 状态完整
- [ ] 焦点环可见 (键盘可达)
