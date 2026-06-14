using GameMonitor.Models;

namespace GameMonitor.Services;

public sealed class MonitorService
{
    private static readonly Dictionary<string, HashSet<uint>> SourceIds = new()
    {
        ["gpu_temperature"] = [0x00000000],
        ["gpu_frequency"] = [0x00000020],
        ["gpu_memory_frequency"] = [0x00000022],
        ["gpu_usage"] = [0x00000030],
        ["gpu_power"] = [0x00000061],
        ["gpu_fan_speed"] = [0x00000010],
        ["gpu_memory_usage"] = [0x00000031],
        ["cpu_temperature"] = [0x00000080],
        ["cpu_usage"] = [0x00000090],
        ["cpu_frequency"] = [0x000000A0],
        ["cpu_power"] = [0x00000100],
        ["memory_usage"] = [0x00000091],
    };

    private static readonly Dictionary<string, string[][]> Rules = new()
    {
        ["cpu_frequency"] = [["cpu"], ["clock", "frequency", "频率"], ["mhz", "ghz"]],
        ["cpu_usage"] = [["cpu"], ["usage", "load", "占用", "使用率"], ["%"]],
        ["cpu_temperature"] = [["cpu"], ["temperature", "temp", "温度"], ["°c", " c", "度"]],
        ["cpu_power"] = [["cpu"], ["power", "功耗", "功率"], ["w"]],
        ["gpu_frequency"] = [["gpu", "core"], ["clock", "frequency", "频率"], ["mhz", "ghz"]],
        ["gpu_memory_frequency"] = [["memory", "vram", "显存"], ["clock", "frequency", "频率"], ["mhz", "ghz"]],
        ["gpu_usage"] = [["gpu"], ["usage", "load", "占用", "使用率"], ["%"]],
        ["gpu_temperature"] = [["gpu"], ["temperature", "temp", "温度"], ["°c", " c", "度"]],
        ["gpu_power"] = [["gpu"], ["power", "功耗", "功率"], ["w"]],
        ["gpu_fan_speed"] = [["fan", "风扇"], ["speed", "tachometer", "转速"], ["rpm", "%"]],
        ["gpu_memory_usage"] = [["memory", "vram", "显存"], ["usage", "used", "占用", "使用率"], ["mb", "gb", "%"]],
        ["memory_usage"] = [["ram", "memory", "内存"], ["usage", "used", "占用", "使用"], ["mb", "gb", "%"]],
    };

    private readonly AfterburnerReader _reader = new();
    private readonly Queue<(DateTime Time, double Fps)> _fpsHistory = new();

    public MonitorSnapshot Read(AppSettings settings)
    {
        try
        {
            var sensors = _reader.Read();
            var metrics = MetricCatalog.HardwareIds
                .Select(id => MapMetric(id, sensors, settings))
                .ToList();
            var fps = ReadFps(sensors);
            var status = settings.Language == "en"
                ? "Reading MSI Afterburner shared memory"
                : "正在读取 MSI Afterburner 共享内存";
            return new MonitorSnapshot(DateTime.Now, metrics, fps, status, true);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or
            UnauthorizedAccessException or
            InvalidDataException or
            IOException)
        {
            var status = settings.Language == "en"
                ? "MSI Afterburner shared memory is unavailable"
                : "MSI Afterburner 共享内存不可用";
            return new MonitorSnapshot(
                DateTime.Now,
                MetricCatalog.HardwareIds
                    .Select(id => new MetricSnapshot(id, MetricCatalog.Label(id, settings.Language), "--"))
                    .ToList(),
                new FpsSnapshot(null, null, null, null),
                $"{status}: {exception.Message}",
                false);
        }
    }

    private static MetricSnapshot MapMetric(
        string id,
        IReadOnlyList<AfterburnerSensor> sensors,
        AppSettings settings)
    {
        var sensor = sensors
            .Where(candidate => Matches(candidate, id))
            .OrderByDescending(candidate => Score(candidate, id))
            .FirstOrDefault();
        var value = sensor is null ? "--" : FormatValue(id, sensor, settings);
        return new MetricSnapshot(id, MetricCatalog.Label(id, settings.Language), value);
    }

    private FpsSnapshot ReadFps(IReadOnlyList<AfterburnerSensor> sensors)
    {
        var current = PickFps(sensors, FpsKind.Current);
        var average = PickFps(sensors, FpsKind.Average);
        var low = PickFps(sensors, FpsKind.LowOnePercent);
        var frameTime = sensors.FirstOrDefault(IsFrameTime);

        double? currentValue = current?.Value;
        double? frameTimeValue = frameTime?.Value;
        if ((!currentValue.HasValue || currentValue <= 0) && frameTimeValue > 0)
        {
            currentValue = 1000.0 / frameTimeValue;
        }

        double? averageValue = average?.Value;
        double? lowValue = low?.Value;
        if (currentValue > 0)
        {
            var now = DateTime.UtcNow;
            _fpsHistory.Enqueue((now, currentValue.Value));
            while (_fpsHistory.TryPeek(out var sample) && now - sample.Time > TimeSpan.FromMinutes(2))
            {
                _fpsHistory.Dequeue();
            }

            var samples = _fpsHistory.Select(sample => sample.Fps).Order().ToArray();
            if (samples.Length > 0)
            {
                averageValue ??= samples.Average();
                var lowCount = Math.Max(1, (int)Math.Ceiling(samples.Length * 0.01));
                lowValue ??= samples.Take(lowCount).Average();
            }
        }

        return new FpsSnapshot(
            Round(currentValue, 1),
            Round(frameTimeValue, 2),
            Round(lowValue, 1),
            Round(averageValue, 1));
    }

    private static bool Matches(AfterburnerSensor sensor, string id)
    {
        if (SourceIds[id].Contains(sensor.SourceId))
        {
            return true;
        }

        var text = $"{sensor.Id} {sensor.Label} {sensor.RawLabel} {sensor.Units}".ToLowerInvariant();
        return Rules[id].All(group => group.Any(text.Contains));
    }

    private static int Score(AfterburnerSensor sensor, string id)
    {
        var text = $"{sensor.Id} {sensor.Label} {sensor.RawLabel}".ToLowerInvariant();
        var score = SourceIds[id].Contains(sensor.SourceId) ? 20 : 0;
        if (id.StartsWith("cpu_", StringComparison.Ordinal) && sensor.GpuIndex == uint.MaxValue) score += 12;
        if (id.StartsWith("gpu_", StringComparison.Ordinal) && sensor.GpuIndex == 0) score += 6;
        if (text.Contains("average") || text.Contains("avg")) score -= 8;
        if (text.Contains("per core")) score -= 4;
        if (id == "gpu_memory_frequency" && (text.Contains("memory clock") || text.Contains("显存频率"))) score += 20;
        if (id == "gpu_memory_usage" && (text.Contains("vram") || text.Contains("显存"))) score += 20;
        if (id == "gpu_fan_speed" && (text.Contains("rpm") || text.Contains("tachometer"))) score += 25;
        if (id == "memory_usage" && (text.Contains("gpu") || text.Contains("vram") || text.Contains("显存"))) score -= 20;
        return score;
    }

    private static string FormatValue(string id, AfterburnerSensor sensor, AppSettings settings)
    {
        if (id is "cpu_temperature" or "gpu_temperature")
        {
            return settings.TemperatureUnit == "F"
                ? $"{sensor.Value * 9 / 5 + 32:0} °F"
                : $"{sensor.Value:0} °C";
        }

        if (id is "memory_usage" or "gpu_memory_usage")
        {
            return sensor.Value >= 1024
                ? $"{sensor.Value / 1024:0.0} GB"
                : $"{sensor.Value:0} MB";
        }

        var decimals = Math.Abs(sensor.Value) >= 100 ? 0 : 1;
        return $"{Math.Round(sensor.Value, decimals):0.##} {sensor.Units}".Trim();
    }

    private static bool IsFrameTime(AfterburnerSensor sensor)
    {
        var text = $"{sensor.Id} {sensor.Label} {sensor.RawLabel}".ToLowerInvariant();
        return sensor.SourceId == 0x51 ||
               text.Contains("frametime") ||
               text.Contains("frame time") ||
               text.Contains("帧生成");
    }

    private static bool IsFps(AfterburnerSensor sensor)
    {
        if (IsFrameTime(sensor))
        {
            return false;
        }

        var text = $"{sensor.Id} {sensor.Label} {sensor.RawLabel}".ToLowerInvariant();
        return sensor.SourceId == 0x50 ||
               text.Contains("framerate") ||
               text.Contains("frame rate") ||
               text.Contains("fps") ||
               text.Contains("帧率");
    }

    private static AfterburnerSensor? PickFps(
        IReadOnlyList<AfterburnerSensor> sensors,
        FpsKind kind)
    {
        return sensors
            .Where(IsFps)
            .Select(sensor => (Sensor: sensor, Score: FpsScore(sensor, kind)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => candidate.Sensor)
            .FirstOrDefault();
    }

    private static int FpsScore(AfterburnerSensor sensor, FpsKind kind)
    {
        var text = $"{sensor.Id} {sensor.Label} {sensor.RawLabel}".ToLowerInvariant();
        return kind switch
        {
            FpsKind.Current => 20 - (ContainsAny(text, "avg", "average", "平均", "1%", "low", "最低", "min", "max") ? 30 : 0),
            FpsKind.Average => (ContainsAny(text, "avg", "average", "平均") ? 30 : 0) -
                               (ContainsAny(text, "1%", "low", "最低", "min", "max") ? 20 : 0),
            FpsKind.LowOnePercent => (ContainsAny(text, "1%", "1 percent") ? 35 : 0) +
                                     (ContainsAny(text, "low", "最低") ? 20 : 0) -
                                     (ContainsAny(text, "avg", "average", "平均", "max") ? 20 : 0),
            _ => 0,
        };
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);

    private static double? Round(double? value, int digits) =>
        value.HasValue ? Math.Round(value.Value, digits) : null;

    private enum FpsKind
    {
        Current,
        Average,
        LowOnePercent,
    }
}
