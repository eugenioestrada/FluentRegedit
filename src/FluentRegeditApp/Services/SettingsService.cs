using System;
using System.IO;
using System.Text.Json;

namespace FluentRegeditApp.Services;

public enum AppTheme { System, Light, Dark }
public enum RegView { Default, Registry32, Registry64 }

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public RegView View { get; set; } = RegView.Default;
    public bool ConfirmDestructive { get; set; } = true;
    public int RecentLocationsLimit { get; set; } = 12;
    public bool RegexSearch { get; set; } = false;
}

public sealed class SettingsService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions s_opts = new() { WriteIndented = true };

    public SettingsService(string? overridePath = null)
    {
        _path = overridePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FluentRegedit", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public string FilePath => _path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, s_opts) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, s_opts);
        File.WriteAllText(_path, json);
    }
}
