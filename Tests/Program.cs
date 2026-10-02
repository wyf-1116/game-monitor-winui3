using GameMonitor.Models;
using GameMonitor.Services;

var tests = new (string Name, Action<TestDirectories> Run)[]
{
    ("First run defaults to LocalAppData without creating files", directories =>
    {
        var service = directories.CreateService();
        Check(service.StorageLocation == SettingsStorageLocation.LocalAppData, "Default location");
        Check(service.SettingsPath == directories.LocalConfig, "Default path");
        Check(service.Load().RefreshIntervalMs == 1000, "Default settings");
        Check(!Directory.Exists(directories.LocalAppData), "Loading should not create directories");
    }),
    ("Existing user configuration loads without a location record", directories =>
    {
        directories.Write(directories.LocalConfig, "{\"language\":\"en\",\"refresh_interval_ms\":2000}");
        var settings = directories.CreateService().Load();
        Check(settings.Language == "en" && settings.RefreshIntervalMs == 2000, "Existing settings");
    }),
    ("User configuration round-trips and normalizes values", directories =>
    {
        var service = directories.CreateService();
        service.Save(new AppSettings
        {
            Language = "en",
            TemperatureUnit = "F",
            RefreshIntervalMs = 100,
            DisplayItems = ["cpu_usage", "cpu_usage", "invalid"],
        }, SettingsStorageLocation.LocalAppData);
        var loaded = directories.CreateService().Load();
        Check(loaded.Language == "en" && loaded.TemperatureUnit == "F", "Settings round-trip");
        Check(loaded.RefreshIntervalMs == 250, "Refresh bounds");
        Check(loaded.DisplayItems.SequenceEqual(["cpu_usage"]), "Metric normalization");
    }),
    ("Program-directory choice survives restart and preserves previous configuration", directories =>
    {
        var service = directories.CreateService();
        service.Save(new AppSettings { RefreshIntervalMs = 1500 }, SettingsStorageLocation.LocalAppData);
        var previous = File.ReadAllText(directories.LocalConfig);
        service.Save(new AppSettings { Language = "en", RefreshIntervalMs = 2500 }, SettingsStorageLocation.ProgramDirectory);
        var restarted = directories.CreateService();
        Check(restarted.StorageLocation == SettingsStorageLocation.ProgramDirectory, "Saved program location");
        Check(restarted.SettingsPath == directories.ProgramConfig, "EXE-directory path");
        Check(restarted.Load().RefreshIntervalMs == 2500, "Read selected configuration");
        Check(File.ReadAllText(directories.LocalConfig) == previous, "Previous file retained");
    }),
    ("Switching back to LocalAppData overrides the retained program configuration", directories =>
    {
        var service = directories.CreateService();
        service.Save(new AppSettings { RefreshIntervalMs = 2500 }, SettingsStorageLocation.ProgramDirectory);
        var previous = File.ReadAllText(directories.ProgramConfig);
        service.Save(new AppSettings { RefreshIntervalMs = 3500 }, SettingsStorageLocation.LocalAppData);
        var restarted = directories.CreateService();
        Check(restarted.StorageLocation == SettingsStorageLocation.LocalAppData, "Saved user location");
        Check(restarted.Load().RefreshIntervalMs == 3500, "Retained program file must not win");
        Check(File.ReadAllText(directories.ProgramConfig) == previous, "Program configuration retained");
    }),
    ("Missing selected configuration retains the chosen location", directories =>
    {
        directories.Write(directories.LocationRecord, "\"program_directory\"");
        directories.Write(directories.LocalConfig, "{\"refresh_interval_ms\":8000}");
        var service = directories.CreateService();
        Check(service.Load().RefreshIntervalMs == 1000, "Defaults instead of unrelated file");
        Check(service.StorageLocation == SettingsStorageLocation.ProgramDirectory, "Location retained");
        service.Save(new AppSettings(), service.StorageLocation);
        Check(File.Exists(directories.ProgramConfig), "Save recreates selected configuration");
    }),
    ("Invalid selected configuration uses defaults without switching location", directories =>
    {
        directories.Write(directories.LocationRecord, "\"program_directory\"");
        directories.Write(directories.ProgramConfig, "not json");
        var service = directories.CreateService();
        Check(service.Load().Language == "zh", "Default settings on malformed JSON");
        Check(service.StorageLocation == SettingsStorageLocation.ProgramDirectory, "Location retained");
    }),
    ("Invalid location records safely fall back to LocalAppData", directories =>
    {
        foreach (var invalid in new[] { "not json", "\"unknown\"", "123", "null" })
        {
            directories.Write(directories.LocationRecord, invalid);
            Check(directories.CreateService().StorageLocation == SettingsStorageLocation.LocalAppData, "Safe fallback");
        }
    }),
    ("A failed target write does not switch the active location", directories =>
    {
        var service = directories.CreateService();
        service.Save(new AppSettings { RefreshIntervalMs = 2000 }, SettingsStorageLocation.LocalAppData);
        Directory.CreateDirectory(directories.ProgramConfig);
        ExpectWriteFailure(() => service.Save(new AppSettings { RefreshIntervalMs = 3000 }, SettingsStorageLocation.ProgramDirectory));
        Check(service.StorageLocation == SettingsStorageLocation.LocalAppData, "Active location unchanged");
        Check(directories.CreateService().Load().RefreshIntervalMs == 2000, "Previous settings unchanged");
        Check(!File.Exists(directories.LocationRecord), "No preference recorded for failed save");
        Check(!Directory.EnumerateFiles(directories.ApplicationDirectory, "*.tmp").Any(), "Temporary files cleaned");
    }),
    ("A failed location-record write keeps the previous configuration active", directories =>
    {
        var service = directories.CreateService();
        service.Save(new AppSettings { RefreshIntervalMs = 2000 }, SettingsStorageLocation.LocalAppData);
        Directory.CreateDirectory(directories.LocationRecord);
        ExpectWriteFailure(() => service.Save(new AppSettings { RefreshIntervalMs = 4000 }, SettingsStorageLocation.ProgramDirectory));
        Check(service.StorageLocation == SettingsStorageLocation.LocalAppData, "Active location unchanged");
        Check(directories.CreateService().Load().RefreshIntervalMs == 2000, "Previous settings remain active after restart");
    }),
    ("A failed overwrite preserves the last complete configuration", directories =>
    {
        var service = directories.CreateService();
        service.Save(new AppSettings { RefreshIntervalMs = 2000 }, SettingsStorageLocation.LocalAppData);
        var previous = File.ReadAllText(directories.LocalConfig);
        File.SetAttributes(directories.LocalConfig, FileAttributes.ReadOnly);
        try
        {
            ExpectWriteFailure(() => service.Save(new AppSettings { RefreshIntervalMs = 4000 }, SettingsStorageLocation.LocalAppData));
            Check(File.ReadAllText(directories.LocalConfig) == previous, "Previous file must be intact");
        }
        finally
        {
            File.SetAttributes(directories.LocalConfig, FileAttributes.Normal);
        }
    }),
    ("Null display-item lists do not break settings loading", directories =>
    {
        directories.Write(directories.LocalConfig, "{\"display_items\":null}");
        Check(directories.CreateService().Load().DisplayItems.SequenceEqual(MetricCatalog.AllIds), "Default metric list");
    }),
};

var failed = 0;
foreach (var (name, run) in tests)
{
    using var directories = new TestDirectories();
    try
    {
        run(directories);
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failed}/{tests.Length} checks passed.");
return failed == 0 ? 0 : 1;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectWriteFailure(Action save)
{
    try
    {
        save();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        return;
    }

    throw new InvalidOperationException("Expected an IO or access error");
}

sealed class TestDirectories : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "GameMonitorWinUI3.Tests", Guid.NewGuid().ToString("N"));

    public string ApplicationDirectory => Path.Combine(_root, "application");
    public string LocalAppData => Path.Combine(_root, "local-app-data");
    public string LocalConfig => Path.Combine(LocalAppData, "GameMonitorWinUI3", "config.json");
    public string ProgramConfig => Path.Combine(ApplicationDirectory, "config.json");
    public string LocationRecord => Path.Combine(LocalAppData, "GameMonitorWinUI3", "storage-location.json");

    public SettingsService CreateService() => new(ApplicationDirectory, LocalAppData);

    public void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        var root = Path.GetFullPath(_root);
        var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GameMonitorWinUI3.Tests")) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unexpected test directory");
        }

        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
