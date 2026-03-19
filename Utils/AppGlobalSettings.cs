using System;
using System.IO;
using System.Text.Json;

namespace InFalsusSongPackStudio.Utils;

// 全局应用配置（持久化到 AppData），用于跨窗口共享基础路径设置。
public sealed class AppGlobalSettings
{
    public string GameDirectory { get; set; } = string.Empty;
    public string ThemeMode { get; set; } = "system";
    public bool AutoRenameWhenTargetLocked { get; set; } = true;
}

// 统一读写全局应用配置，避免窗口各自维护路径状态。
public static class AppGlobalSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsFilePath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "InFalsusSongPackStudio",
            "appsettings.json");

    public static AppGlobalSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new AppGlobalSettings();

            string json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppGlobalSettings>(json) ?? new AppGlobalSettings();
        }
        catch
        {
            return new AppGlobalSettings();
        }
    }

    public static void Save(AppGlobalSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        string dir = Path.GetDirectoryName(SettingsFilePath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    public static string LoadGameDirectory()
    {
        return (Load().GameDirectory ?? string.Empty).Trim();
    }

    public static void SaveGameDirectory(string gameDirectory)
    {
        var settings = Load();
        settings.GameDirectory = gameDirectory?.Trim() ?? string.Empty;
        Save(settings);
    }
}