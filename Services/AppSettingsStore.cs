using System;
using System.IO;
using System.Text.Json;
using SerialPortTool.Models;

namespace SerialPortTool.Services;

public static class AppSettingsStore
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SerialPortTool");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    /// <summary>日志目录：与可执行文件同级的 log 文件夹。</summary>
    public static string LogsDirectory => Path.Combine(AppContext.BaseDirectory, "log");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.Ports ??= new();
            settings.RegexHistory ??= new();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 忽略持久化失败，不影响主功能
        }
    }

    public static PortSettingsEntry? GetPortEntry(AppSettings settings, int portIndex)
    {
        if (portIndex < 0 || portIndex >= settings.Ports.Count)
            return null;
        return settings.Ports[portIndex];
    }

    public static void SetPortEntry(AppSettings settings, int portIndex, PortSettingsEntry entry)
    {
        while (settings.Ports.Count <= portIndex)
            settings.Ports.Add(new PortSettingsEntry());
        settings.Ports[portIndex] = entry;
    }
}
