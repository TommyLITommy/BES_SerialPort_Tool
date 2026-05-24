using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SerialPortTool.Helpers;

public static class NotepadPlusPlusLauncher
{
    private static string? _cachedExePath;
    private static string? _userConfiguredPath;

    public static void SetUserConfiguredPath(string? path) => _userConfiguredPath = path;

    public static bool TryOpenFile(string? filePath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            errorMessage = "当前没有可用的日志文件，请先打开串口";
            return false;
        }

        var exe = FindExecutable();
        if (exe == null)
        {
            errorMessage =
                "未找到 Notepad++。可在 settings.json 中设置 NotepadPlusPlusPath 为 notepad++.exe 完整路径";
            return false;
        }

        string fullPath = Path.GetFullPath(filePath);

        try
        {
            // 使用 Shell 启动 GUI 程序（比 UseShellExecute=false 更可靠）
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"\"{fullPath}\"",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory
            };
            Process.Start(psi);
            return true;
        }
        catch
        {
            try
            {
                // 备用：.NET 推荐的 Shell 启动方式
                Process.Start(new ProcessStartInfo(exe, $"\"{fullPath}\"") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"启动 Notepad++ 失败: {ex.Message}";
                return false;
            }
        }
    }

    private static string? FindExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_userConfiguredPath) && File.Exists(_userConfiguredPath))
            return _cachedExePath = _userConfiguredPath;

        if (!string.IsNullOrEmpty(_cachedExePath) && File.Exists(_cachedExePath))
            return _cachedExePath;

        var candidates = new List<string>();

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        candidates.Add(Path.Combine(programFiles, "Notepad++", "notepad++.exe"));
        candidates.Add(Path.Combine(programFilesX86, "Notepad++", "notepad++.exe"));
        candidates.Add(Path.Combine(localAppData, "Programs", "Notepad++", "notepad++.exe"));
        candidates.Add(Path.Combine(localAppData, "Notepad++", "notepad++.exe"));

        var registryPath = FindFromRegistry();
        if (!string.IsNullOrWhiteSpace(registryPath))
            candidates.Add(registryPath);

        var wherePath = FindViaWhereCommand();
        if (!string.IsNullOrWhiteSpace(wherePath))
            candidates.Add(wherePath);

        var envPath = Environment.GetEnvironmentVariable("NOTEPADPP_PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
            candidates.Add(envPath.Trim('"'));

        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return _cachedExePath = Path.GetFullPath(path);
        }

        return null;
    }

    private static string? FindFromRegistry()
    {
        string? path = ReadUninstallKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Notepad++")
            ?? ReadUninstallKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Notepad++")
            ?? ReadUninstallKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Notepad++");

        return path;
    }

    private static string? ReadUninstallKey(RegistryKey root, string subKeyPath)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key == null) return null;

            var displayIcon = key.GetValue("DisplayIcon") as string;
            if (!string.IsNullOrWhiteSpace(displayIcon))
            {
                string iconPath = displayIcon.Split(',')[0].Trim().Trim('"');
                if (File.Exists(iconPath)) return iconPath;
            }

            var installLocation = key.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                string exe = Path.Combine(installLocation.TrimEnd('\\', '/'), "notepad++.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? FindViaWhereCommand()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = "notepad++.exe",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null) return null;

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);

            if (string.IsNullOrWhiteSpace(output)) return null;

            string firstLine = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            return File.Exists(firstLine) ? firstLine : null;
        }
        catch
        {
            return null;
        }
    }
}
