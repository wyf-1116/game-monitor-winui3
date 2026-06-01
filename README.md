# Game Monitor

Portrait 4K hardware and FPS monitor for a secondary Windows display.

中文说明见 [README.zh-CN.md](README.zh-CN.md).

## Required companion software

Install and run:

1. **MSI Afterburner**
   - Official download page: [MSI Afterburner](https://www.msi.com/Landing/afterburner).
   - Download MSI Afterburner only from the official `msi.com` page.
   - Run MSI Afterburner before launching games.
   - In MSI Afterburner, enable the hardware monitoring items you want to track.
   - Enable `Framerate`, `Framerate Avg`, and `Framerate 1% Low` in the Monitoring list if you want FPS, average FPS, and 1% Low.

The monitor reads hardware sensor data and FPS metrics from MSI Afterburner shared memory.

## Run

Double-click `Start-Monitor.bat`, then open:

```text
http://127.0.0.1:8765
```

Move the browser window to the secondary portrait 4K display and press `F11` for fullscreen.

## Configure

Use the settings button in the upper-right corner:

- Refresh interval: controls polling frequency in milliseconds.
- Display items: turn the FPS metrics and hardware metrics on or off. Hardware metrics can be reordered with the up/down buttons in Settings.
- FPS: shown when MSI Afterburner exposes framerate monitoring values.

Settings are stored in `config.json`.

## Notes

- If MSI Afterburner is not running, hardware values will show as unavailable.
- If the MSI Afterburner framerate monitoring items are disabled, FPS values will show as unavailable.
- `1% Low` and average FPS use MSI Afterburner's own monitoring values when available; otherwise they are estimated from recent FPS samples.
- `Frametime` is shown as frame generation time and can also be used to derive current FPS when Afterburner does not expose a direct framerate sensor.
- Hardware cards are limited to CPU frequency, CPU usage, CPU temperature, CPU power, GPU frequency, VRAM frequency, GPU usage, GPU temperature, GPU power, VRAM usage, and memory usage.
- The layout is designed for a 2160 x 3840 portrait display but remains usable on smaller screens for setup.
