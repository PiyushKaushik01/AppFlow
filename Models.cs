using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace AppFlow;

public sealed class AppEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Source { get; set; } = "Manual";
    [JsonIgnore] public string FileName => System.IO.Path.GetFileName(Path);
    [JsonIgnore] public ImageSource? IconSource
    {
        get
        {
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Path);
                if (icon == null) return null;
                var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(24, 24));
                source.Freeze(); return source;
            }
            catch { return null; }
        }
    }
    public override string ToString() => Name;
}

public sealed class AutomationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public string TriggerAppId { get; set; } = "";
    public List<string> ActionAppIds { get; set; } = [];
    public List<string> CloseAppIds { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public int DelayMs { get; set; } = 1000;
    public bool RelaunchRunning { get; set; }
    public bool TriggerOnWindow { get; set; }
    [JsonIgnore] public string TriggerName { get; set; } = "Choose a trigger";
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool LaunchMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public int DefaultDelayMs { get; set; } = 1000;
    public bool Notifications { get; set; } = true;
}

public sealed class AppData
{
    public List<AppEntry> Applications { get; set; } = [];
    public List<AutomationProfile> Profiles { get; set; } = [];
    public AppSettings Settings { get; set; } = new();
}

public static class DataStore
{
    private static readonly string DirectoryPath = Environment.GetEnvironmentVariable("APPFLOW_DATA_DIR") is { Length: > 0 } overridePath
        ? System.IO.Path.GetFullPath(overridePath)
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppFlow");
    private static readonly string FilePath = System.IO.Path.Combine(DirectoryPath, "data.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static AppData Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath), Options) ?? new(); }
        catch { }
        return new();
    }
    public static void Save(AppData data)
    {
        Directory.CreateDirectory(DirectoryPath);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        File.Move(tmp, FilePath, true);
    }
}
