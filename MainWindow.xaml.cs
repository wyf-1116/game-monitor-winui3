using System.Collections.ObjectModel;
using GameMonitor.Models;
using GameMonitor.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using Windows.UI;

namespace GameMonitor;

public sealed partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly MonitorService _monitorService = new();
    private readonly DispatcherTimer _timer = new();
    private readonly string? _gpuName = GraphicsAdapterService.ReadName();
    private AppSettings _settings;
    private bool _isFullScreen;

    public ObservableCollection<MetricCard> MetricCards { get; } = [];
    public ObservableCollection<DisplayItemOption> DisplayOptions { get; } = [];

    public MainWindow()
    {
        InitializeComponent();

        _settings = _settingsService.Load();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));

        CpuNameText.Text = ReadCpuName();

        _timer.Tick += (_, _) => RefreshSnapshot();
        Closed += (_, _) => _timer.Stop();

        ApplyLanguage();
        ResetTimer();
        RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        var snapshot = _monitorService.Read(_settings);
        var visibleIds = _settings.DisplayItems.ToHashSet(StringComparer.Ordinal);

        UpdateMetricCards(snapshot.Metrics.Where(metric => visibleIds.Contains(metric.Id)));

        FpsValueText.Visibility = visibleIds.Contains("fps_current")
            ? Visibility.Visible
            : Visibility.Collapsed;
        FrameTimeText.Visibility = visibleIds.Contains("frame_time")
            ? Visibility.Visible
            : Visibility.Collapsed;
        LowFpsText.Visibility = visibleIds.Contains("fps_1_low")
            ? Visibility.Visible
            : Visibility.Collapsed;
        AverageFpsText.Visibility = visibleIds.Contains("fps_average")
            ? Visibility.Visible
            : Visibility.Collapsed;

        FpsValueText.Text = Format(snapshot.Fps.Current, 0);
        FrameTimeText.Text = _settings.Language == "en"
            ? $"Frame time {Format(snapshot.Fps.FrameTimeMs, 2)} ms"
            : $"帧生成 {Format(snapshot.Fps.FrameTimeMs, 2)} ms";
        LowFpsText.Text = $"1% Low {Format(snapshot.Fps.LowOnePercent, 1)} FPS";
        AverageFpsText.Text = _settings.Language == "en"
            ? $"Average {Format(snapshot.Fps.Average, 1)} FPS"
            : $"平均 {Format(snapshot.Fps.Average, 1)} FPS";

        StatusText.Text = snapshot.Status;
        UpdatedAtText.Text = snapshot.UpdatedAt.ToString("HH:mm:ss");
        ConnectionIndicator.Fill = new SolidColorBrush(
            snapshot.IsConnected
                ? Color.FromArgb(255, 70, 211, 163)
                : Color.FromArgb(255, 255, 99, 99));
    }

    private void UpdateMetricCards(IEnumerable<MetricSnapshot> metrics)
    {
        var snapshots = metrics.ToList();
        var structureChanged =
            MetricCards.Count != snapshots.Count ||
            MetricCards.Where((card, index) => card.Id != snapshots[index].Id).Any();

        if (structureChanged)
        {
            MetricCards.Clear();
            foreach (var metric in snapshots)
            {
                MetricCards.Add(new MetricCard
                {
                    Id = metric.Id,
                    Label = metric.Label,
                    Value = metric.Value,
                });
            }

            return;
        }

        for (var index = 0; index < snapshots.Count; index++)
        {
            MetricCards[index].Label = snapshots[index].Label;
            MetricCards[index].Value = snapshots[index].Value;
        }
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var isLandscape = e.NewSize.Width / Math.Max(1, e.NewSize.Height) > 1.2;
        if (isLandscape)
        {
            PrimaryColumn.Width = new GridLength(0.43, GridUnitType.Star);
            SecondaryColumn.Width = new GridLength(0.57, GridUnitType.Star);
            Grid.SetRow(MetricsGrid, 0);
            Grid.SetRowSpan(MetricsGrid, 2);
            Grid.SetColumn(MetricsGrid, 1);
            MetricsGrid.Margin = new Thickness(20, 0, 0, 0);
            FpsValueText.FontSize = Math.Clamp(e.NewSize.Height * 0.18, 76, 166);
        }
        else
        {
            PrimaryColumn.Width = new GridLength(1, GridUnitType.Star);
            SecondaryColumn.Width = new GridLength(0);
            Grid.SetRow(MetricsGrid, 2);
            Grid.SetRowSpan(MetricsGrid, 1);
            Grid.SetColumn(MetricsGrid, 0);
            MetricsGrid.Margin = new Thickness(0, 16, 0, 0);
            FpsValueText.FontSize = Math.Clamp(e.NewSize.Width * 0.18, 72, 150);
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateSettingsDialog();
        SettingsDialog.XamlRoot = RootGrid.XamlRoot;
        await SettingsDialog.ShowAsync();
    }

    private void SettingsDialog_PrimaryButtonClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        var updatedSettings = new AppSettings
        {
            RefreshIntervalMs = double.IsFinite(RefreshIntervalBox.Value)
                ? (int)Math.Clamp(RefreshIntervalBox.Value, 250, 10_000)
                : _settings.RefreshIntervalMs,
            TemperatureUnit = SelectedTag(TemperatureUnitBox, "C"),
            Language = SelectedTag(LanguageBox, "zh"),
            DisplayItems = DisplayOptions
                .Where(option => option.IsEnabled)
                .Select(option => option.Id)
                .ToList(),
        };

        try
        {
            _settingsService.Save(updatedSettings, SelectedSettingsLocation());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            args.Cancel = true;
            SettingsErrorText.Text = _settings.Language == "en"
                ? $"Could not save configuration to the selected location. Check folder write permissions. {exception.Message}"
                : $"无法将配置保存到所选位置，请检查目录写入权限。{exception.Message}";
            SettingsErrorText.Visibility = Visibility.Visible;
            return;
        }

        _settings = updatedSettings;
        ApplyLanguage();
        ResetTimer();
        RefreshSnapshot();
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        _isFullScreen = !_isFullScreen;
        AppWindow.SetPresenter(
            _isFullScreen
                ? AppWindowPresenterKind.FullScreen
                : AppWindowPresenterKind.Default);
    }

    private void PopulateSettingsDialog()
    {
        RefreshIntervalBox.Value = _settings.RefreshIntervalMs;
        TemperatureUnitBox.SelectedIndex = _settings.TemperatureUnit == "F" ? 1 : 0;
        LanguageBox.SelectedIndex = _settings.Language == "en" ? 1 : 0;
        SettingsLocationBox.SelectedIndex = _settingsService.StorageLocation == SettingsStorageLocation.ProgramDirectory ? 1 : 0;
        SettingsPathText.Text = _settingsService.GetSettingsPath(SelectedSettingsLocation());
        SettingsErrorText.Visibility = Visibility.Collapsed;

        var enabled = _settings.DisplayItems.ToHashSet(StringComparer.Ordinal);
        DisplayOptions.Clear();
        foreach (var id in MetricCatalog.AllIds)
        {
            DisplayOptions.Add(new DisplayItemOption
            {
                Id = id,
                Label = MetricCatalog.Label(id, _settings.Language),
                IsEnabled = enabled.Contains(id),
            });
        }
    }

    private void ApplyLanguage()
    {
        var english = _settings.Language == "en";
        GpuNameText.Text = _gpuName ?? (english ? "GPU model unavailable" : "无法获取显卡型号");
        AppTitleBar.Subtitle = english ? "Direct shared-memory monitor" : "共享内存直读监控";
        SettingsDialog.Title = english ? "Settings" : "设置";
        SettingsDialog.PrimaryButtonText = english ? "Save" : "保存";
        SettingsDialog.CloseButtonText = english ? "Cancel" : "取消";
        RefreshLabel.Text = english ? "Refresh interval" : "刷新间隔";
        RefreshIntervalBox.Header = english ? "Milliseconds" : "毫秒";
        TemperatureUnitBox.Header = english ? "Temperature unit" : "温度单位";
        CelsiusOption.Content = english ? "Celsius (°C)" : "摄氏度 (°C)";
        FahrenheitOption.Content = english ? "Fahrenheit (°F)" : "华氏度 (°F)";
        LanguageBox.Header = english ? "Language" : "语言";
        ChineseOption.Content = english ? "Chinese" : "中文";
        SettingsLocationBox.Header = english ? "Configuration location" : "配置保存位置";
        LocalAppDataOption.Content = english ? "User folder (%LocalAppData%)" : "用户目录 (%LocalAppData%)";
        ProgramDirectoryOption.Content = english ? "Program directory (EXE folder)" : "程序目录 (EXE 所在目录)";
        DisplayItemsLabel.Text = english ? "Display items" : "显示项目";
        SettingsButton.SetValue(ToolTipService.ToolTipProperty, english ? "Settings" : "设置");
        FullScreenButton.SetValue(ToolTipService.ToolTipProperty, english ? "Full screen" : "全屏");
    }

    private void ResetTimer()
    {
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(_settings.RefreshIntervalMs);
        _timer.Start();
    }

    private SettingsStorageLocation SelectedSettingsLocation() =>
        SettingsLocationBox.SelectedIndex == 1
            ? SettingsStorageLocation.ProgramDirectory
            : SettingsStorageLocation.LocalAppData;

    private void SettingsLocationBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsPathText is null || SettingsErrorText is null)
        {
            return;
        }

        SettingsPathText.Text = _settingsService.GetSettingsPath(SelectedSettingsLocation());
        SettingsErrorText.Visibility = Visibility.Collapsed;
    }

    private static string SelectedTag(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static string Format(double? value, int decimals) =>
        value.HasValue ? Math.Round(value.Value, decimals).ToString($"F{decimals}") : "--";

    private static string ReadCpuName()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "ProcessorNameString",
                "CPU")?.ToString()?.Trim() ?? "CPU";
        }
        catch
        {
            return "CPU";
        }
    }
}
