using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SerialPortTool.Models;

namespace SerialPortTool.Services;

public sealed class SerialLogWriter : IDisposable
{
    private const long MaxLogFileBytes = 50L * 1024 * 1024;

    private readonly ConcurrentQueue<string> _queue = new();
    private readonly object _fileLock = new();
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _flushTask;
    private string? _logFilePath;
    private string? _safePortName;
    private string? _sessionTimestamp;
    private int _partIndex = 1;
    private SerialPortConfig? _config;

    public string? LogFilePath => _logFilePath;
    public bool IsActive => _writer != null;

    public void Start(SerialPortConfig config)
    {
        Stop();

        Directory.CreateDirectory(AppSettingsStore.LogsDirectory);
        _config = config;
        _safePortName = SanitizeFileName(config.PortName);
        _sessionTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _partIndex = 1;
        _logFilePath = BuildLogPath();

        OpenLogFile(isContinuation: false);

        _cts = new CancellationTokenSource();
        _flushTask = Task.Run(() => FlushLoop(_cts.Token));
    }

    public void AppendLine(string line)
    {
        if (_writer == null || string.IsNullOrEmpty(line)) return;
        _queue.Enqueue(line);
    }

    public void AppendBatch(IEnumerable<SerialDataModel> batch)
    {
        foreach (var data in batch)
            AppendLine(data.DisplayLine);
    }

    public void Flush() => FlushRemaining();

    public void Stop()
    {
        _cts?.Cancel();
        try { _flushTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
        _cts = null;
        _flushTask = null;

        FlushRemaining();

        lock (_fileLock)
        {
            CloseCurrentFile(rotated: false);
            _config = null;
            _safePortName = null;
            _sessionTimestamp = null;
            _partIndex = 1;
        }
    }

    private async Task FlushLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(100, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            FlushRemaining();
        }
    }

    private void FlushRemaining()
    {
        if (_writer == null) return;

        lock (_fileLock)
        {
            while (_queue.TryDequeue(out var line))
            {
                if (ShouldRotate())
                    RotateToNewFile();

                try { _writer.WriteLine(line); }
                catch { return; }

                if (ShouldRotate())
                    RotateToNewFile();
            }
            try { _writer.Flush(); } catch { }
        }
    }

    private bool ShouldRotate() => GetCurrentFileLength() >= MaxLogFileBytes;

    private long GetCurrentFileLength()
    {
        if (_writer?.BaseStream is FileStream fs)
            return fs.Length;
        return 0;
    }

    private void RotateToNewFile()
    {
        if (_writer == null || _config == null || _safePortName == null || _sessionTimestamp == null)
            return;

        var previousPath = _logFilePath;
        CloseCurrentFile(rotated: true);

        _partIndex++;
        _logFilePath = BuildLogPath();
        OpenLogFile(isContinuation: true, previousPath);
    }

    private string BuildLogPath()
    {
        string fileName = _partIndex <= 1
            ? $"{_safePortName}_{_sessionTimestamp}.log"
            : $"{_safePortName}_{_sessionTimestamp}_part{_partIndex:D3}.log";
        return Path.Combine(AppSettingsStore.LogsDirectory, fileName);
    }

    private void OpenLogFile(bool isContinuation, string? previousPath = null)
    {
        if (_config == null || string.IsNullOrEmpty(_logFilePath))
            return;

        var fileStream = new FileStream(_logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = false };

        if (isContinuation)
        {
            _writer.WriteLine($"# SerialPortTool Log (continued)");
            _writer.WriteLine($"# Segment started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(previousPath))
                _writer.WriteLine($"# Rotated from: {previousPath} (previous segment reached {MaxLogFileBytes / (1024 * 1024)} MB)");
        }
        else
        {
            var config = _config;
            _writer.WriteLine($"# SerialPortTool Log");
            _writer.WriteLine($"# Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _writer.WriteLine($"# Port: {config.PortName}, Baud: {config.BaudRate}, DataBits: {config.DataBits}, Parity: {config.Parity}, StopBits: {config.StopBits}, FlowControl: {config.FlowControl}");
            _writer.WriteLine($"# Max segment size: {MaxLogFileBytes / (1024 * 1024)} MB");
        }

        _writer.WriteLine();
    }

    private void CloseCurrentFile(bool rotated)
    {
        if (_writer == null) return;

        try
        {
            if (rotated)
                _writer.WriteLine($"# Rotated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (segment reached {MaxLogFileBytes / (1024 * 1024)} MB)");
            else
                _writer.WriteLine($"# Closed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _writer.WriteLine();
            _writer.Flush();
            _writer.Dispose();
        }
        catch { }
        _writer = null;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Unknown";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    public void Dispose() => Stop();
}
