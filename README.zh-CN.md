# Game Monitor

基于 WinUI 3 的 Windows 原生硬件与帧率监控应用，适合放在横向或纵向副显示器上。

## 架构

- 单进程 WinUI 3 桌面应用。
- 直接读取 MSI Afterburner 的 `MAHMSharedMemory` 命名共享内存。
- 不运行 Python，不启动 HTTP 服务，不监听端口，也不需要浏览器。
- 使用 Windows App SDK 2.2 和 WinUI 原生 `TitleBar` 控件。
- 使用系统 caption controls、Mica、主题色、高对比度和系统缩放行为。

## 依赖

1. Windows 10 1809 或更高版本，推荐 Windows 11。
2. .NET 8 SDK，仅从源码构建时需要。
3. MSI Afterburner。

请在 MSI Afterburner 的 `Monitoring` 页面启用需要显示的监控项。要显示帧率区域，请启用 `Framerate`、`Frametime`、`Framerate Avg` 和 `Framerate 1% Low` 中可用的项目。

## 运行

双击：

```text
Start-Monitor.bat
```

脚本会在首次运行时发布自包含的 x64 应用，之后直接启动：

```text
bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\GameMonitor.exe
```

也可以在终端运行：

```powershell
dotnet run --project GameMonitor.csproj -p:Platform=x64
```

## 设置

标题栏右侧提供全屏和设置按钮。设置包括：

- 刷新间隔
- 摄氏度或华氏度
- 中文或 English
- FPS 与硬件监控项目的显示开关

配置保存在：

```text
%LOCALAPPDATA%\GameMonitor\config.json
```

应用不会修改 MSI Afterburner 配置，只读取其共享内存。如果 Afterburner 未运行或没有启用对应监控项，相关值会显示为不可用。
