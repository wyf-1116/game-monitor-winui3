using System.Text.Json;
using System.Text.Json.Serialization;
using GameMonitor.Models;

namespace GameMonitor.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    private readonly string _applicationDirectory;
    private readonly string _localSettingsDirectory;
    private readonly string _storageLocationPath;

    public SettingsStorageLocation StorageLocation { get; private set; }
    public string SettingsPath => GetSettingsPath(StorageLocation);

    public SettingsService()
        : this(AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal SettingsService(string applicationDirectory, string localApplicationDataDirectory)
    {
        _applicationDirectory = Path.GetFullPath(applicationDirectory);
        _localSettingsDirectory = Path.Combine(Path.GetFullPath(localApplicationDataDirectory), "GameMonitorWinUI3");
        _storageLocationPath = Path.Combine(_localSettingsDirectory, "storage-location.json");
        StorageLocation = ReadStorageLocation();
    }

    public string GetSettingsPath(SettingsStorageLocation location) => location switch
    {
        SettingsStorageLocation.LocalAppData => Path.Combine(_localSettingsDirectory, "config.json"),
        SettingsStorageLocation.ProgramDirectory => Path.Combine(_applicationDirectory, "config.json"),
        _ => throw new ArgumentOutOfRangeException(nameof(location)),
    };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(SettingsPath),
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
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings, SettingsStorageLocation location)
    {
        var settingsPath = GetSettingsPath(location);
        Normalize(settings);
        WriteJson(settingsPath, settings);

        // Keep the previous location active until its replacement configuration is saved.
        if (location != StorageLocation)
        {
            WriteJson(_storageLocationPath, location);
        }

        StorageLocation = location;
    }

    private SettingsStorageLocation ReadStorageLocation()
    {
        try
        {
            var location = JsonSerializer.Deserialize<SettingsStorageLocation>(
                File.ReadAllText(_storageLocationPath), JsonOptions);
            return Enum.IsDefined(location) ? location : SettingsStorageLocation.LocalAppData;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return SettingsStorageLocation.LocalAppData;
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.RefreshIntervalMs = Math.Clamp(settings.RefreshIntervalMs, 250, 10_000);
        settings.Language = settings.Language == "en" ? "en" : "zh";
        settings.TemperatureUnit = settings.TemperatureUnit == "F" ? "F" : "C";
        var validIds = MetricCatalog.AllIds.ToHashSet(StringComparer.Ordinal);
        settings.DisplayItems = (settings.DisplayItems ?? MetricCatalog.AllIds.ToList())
            .Where(validIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
