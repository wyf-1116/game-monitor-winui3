using System.Text.Json;
using GameMonitor.Models;

namespace GameMonitor.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameMonitor");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "config.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_settingsPath),
                JsonOptions) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Normalize(settings);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static void Normalize(AppSettings settings)
    {
        settings.RefreshIntervalMs = Math.Clamp(settings.RefreshIntervalMs, 250, 10_000);
        settings.Language = settings.Language == "en" ? "en" : "zh";
        settings.TemperatureUnit = settings.TemperatureUnit == "F" ? "F" : "C";
        var validIds = MetricCatalog.AllIds.ToHashSet(StringComparer.Ordinal);
        settings.DisplayItems = settings.DisplayItems
            .Where(validIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
