[English](README_EN.md) | 中文版

# FControl

> F 键控制与快捷键自动化工具 — 将 F1–F12 与自定义组合键变成系统控制和脚本入口。

[![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![WinUI](https://img.shields.io/badge/WinUI-3-0078D4?logo=windows)](https://learn.microsoft.com/zh-cn/windows/apps/winui/)
[![Windows App SDK](https://img.shields.io/badge/Windows%20App%20SDK-2.0-0078D4)](https://learn.microsoft.com/zh-cn/windows/apps/windows-app-sdk/)
[![Platform](https://img.shields.io/badge/Windows-10%201809%2B%20%7C%2011-blue?logo=windows)](https://www.microsoft.com/windows)

## 简介

很多键盘（尤其是紧凑布局或机械键盘）没有独立的多媒体控制键，调节音量、亮度或切歌时得频繁用鼠标操作任务栏。**FControl** 把 F1–F12 复用为专用的系统控制键，同时支持自定义组合快捷键来执行本地脚本，所有操作都伴随 Fluent Design 风格的浮层视觉反馈。

### 核心能力

| 类别 | 功能 |
| --- | --- |
| 屏幕亮度 | 通过 DDC/CI 调节 HDMI/DP 外接显示器亮度 |
| 系统音量 | 音量增大/减小、静音切换 |
| 媒体播放 | 播放/暂停、上一曲/下一曲、停止、快进/回退 |
| 自定义脚本 | 组合快捷键执行 Shell / PowerShell / Bash / Python / Node.js 脚本或外部程序 |

## 截图

![F1](./image/F1.png)
![F2](./image/F2.png)
![F3](./image/F3.png)
![F4](./image/F4.png)
![F5](./image/F5.png)
![F6](./image/F6.png)

## 特性

- **F1–F12 全局热键** — 后台运行，全系统响应，支持自定义映射每个按键的动作
- **自定义组合快捷键** — `Ctrl` / `Alt` / `Shift` / `Win` + 任意键，触发本地脚本或命令
- **脚本执行引擎** — 支持 6 种执行类型：Windows Shell、PowerShell、Bash、Python、Node.js、外部程序
- **Fluent Design 浮层** — 左上角半透明浮层实时反馈音量/亮度/媒体状态，支持渐现渐隐动画
- **系统托盘驻留** — 关闭窗口后自动转入托盘后台运行，托盘菜单快捷操作
- **DDC/CI 亮度控制** — 直接调节支持 DDC/CI 的外接显示器（HDMI/DP），含检测与排查指引
- **运行环境检测** — 自动检测 Python、Node.js 等运行环境路径与版本，支持手动配置
- **冲突检测** — 保存快捷键时自动检测内部重复、系统保留键、其他程序占用
- **暗色模式** — 完整适配 Windows 浅色/深色主题与系统强调色
- **桌面映射显示** — 在桌面显示 F1–F12 映射状态浮窗，按键时高亮反馈，支持横向/竖向布局、自定义颜色与透明度、窗口锁定（鼠标穿透）
- **ContentDialog 编辑窗口** — 快捷键编辑器由独立窗口重构为 ContentDialog，更符合 WinUI 设计规范，操作更轻量流畅

## 快速开始

### 系统要求

- Windows 10 版本 1809（Build 17763）及以上
- Windows 11 全部版本
- x64、x86 架构（ARM64 可选支持）

### 安装

从 [Releases](https://github.com/biubiutata/fcontrol/releases) 页面下载匹配系统架构的 `FControl-*-Setup.exe`，双击后按安装向导安装。安装过程中可点击“浏览”选择安装磁盘和目录。

### 构建

```bash
# 克隆仓库
git clone https://github.com/biubiutata/fcontrol.git
cd fcontrol

# 构建项目
dotnet build FControl.sln

# 运行调试版
dotnet run --project FControl.csproj

# 发布 Release（ReadyToRun；WinUI/XAML 发布包不要启用裁剪）
dotnet publish -c Release -r win-x64

# 构建 x64 / x86 / ARM64 普通 EXE 安装程序（需要 Inno Setup 6）
winget install --id JRSoftware.InnoSetup -e
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1
```

## 默认按键映射

| 按键 | 默认动作 | 说明 |
| --- | --- | --- |
| F1 | 屏幕亮度减小 | 步进 5% |
| F2 | 屏幕亮度增大 | 步进 5% |
| F3 – F6 | 禁用 | 可按需自定义 |
| F7 | 上一曲 | |
| F8 | 媒体暂停/播放 | 切换播放状态 |
| F9 | 下一曲 | |
| F10 | 静音切换 | 开/关 |
| F11 | 音量减小 | 步进 2% |
| F12 | 音量增大 | 步进 2% |

所有按键均可在「按键映射」页面自由更改，支持：亮度调节、音量调节、静音、媒体控制（播放/暂停/上下曲/停止/快进/回退）、禁用。

## 项目结构

```
FControl/
├── FControl.csproj          # 项目文件 (WinUI 3 + .NET 8)
├── App.xaml(.cs)             # 应用入口与生命周期
├── MainWindow.xaml(.cs)      # 主窗口 (NavigationView)
├── ActionOverlayWindow.xaml(.cs)  # 浮层提示窗口
├── DesktopKeyMappingWindow.xaml(.cs)  # 桌面映射浮窗
├── CustomHotkeyEditorWindow.xaml(.cs)  # 快捷键编辑窗口
├── Models/
│   ├── AppConfiguration.cs   # 配置数据模型、默认值、元数据
│   └── HotKeyAction.cs       # 动作枚举与元数据
├── Services/
│   ├── AppConfigurationService.cs  # 配置持久化 (JSON)
│   ├── GlobalHotKeyService.cs      # 全局热键注册/键盘钩子
│   ├── HotKeyActionService.cs      # 动作调度执行
│   ├── MediaControlService.cs      # 媒体控制 (GSMTC/WM_APPCOMMAND/keybd_event)
│   ├── SystemVolumeService.cs      # 系统音量 (Core Audio API)
│   ├── MonitorBrightnessService.cs # 显示器亮度 (DDC/CI)
│   ├── ScriptExecutionService.cs   # 脚本执行引擎
│   ├── RuntimeEnvironmentService.cs # 运行环境检测
│   ├── TrayIconService.cs          # 系统托盘
│   ├── StartupRegistrationService.cs # 开机自启
│   ├── AppLogService.cs            # 文件日志
│   ├── HexColorHelper.cs            # 颜色解析工具
│   └── HotkeyParser.cs             # 快捷键解析
└── Pages/
    ├── KeyMappingPage.xaml(.cs)     # 按键映射页
    ├── CustomHotkeysPage.xaml(.cs)  # 自定义快捷键页
    ├── DisplaySettingsPage.xaml(.cs) # 显示设置页
    ├── AdvancedSettingsPage.xaml(.cs) # 高级设置页
    └── AboutPage.xaml(.cs)          # 关于页
```

## 技术栈

| 技术 | 用途 |
| --- | --- |
| WinUI 3 + Windows App SDK 2.0 | UI 框架与窗口管理 |
| .NET 8 | 运行时与语言 |
| Windows Core Audio API | 系统音量控制 |
| Windows Monitor Configuration API | DDC/CI 显示器亮度 |
| GlobalSystemMediaTransportControls (GSMTC) | 现代媒体应用控制 |
| RegisterHotKey / Keyboard Hook | 全局热键捕获 |
| Fluent UI System Icons | 图标集 |

## 常见问题

**亮度控制不生效？**
亮度控制依赖 DDC/CI 协议，仅支持 HDMI/DP 连接的外接显示器。请检查：显示器 OSD 菜单中是否开启 DDC/CI、是否使用直连（非转接线/扩展坞/KVM）、显卡驱动是否正常。可在「设置」→「显示器检测」中查看当前显示器状态。

**F 键被其他程序占用？**
在「高级设置」中开启兼容模式，应用将使用键盘钩子兜底捕获按键。

**自定义脚本执行失败？**
在「快捷键功能」页中使用「测试运行」按钮验证脚本路径和解释器路径是否正确。可在「运行环境检测」中查看 Python / Node.js / Bash 的检测状态。

## 同步分享
[LINUX DO](https://linux.do)


## 许可证

[MIT](LICENSE)
