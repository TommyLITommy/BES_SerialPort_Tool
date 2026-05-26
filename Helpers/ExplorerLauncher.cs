using System;
using System.Diagnostics;
using System.IO;

namespace SerialPortTool.Helpers;

public static class ExplorerLauncher
{
    public static bool TryRevealInExplorer(string filePath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            errorMessage = "当前没有可用的日志文件，请先打开串口";
            return false;
        }

        string fullPath = Path.GetFullPath(filePath);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{fullPath}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"打开文件夹失败: {ex.Message}";
            return false;
        }
    }

    public static bool TryOpenFolder(string folderPath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            errorMessage = "日志文件夹不存在";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(folderPath),
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"打开文件夹失败: {ex.Message}";
            return false;
        }
    }
}
