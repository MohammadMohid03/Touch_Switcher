using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchSwitcher.Configuration;

public enum SwitchMode
{
    Application,
    Window
}

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public SwitchMode SwitchMode { get; set; } = SwitchMode.Application;
    public int MinSwipeDistance { get; set; } = 100;
    public double HorizontalVerticalRatio { get; set; } = 2.0;
    public int GestureTimeoutMs { get; set; } = 500;
    public int CooldownMs { get; set; } = 250;
    public int RequiredFingers { get; set; } = 3;
    public int Sensitivity { get; set; } = 55;
    public bool StartWithWindows { get; set; }
    public bool DisableInFullscreen { get; set; } = true;
    public bool EnableSlideAnimation { get; set; } = true;
    public List<string> ExcludedApplications { get; set; } = new();
}

public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? Changed;

    public SettingsManager()
    {
        string folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TouchSwitcher");
        Directory.CreateDirectory(folder);
        _path = System.IO.Path.Combine(folder, "settings.json");
        Current = Load();
    }

    public void Save(AppSettings settings)
    {
        settings.MinSwipeDistance = Math.Clamp(settings.MinSwipeDistance, 20, 400);
        settings.HorizontalVerticalRatio = Math.Clamp(settings.HorizontalVerticalRatio, 1.1, 8);
        settings.GestureTimeoutMs = Math.Clamp(settings.GestureTimeoutMs, 120, 3000);
        settings.CooldownMs = Math.Clamp(settings.CooldownMs, 50, 2000);
        settings.RequiredFingers = Math.Clamp(settings.RequiredFingers, 3, 4);
        settings.Sensitivity = Math.Clamp(settings.Sensitivity, 0, 100);
        settings.ExcludedApplications = settings.ExcludedApplications
            .Select(NormalizeExe)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Current = settings;
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
        StartupManager.Apply(settings.StartWithWindows);
        Changed?.Invoke(settings);
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions);
            return loaded ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static string NormalizeExe(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string name = System.IO.Path.GetFileName(value.Trim());
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name += ".exe";
        }

        return name;
    }
}
