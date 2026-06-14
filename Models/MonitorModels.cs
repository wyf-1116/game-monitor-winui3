using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameMonitor.Models;

public sealed record AfterburnerSensor(
    string Id,
    string Label,
    string Units,
    string RawLabel,
    float Value,
    uint GpuIndex,
    uint SourceId);

public sealed record FpsSnapshot(
    double? Current,
    double? FrameTimeMs,
    double? LowOnePercent,
    double? Average);

public sealed record MetricSnapshot(string Id, string Label, string Value);

public sealed record MonitorSnapshot(
    DateTime UpdatedAt,
    IReadOnlyList<MetricSnapshot> Metrics,
    FpsSnapshot Fps,
    string Status,
    bool IsConnected);

public sealed class MetricCard : INotifyPropertyChanged
{
    private string _label = "";
    private string _value = "";

    public string Id { get; set; } = "";

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class DisplayItemOption : INotifyPropertyChanged
{
    private bool _isEnabled;

    public string Id { get; set; } = "";
    public string Label { get; set; } = "";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class AppSettings
{
    public int RefreshIntervalMs { get; set; } = 1000;
    public string Language { get; set; } = "zh";
    public string TemperatureUnit { get; set; } = "C";
    public List<string> DisplayItems { get; set; } = MetricCatalog.AllIds.ToList();
}

public static class MetricCatalog
{
    public static readonly string[] FpsIds =
    [
        "fps_current",
        "frame_time",
        "fps_1_low",
        "fps_average",
    ];

    public static readonly string[] HardwareIds =
    [
        "cpu_frequency",
        "cpu_usage",
        "cpu_temperature",
        "cpu_power",
        "gpu_frequency",
        "gpu_memory_frequency",
        "gpu_usage",
        "gpu_temperature",
        "gpu_power",
        "gpu_fan_speed",
        "gpu_memory_usage",
        "memory_usage",
    ];

    public static IEnumerable<string> AllIds => FpsIds.Concat(HardwareIds);

    public static string Label(string id, string language)
    {
        var english = language == "en";
        return id switch
        {
            "fps_current" => english ? "Current FPS" : "实时帧率",
            "frame_time" => english ? "Frame Time" : "帧生成时间",
            "fps_1_low" => "1% Low",
            "fps_average" => english ? "Average FPS" : "平均帧率",
            "cpu_frequency" => english ? "CPU Clock" : "CPU 频率",
            "cpu_usage" => english ? "CPU Usage" : "CPU 占用率",
            "cpu_temperature" => english ? "CPU Temperature" : "CPU 温度",
            "cpu_power" => english ? "CPU Power" : "CPU 功率",
            "gpu_frequency" => english ? "GPU Clock" : "GPU 频率",
            "gpu_memory_frequency" => english ? "VRAM Clock" : "显存频率",
            "gpu_usage" => english ? "GPU Usage" : "GPU 占用率",
            "gpu_temperature" => english ? "GPU Temperature" : "GPU 温度",
            "gpu_power" => english ? "GPU Power" : "GPU 功率",
            "gpu_fan_speed" => english ? "GPU Fan Speed" : "GPU 风扇转速",
            "gpu_memory_usage" => english ? "VRAM Usage" : "显存占用",
            "memory_usage" => english ? "Memory Usage" : "内存占用",
            _ => id,
        };
    }
}
