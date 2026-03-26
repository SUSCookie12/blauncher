using System;
using System.IO;
using System.Text.Json;

namespace BLauncher.Services;

public class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BLauncher");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public string Username { get; set; } = "Player";
    public string LastVersion { get; set; } = "1.21.11";

    public static SettingsService Load()
    {
        try { if (File.Exists(SettingsPath)) return JsonSerializer.Deserialize<SettingsService>(File.ReadAllText(SettingsPath)) ?? new SettingsService(); }
        catch { }
        return new SettingsService();
    }

    public static void Save(SettingsService settings)
    {
        try { if (!Directory.Exists(SettingsDir)) Directory.CreateDirectory(SettingsDir); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings)); }
        catch { }
    }
}
