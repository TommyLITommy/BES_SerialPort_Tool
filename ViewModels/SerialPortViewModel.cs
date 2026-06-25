using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO.Ports;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SerialPortTool.Helpers;
using SerialPortTool.Models;
using SerialPortTool.Services;

namespace SerialPortTool.ViewModels;

public class SerialPortViewModel : INotifyPropertyChanged
{
    private readonly SerialPortService _service;
    private readonly SerialLogWriter _logWriter = new();
    private readonly BtsnoopWriter _btsnoopWriter = new();
    private readonly BesHciLogParser _hciParser = new();
    private readonly WiresharkPipeServer _wiresharkPipeServer = new();
    private readonly EllisysSender _ellisysSender = new();
    private readonly object _hciLock = new();
    private readonly AppSettings _appSettings;
    private readonly int _portIndex;
    private readonly Dispatcher _dispatcher;
    private readonly object _dataLock = new();
    private readonly List<SerialDataModel> _allData = new(); // 使用List而不是ObservableCollection，避免UI绑定开销
    private readonly List<SerialDataModel> _filteredData = new();
    private const int DisplayLineLimit = 500;
    private const int DefaultPresetCount = 5;
    private const int MaxPresetCount = 20;
    private const int MaxFilterRegexHistory = 20;
    private Regex? _cachedFilterRegex;
    private string _appliedFilterDisplayText = "";
    private CancellationTokenSource? _batchCts;
    private bool _isBatchSending;
    private DispatcherTimer? _uptimeTimer;
    private DateTime? _portOpenedAt;
    private bool _wiresharkPipeEnabled;
    private bool _ellisysEnabled;
    private string _ellisysEndpoint = "127.0.0.1:24352";
    private string _ellisysTransport = "UDP";
    private string _hciRealtimeStatus = "";

    public SerialPortService Service => _service;
    public SerialPortConfig Config => _service.Config;

    // 使用ObservableCollection只暴露给UI，但内部用List管理
    public ObservableCollection<SerialDataModel> AllData { get; } = new();
    public ObservableCollection<SerialDataModel> FilteredDisplayData { get; } = new();
    public ObservableCollection<string> AvailablePorts { get; } = new();
    public ObservableCollection<PresetCommandItem> PresetCommands { get; } = new();
    public ObservableCollection<string> FilterRegexSuggestions { get; } = new();

    public static string[] PresetFormatOptions => PresetCommandItem.FormatOptions;

    public static int[] BaudRateList => SerialPortConfig.BaudRateList;
    public static int[] DataBitsList => SerialPortConfig.DataBitsList;
    public static string[] ParityList => SerialPortConfig.ParityList;
    public static string[] StopBitsList => SerialPortConfig.StopBitsList;
    public static string[] FlowControlList => SerialPortConfig.FlowControlList;
    public string[] EllisysTransportOptions { get; } = new[] { "UDP", "TCP" };

    private string _statusMessage = "就绪";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
    }

    private string _portRunTimeText = "";
    public string PortRunTimeText
    {
        get => _portRunTimeText;
        private set { _portRunTimeText = value; OnPropertyChanged(nameof(PortRunTimeText)); }
    }

    private int _rxCount = 0;
    public int RxCount
    {
        get => _rxCount;
        set { _rxCount = value; OnPropertyChanged(nameof(RxCount)); }
    }

    private int _txCount = 0;
    public int TxCount
    {
        get => _txCount;
        set { _txCount = value; OnPropertyChanged(nameof(TxCount)); }
    }

    private long _totalAllLineCount;
    private long _totalFilteredLineCount;

    /// <summary>原始日志累计条数（不受内存缓冲上限影响）。</summary>
    public long AllDataCount { get { lock (_dataLock) return _totalAllLineCount; } }

    /// <summary>匹配日志累计条数。</summary>
    public long FilteredCount { get { lock (_dataLock) return _totalFilteredLineCount; } }

    public string AppliedFilterDisplayText
    {
        get => _appliedFilterDisplayText;
        private set { _appliedFilterDisplayText = value; OnPropertyChanged(nameof(AppliedFilterDisplayText)); }
    }

    private bool _loopBatchSend;
    public bool LoopBatchSend
    {
        get => _loopBatchSend;
        set { _loopBatchSend = value; OnPropertyChanged(nameof(LoopBatchSend)); PersistSettings(); }
    }

    private double _presetDrawerWidth = 400;
    public double PresetDrawerWidth
    {
        get => _presetDrawerWidth;
        set
        {
            var clamped = Math.Clamp(value, 300, 640);
            if (Math.Abs(_presetDrawerWidth - clamped) < 0.5) return;
            _presetDrawerWidth = clamped;
            OnPropertyChanged(nameof(PresetDrawerWidth));
            PersistSettings();
        }
    }

    public bool IsBatchSending
    {
        get => _isBatchSending;
        private set { _isBatchSending = value; OnPropertyChanged(nameof(IsBatchSending)); }
    }

    public bool CanRemovePresets => PresetCommands.Count > 1;

    public bool HasActiveLog =>
        !string.IsNullOrEmpty(_logWriter.LogFilePath) && File.Exists(_logWriter.LogFilePath);

    public string? CurrentLogFilePath => _logWriter.LogFilePath;
    public string? CurrentBtsnoopFilePath => _btsnoopWriter.FilePath;

    public bool WiresharkPipeEnabled
    {
        get => _wiresharkPipeEnabled;
        set
        {
            if (_wiresharkPipeEnabled == value) return;
            _wiresharkPipeEnabled = value;
            OnPropertyChanged(nameof(WiresharkPipeEnabled));
            PersistSettings();
            ApplyRealtimeOutputState();
        }
    }

    public bool EllisysEnabled
    {
        get => _ellisysEnabled;
        set
        {
            if (_ellisysEnabled == value) return;
            _ellisysEnabled = value;
            OnPropertyChanged(nameof(EllisysEnabled));
            PersistSettings();
            ApplyRealtimeOutputState();
        }
    }

    public string EllisysEndpoint
    {
        get => _ellisysEndpoint;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "127.0.0.1:24352" : value.Trim();
            if (_ellisysEndpoint == normalized) return;
            _ellisysEndpoint = normalized;
            OnPropertyChanged(nameof(EllisysEndpoint));
            PersistSettings();
            if (EllisysEnabled)
                ApplyRealtimeOutputState();
        }
    }

    public string EllisysTransport
    {
        get => _ellisysTransport;
        set
        {
            var normalized = value?.Equals("TCP", StringComparison.OrdinalIgnoreCase) == true ? "TCP" : "UDP";
            if (_ellisysTransport == normalized) return;
            _ellisysTransport = normalized;
            OnPropertyChanged(nameof(EllisysTransport));
            PersistSettings();
            if (EllisysEnabled)
                ApplyRealtimeOutputState();
        }
    }

    public string HciRealtimeStatus
    {
        get => _hciRealtimeStatus;
        private set { _hciRealtimeStatus = value; OnPropertyChanged(nameof(HciRealtimeStatus)); }
    }

    public ICommand OpenCloseCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand RefreshFilterCommand { get; }
    public ICommand ClosePortCommand { get; }
    public ICommand AddPresetCommand { get; }
    public ICommand RemovePresetCommand { get; }
    public ICommand SendPresetCommand { get; }
    public ICommand StartBatchSendCommand { get; }
    public ICommand StopBatchSendCommand { get; }
    public ICommand OpenLogInNotepadPlusPlusCommand { get; }
    public ICommand OpenLogFolderCommand { get; }

    public event EventHandler? RequestClose;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RequestScrollToEnd;

    public SerialPortViewModel(int portIndex, AppSettings appSettings)
    {
        _portIndex = portIndex;
        _appSettings = appSettings;
        _dispatcher = Application.Current.Dispatcher;
        _service = new SerialPortService();

        ApplySavedSettings();

        _service.DataReceivedBatch += OnDataReceivedBatch;
        _service.ErrorOccurred += OnErrorOccurred;
        _service.PortOpened += OnPortOpened;
        _service.PortClosed += OnPortClosed;
        _wiresharkPipeServer.StatusChanged += (_, _) => _dispatcher.BeginInvoke(() => HciRealtimeStatus = BuildRealtimeStatus());

        OpenCloseCommand = new RelayCommand(_ => TogglePort(), _ => !string.IsNullOrEmpty(Config.PortName));
        ClearCommand = new RelayCommand(_ => ClearData());
        RefreshPortsCommand = new RelayCommand(_ => RefreshPorts());
        RefreshFilterCommand = new RelayCommand(_ => ApplyFilter(), _ => true);
        ClosePortCommand = new RelayCommand(_ => RequestClose?.Invoke(this, EventArgs.Empty), _ => true);
        AddPresetCommand = new RelayCommand(_ => AddPreset(), _ => PresetCommands.Count < MaxPresetCount);
        RemovePresetCommand = new RelayCommand(p => RemovePreset(p as PresetCommandItem), _ => CanRemovePresets);
        SendPresetCommand = new RelayCommand(
            p => SendPreset(p as PresetCommandItem),
            p => _service.IsOpen && p is PresetCommandItem pi && !string.IsNullOrWhiteSpace(pi.Command));
        StartBatchSendCommand = new RelayCommand(_ => StartBatchSend(), _ => _service.IsOpen && !IsBatchSending);
        StopBatchSendCommand = new RelayCommand(_ => StopBatchSend(), _ => IsBatchSending);
        OpenLogInNotepadPlusPlusCommand = new RelayCommand(_ => OpenLogInNotepadPlusPlus());
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());

        Config.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SerialPortConfig.PortName) or nameof(SerialPortConfig.BaudRate))
            {
                PersistSettings();
            }

            if (e.PropertyName == nameof(SerialPortConfig.FilterEnabled))
            {
                RebuildFilterRegex();
                _dispatcher.BeginInvoke(() =>
                {
                    lock (_dataLock)
                    {
                        _filteredData.Clear();
                        FilteredDisplayData.Clear();
                        OnPropertyChanged(nameof(FilteredCount));
                    }
                });
            }
        };

        RebuildFilterRegex();
        LoadPresetCommands();
        PresetCommands.CollectionChanged += OnPresetCommandsChanged;
        ApplyRealtimeOutputState();

        RefreshPorts();
    }

    private void OnPresetCommandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (PresetCommandItem item in e.NewItems)
                item.PropertyChanged += OnPresetItemChanged;
        }
        if (e.OldItems != null)
        {
            foreach (PresetCommandItem item in e.OldItems)
                item.PropertyChanged -= OnPresetItemChanged;
        }
        ReindexPresets();
        OnPropertyChanged(nameof(CanRemovePresets));
        PersistSettings();
    }

    private void OnPresetItemChanged(object? sender, PropertyChangedEventArgs e) => PersistSettings();

    private void LoadPresetCommands()
    {
        var entry = AppSettingsStore.GetPortEntry(_appSettings, _portIndex);
        LoopBatchSend = entry?.LoopBatchSend ?? false;

        PresetCommands.Clear();
        var saved = entry?.PresetCommands;
        if (saved == null || saved.Count == 0)
        {
            for (int i = 1; i <= DefaultPresetCount; i++)
                PresetCommands.Add(new PresetCommandItem { Index = i, DelayMs = 1000 });
            return;
        }

        int idx = 1;
        foreach (var e in saved)
            PresetCommands.Add(PresetCommandItem.FromEntry(e, idx++));
    }

    private void ReindexPresets()
    {
        for (int i = 0; i < PresetCommands.Count; i++)
            PresetCommands[i].Index = i + 1;
    }

    private void AddPreset()
    {
        if (PresetCommands.Count >= MaxPresetCount) return;
        PresetCommands.Add(new PresetCommandItem { Index = PresetCommands.Count + 1, DelayMs = 1000 });
    }

    private void RemovePreset(PresetCommandItem? item)
    {
        if (item == null || PresetCommands.Count <= 1) return;
        PresetCommands.Remove(item);
    }

    public void SendPreset(PresetCommandItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Command)) return;
        bool isHex = item.Format == "HEX";
        if (isHex)
            item.FinalizeHexCommand();
        _service.SendPresetData(item.Command.Trim(), isHex);
    }

    private void StartBatchSend() => _ = StartBatchSendAsync();

    public async Task StartBatchSendAsync()
    {
        if (!_service.IsOpen)
        {
            StatusMessage = "请先打开串口";
            return;
        }

        _batchCts?.Cancel();
        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;
        IsBatchSending = true;

        try
        {
            do
            {
                foreach (var item in PresetCommands)
                {
                    token.ThrowIfCancellationRequested();
                    if (!item.IsEnabled || string.IsNullOrWhiteSpace(item.Command)) continue;

                    await _dispatcher.InvokeAsync(() => SendPreset(item), DispatcherPriority.Normal);
                    int delay = item.DelayMs < 0 ? 100 : item.DelayMs;
                    await Task.Delay(delay, token);
                }
                if (!LoopBatchSend) break;
            } while (true);
        }
        catch (OperationCanceledException)
        {
            // 用户停止
        }
        catch (Exception ex)
        {
            _dispatcher.BeginInvoke(() => StatusMessage = $"批量发送异常: {ex.Message}");
        }
        finally
        {
            IsBatchSending = false;
        }
    }

    public void StopBatchSend()
    {
        _batchCts?.Cancel();
        IsBatchSending = false;
    }

    private void ApplySavedSettings()
    {
        var entry = AppSettingsStore.GetPortEntry(_appSettings, _portIndex);
        if (entry == null) return;
        Config.ApplyFrom(entry);
        _wiresharkPipeEnabled = entry.WiresharkPipeEnabled;
        _ellisysEnabled = entry.EllisysEnabled;
        _ellisysEndpoint = string.IsNullOrWhiteSpace(entry.EllisysEndpoint) ? "127.0.0.1:24352" : entry.EllisysEndpoint;
        _ellisysTransport = entry.EllisysTransport?.Equals("TCP", StringComparison.OrdinalIgnoreCase) == true ? "TCP" : "UDP";
        if (entry.PresetDrawerWidth >= 300)
            _presetDrawerWidth = Math.Clamp(entry.PresetDrawerWidth, 300, 640);
    }

    public void PersistSettings()
    {
        var entry = Config.ToSettingsEntry();
        entry.LoopBatchSend = LoopBatchSend;
        entry.WiresharkPipeEnabled = WiresharkPipeEnabled;
        entry.EllisysEnabled = EllisysEnabled;
        entry.EllisysEndpoint = EllisysEndpoint;
        entry.EllisysTransport = EllisysTransport;
        entry.PresetDrawerWidth = PresetDrawerWidth;
        entry.PresetCommands = PresetCommands.Select(p => p.ToEntry()).ToList();
        AppSettingsStore.SetPortEntry(_appSettings, _portIndex, entry);
        AppSettingsStore.Save(_appSettings);
    }

    private bool IsRealtimeOutputEnabled => WiresharkPipeEnabled || EllisysEnabled;

    private void ApplyRealtimeOutputState()
    {
        if (_service.IsOpen)
        {
            if (WiresharkPipeEnabled)
                _wiresharkPipeServer.Start();
            else
                _wiresharkPipeServer.Stop();

            if (EllisysEnabled)
            {
                _ellisysSender.Close();
                _ellisysSender.Open(EllisysEndpoint, GetEllisysTransport());
            }
            else
            {
                _ellisysSender.Close();
            }
        }
        else
        {
            _wiresharkPipeServer.Stop();
            _ellisysSender.Close();
        }

        HciRealtimeStatus = BuildRealtimeStatus();
    }

    private void StopRealtimeOutputs()
    {
        _wiresharkPipeServer.Stop();
        _ellisysSender.Close();
        HciRealtimeStatus = BuildRealtimeStatus();
    }

    private string BuildRealtimeStatus()
    {
        var parts = new List<string>();
        if (WiresharkPipeEnabled)
            parts.Add(_wiresharkPipeServer.IsConnected ? "Wireshark: connected" : "Wireshark: waiting");
        if (EllisysEnabled)
            parts.Add($"{EllisysTransport}: {_ellisysSender.LastError}");
        return string.Join(" | ", parts);
    }

    private void ForwardRealtimePackets(List<HciPacket> packets)
    {
        if (packets.Count == 0)
            return;

        if (EllisysEnabled && !_ellisysSender.IsOpen)
            _ellisysSender.Open(EllisysEndpoint, GetEllisysTransport());

        foreach (var packet in packets)
        {
            if (WiresharkPipeEnabled)
                _wiresharkPipeServer.EnqueuePacket(packet);
            if (EllisysEnabled)
                _ellisysSender.SendPacket(packet);
        }

        HciRealtimeStatus = BuildRealtimeStatus();
    }

    private SerialPortTool.Services.EllisysTransport GetEllisysTransport() =>
        EllisysTransport == "TCP" ? SerialPortTool.Services.EllisysTransport.Tcp : SerialPortTool.Services.EllisysTransport.Udp;

    private void OnPortOpened(object? sender, EventArgs e)
    {
        StartUptimeTimer();
        try
        {
            _logWriter.Start(Config);
            _btsnoopWriter.Start(Config);
            lock (_hciLock)
                _hciParser.Reset();
            ApplyRealtimeOutputState();
            var logName = _logWriter.LogFilePath != null ? Path.GetFileName(_logWriter.LogFilePath) : "";
            var snoopName = _btsnoopWriter.FilePath != null ? Path.GetFileName(_btsnoopWriter.FilePath) : "";
            StatusMessage = $"串口 {Config.PortName} 已打开，日志: {logName}，HCI: {snoopName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"串口 {Config.PortName} 已打开，日志写入失败: {ex.Message}";
        }
        NotifyLogStateChanged();
        PersistSettings();
    }

    private void OnPortClosed(object? sender, EventArgs e)
    {
        StopUptimeTimer();
        _logWriter.Stop();
        _btsnoopWriter.Stop();
        lock (_hciLock)
            _hciParser.Reset();
        StopRealtimeOutputs();
        StatusMessage = $"串口 {Config.PortName} 已关闭";
        NotifyLogStateChanged();
        PersistSettings();
    }

    private void StartUptimeTimer()
    {
        _portOpenedAt = DateTime.Now;
        UpdatePortRunTime();

        _uptimeTimer?.Stop();
        _uptimeTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _uptimeTimer.Tick += (_, _) => UpdatePortRunTime();
        _uptimeTimer.Start();
    }

    private void StopUptimeTimer()
    {
        _uptimeTimer?.Stop();
        _uptimeTimer = null;
        _portOpenedAt = null;
        PortRunTimeText = "";
    }

    private void UpdatePortRunTime()
    {
        if (_portOpenedAt == null) return;
        PortRunTimeText = $"运行 {FormatElapsed(DateTime.Now - _portOpenedAt.Value)}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalDays >= 1)
            return $"{(int)elapsed.TotalDays}天 {elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        return $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private void NotifyLogStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveLog));
        OnPropertyChanged(nameof(CurrentLogFilePath));
        OnPropertyChanged(nameof(CurrentBtsnoopFilePath));
    }

    private void OpenLogInNotepadPlusPlus()
    {
        if (!string.IsNullOrWhiteSpace(_appSettings.NotepadPlusPlusPath))
            NotepadPlusPlusLauncher.SetUserConfiguredPath(_appSettings.NotepadPlusPlusPath);

        _logWriter.Flush();
        _btsnoopWriter.Flush();

        var logPath = _logWriter.LogFilePath;
        if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
        {
            StatusMessage = "请先打开串口以生成日志文件";
            NotifyLogStateChanged();
            return;
        }

        if (!NotepadPlusPlusLauncher.TryOpenFile(logPath, out var error))
        {
            StatusMessage = error ?? "打开日志失败";
            return;
        }

        StatusMessage = $"已在 Notepad++ 打开: {Path.GetFileName(logPath)}";
    }

    private void OpenLogFolder()
    {
        _logWriter.Flush();

        var logPath = _logWriter.LogFilePath;
        if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
        {
            if (!ExplorerLauncher.TryRevealInExplorer(logPath, out var error))
                StatusMessage = error ?? "打开文件夹失败";
            else
                StatusMessage = $"已打开日志所在文件夹: {Path.GetDirectoryName(logPath)}";
            return;
        }

        var logDir = AppSettingsStore.LogsDirectory;
        if (!Directory.Exists(logDir))
        {
            try { Directory.CreateDirectory(logDir); }
            catch
            {
                StatusMessage = "请先打开串口以生成日志文件";
                return;
            }
        }

        if (!ExplorerLauncher.TryOpenFolder(logDir, out var folderError))
            StatusMessage = folderError ?? "打开文件夹失败";
        else
            StatusMessage = $"已打开日志文件夹: {logDir}";
    }

    private void RebuildFilterRegex()
    {
        if (!Config.FilterEnabled)
        {
            _cachedFilterRegex = null;
            return;
        }
        RegexHelper.TryCreate(Config.FilterRegex, out _cachedFilterRegex);
    }

    public void LoadFilterRegexHistory()
    {
        var history = _appSettings.RegexHistory
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxFilterRegexHistory)
            .ToList();

        if (history.Count == FilterRegexSuggestions.Count &&
            history.Zip(FilterRegexSuggestions, string.Equals).All(match => match))
            return;

        FilterRegexSuggestions.Clear();
        foreach (var item in history)
            FilterRegexSuggestions.Add(item);
    }

    private void AddAppliedFilterRegexToHistory(string pattern)
    {
        pattern = pattern.Trim();
        if (string.IsNullOrEmpty(pattern))
            return;

        _appSettings.RegexHistory.RemoveAll(h => string.Equals(h, pattern, StringComparison.Ordinal));
        _appSettings.RegexHistory.Insert(0, pattern);

        if (_appSettings.RegexHistory.Count > MaxFilterRegexHistory)
            _appSettings.RegexHistory.RemoveRange(MaxFilterRegexHistory, _appSettings.RegexHistory.Count - MaxFilterRegexHistory);

        AppSettingsStore.Save(_appSettings);
    }

    private void OnDataReceivedBatch(object? sender, List<SerialDataModel> batch)
    {
        if (batch.Count == 0) return;

        var toAddAll = new List<SerialDataModel>(batch.Count);
        var toAddFiltered = new List<SerialDataModel>();
        var regex = _cachedFilterRegex;
        bool filterActive = Config.FilterEnabled && regex != null;

        lock (_dataLock)
        {
            foreach (var data in batch)
            {
                _allData.Add(data);
                _totalAllLineCount++;
                toAddAll.Add(data);

                if (data.Direction == "RX")
                    RxCount++;
                else
                    TxCount++;

                if (_allData.Count > Config.MaxBufferLines)
                    _allData.RemoveAt(0);
            }
        }

        if (filterActive)
        {
            foreach (var data in batch)
            {
                if (!RegexHelper.IsMatch(regex, data.DisplayLine)) continue;

                lock (_dataLock)
                {
                    _filteredData.Add(data);
                    _totalFilteredLineCount++;
                    if (_filteredData.Count > Config.MaxBufferLines)
                        _filteredData.RemoveAt(0);
                }
                toAddFiltered.Add(data);
            }
        }

        if (_logWriter.IsActive)
            _logWriter.AppendBatch(batch);

        if (_btsnoopWriter.IsActive || IsRealtimeOutputEnabled)
        {
            var packets = new List<HciPacket>();
            lock (_hciLock)
            {
                foreach (var data in batch)
                {
                    foreach (var packet in _hciParser.ParseLine(data))
                    {
                        packets.Add(packet);
                        if (_btsnoopWriter.IsActive)
                            _btsnoopWriter.AppendPacket(packet);
                    }
                }
                if (_btsnoopWriter.IsActive)
                    _btsnoopWriter.Flush();
            }

            ForwardRealtimePackets(packets);
        }

        _dispatcher.BeginInvoke(() =>
        {
            foreach (var item in toAddAll)
            {
                AllData.Add(item);
                while (AllData.Count > DisplayLineLimit)
                    AllData.RemoveAt(0);
            }

            foreach (var item in toAddFiltered)
            {
                FilteredDisplayData.Add(item);
                while (FilteredDisplayData.Count > DisplayLineLimit)
                    FilteredDisplayData.RemoveAt(0);
            }

            OnPropertyChanged(nameof(AllDataCount));
            OnPropertyChanged(nameof(FilteredCount));

            if (Config.AutoScroll && (toAddAll.Count > 0 || toAddFiltered.Count > 0))
                RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Background);
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        _dispatcher.BeginInvoke(() =>
        {
            StatusMessage = error;
        }, DispatcherPriority.Background);
    }

    public void ApplyFilter()
    {
        if (!RegexHelper.TryCreate(Config.FilterRegex, out var regex))
        {
            Config.FilterEnabled = false;
            _cachedFilterRegex = null;
            AppliedFilterDisplayText = "";
            var error = string.IsNullOrWhiteSpace(Config.FilterRegex)
                ? "请输入有效的正则表达式"
                : "正则表达式无效";
            StatusMessage = error;
            return;
        }

        _cachedFilterRegex = regex;
        Config.FilterEnabled = true;
        AddAppliedFilterRegexToHistory(Config.FilterRegex);
        AppliedFilterDisplayText = $"已启用的正则表示式：{Config.FilterRegex}";
        StatusMessage = AppliedFilterDisplayText;

        Task.Run(() =>
        {
            List<SerialDataModel> snapshot;
            lock (_dataLock)
                snapshot = _allData.ToList();

            var matched = new List<SerialDataModel>();
            foreach (var data in snapshot)
            {
                if (RegexHelper.IsMatch(_cachedFilterRegex, data.DisplayLine))
                    matched.Add(data);
            }

            _dispatcher.BeginInvoke(() =>
            {
                lock (_dataLock)
                {
                    _filteredData.Clear();
                    _filteredData.AddRange(matched);
                    _totalFilteredLineCount = matched.Count;
                    while (_filteredData.Count > Config.MaxBufferLines)
                        _filteredData.RemoveAt(0);
                }

                FilteredDisplayData.Clear();
                int start = Math.Max(0, matched.Count - DisplayLineLimit);
                for (int i = start; i < matched.Count; i++)
                    FilteredDisplayData.Add(matched[i]);

                OnPropertyChanged(nameof(FilteredCount));
                if (Config.AutoScroll && FilteredDisplayData.Count > 0)
                    RequestScrollToEnd?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Background);
        });
    }

    public bool TogglePort()
    {
        if (_service.IsOpen)
        {
            _service.Close();
            return true;
        }
        else
        {
            if (string.IsNullOrEmpty(Config.PortName))
            {
                StatusMessage = "请先选择串口";
                return false;
            }
            return _service.Open();
        }
    }

    public bool OpenPort()
    {
        if (_service.IsOpen) return true;
        if (string.IsNullOrEmpty(Config.PortName))
        {
            StatusMessage = "请先选择串口";
            return false;
        }
        return _service.Open();
    }

    public void ClosePort()
    {
        _service.Close();
    }

    public void ClearData()
    {
        lock (_dataLock)
        {
            _allData.Clear();
            _filteredData.Clear();
            _totalAllLineCount = 0;
            _totalFilteredLineCount = 0;
            AllData.Clear();
            FilteredDisplayData.Clear();
            RxCount = 0;
            TxCount = 0;
        }
        OnPropertyChanged(nameof(AllDataCount));
        OnPropertyChanged(nameof(FilteredCount));
        StatusMessage = "数据已清空";
    }

    public void RefreshPorts()
    {
        try
        {
            var ports = SerialPort.GetPortNames();
            AvailablePorts.Clear();
            foreach (var port in ports.OrderBy(p => p))
            {
                AvailablePorts.Add(port);
            }

            if (!string.IsNullOrEmpty(Config.PortName) && !AvailablePorts.Contains(Config.PortName))
                AvailablePorts.Insert(0, Config.PortName);

            if (AvailablePorts.Count > 0 && string.IsNullOrEmpty(Config.PortName))
                Config.PortName = AvailablePorts[0];

            StatusMessage = $"发现 {AvailablePorts.Count} 个串口";
        }
        catch (Exception ex)
        {
            StatusMessage = $"刷新串口失败: {ex.Message}";
        }
    }

    public void Dispose()
    {
        StopUptimeTimer();
        StopBatchSend();
        _logWriter.Dispose();
        _btsnoopWriter.Dispose();
        StopRealtimeOutputs();
        _wiresharkPipeServer.Dispose();
        _ellisysSender.Dispose();
        PersistSettings();
        _service.Dispose();
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }
}
