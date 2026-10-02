# Game Monitor

Native WinUI 3 hardware and frame-rate monitor for portrait or landscape secondary Windows displays.

中文说明见 [README.zh-CN.md](README.zh-CN.md).

## Architecture

- Single-process WinUI 3 desktop application.
- Reads MSI Afterburner's `MAHMSharedMemory` named shared memory directly.
- No Python process, HTTP server, listening port, browser, or WebView.
- Uses Windows App SDK 2.2 and the native WinUI `TitleBar` control.
- Uses system caption controls, Mica, theme resources, high contrast, and system scaling.

## Requirements

1. Windows 10 version 1809 or later; Windows 11 is recommended.
2. .NET 8 SDK, only when building from source.
3. MSI Afterburner.

Enable the desired sensors on MSI Afterburner's `Monitoring` page. For the FPS area, enable the available `Framerate`, `Frametime`, `Framerate Avg`, and `Framerate 1% Low` entries.

## Run

Double-click:

```text
Start-Monitor.bat
```

On first run, the script publishes a self-contained x64 build. Later runs start the native executable directly:

```text
bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\GameMonitor.exe
```

From a terminal:

```powershell
dotnet run --project GameMonitor.csproj -p:Platform=x64
```

## Settings

The title bar provides full-screen and settings buttons. Settings include:

- Refresh interval
- Celsius or Fahrenheit
- Chinese or English
- Visibility of FPS and hardware metrics
- Configuration location: user folder or program directory

By default, settings are stored at:

```text
%LOCALAPPDATA%\GameMonitorWinUI3\config.json
```

You can select **Program directory** in Settings to save `config.json` beside the running EXE. This means the executable directory, not the working directory or source directory. The dialog shows the full destination path. Saving copies the current settings to the selected location; subsequent saves and application launches use that location. The previous configuration file is retained.

The location choice is remembered in `%LOCALAPPDATA%\GameMonitorWinUI3\storage-location.json`. If that record is missing or invalid, the app defaults to the user folder. If the selected configuration is missing or invalid, the app uses default settings at the selected location. The program directory must be writable to save there; a failed save leaves the active settings and location unchanged and displays an error in the dialog.

The user directory is exclusive to the WinUI 3 app. The original Python project stores its configuration in its own project directory. Configurations from that project or the previous `%LOCALAPPDATA%\GameMonitor` directory are not imported automatically.

CPU and GPU model names are read from Windows. If multiple display adapters are detected, their names are shown together, with the primary display adapter first; this does not change which Afterburner sensor is used for each metric.

The app does not modify MSI Afterburner. If Afterburner is not running or a monitoring entry is disabled, the corresponding value is unavailable.
