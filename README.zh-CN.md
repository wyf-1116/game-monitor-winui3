# Game Monitor

用于副显示器的纵向 4K 硬件监控和游戏帧率面板。

English documentation: [README.md](README.md)

## 依赖软件

安装并运行：

1. **MSI Afterburner**
   - 官方下载页面：[MSI Afterburner](https://www.msi.com/Landing/afterburner)。
   - 建议只从 `msi.com` 官方页面下载 MSI Afterburner。
   - 启动游戏前先运行 MSI Afterburner。
   - 在 MSI Afterburner 的 `Monitoring` 里启用你想监控的硬件项目。
   - 如果要显示实时帧率、平均帧率和 1% Low，请在 `Monitoring` 里启用 `Framerate`、`Framerate Avg`、`Framerate 1% Low`。

本面板只读取 MSI Afterburner 的共享内存数据，不再依赖 AIDA64。

## 启动

双击 `Start-Monitor.bat`，然后打开：

```text
http://127.0.0.1:8765
```

把浏览器窗口移动到纵向 4K 副屏，按 `F11` 全屏。

## 设置

点击右上角设置按钮：

- 刷新间隔：设置数据刷新频率，单位为毫秒。
- 温度单位：可在 `°C` 和 `°F` 之间切换，默认 `°C`。
- 语言：可在中文和 English 之间切换。
- 显示项目：可以开启或关闭 FPS 指标和 9 个硬件指标。

设置保存在 `config.json`。

## 显示项目

帧率区域：

- 实时帧率
- 帧生成时间
- 1% Low 帧
- 平均帧率

硬件区域：

- CPU 频率
- CPU 占用率
- CPU 温度
- CPU 功率
- 显卡频率
- 显卡占用率
- 显卡温度
- 显卡功率
- 内存占用，格式为 `已用 / 总量`

## 说明

- 如果 MSI Afterburner 没有运行，硬件数据会显示为不可用。
- 如果 MSI Afterburner 没有暴露帧率监控项，FPS、平均帧率和 1% Low 会显示为不可用。
- `1% Low` 和平均帧率会优先使用 MSI Afterburner 的原生监控值；如果没有原生值，则根据近期 FPS 采样估算。
- `Frametime` 会作为帧生成时间显示；当 Afterburner 没有直接暴露帧率时，也可用它推算当前 FPS。
- 页面布局针对 2160 x 3840 纵向屏设计，也会根据窗口大小自动缩放并在需要时切换两列/三列。
