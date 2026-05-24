using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SerialPortTool.Helpers;
using SerialPortTool.Models;
using SerialPortTool.Services;

namespace SerialPortTool.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _appSettings = AppSettingsStore.Load();

    public ObservableCollection<SerialPortViewModel> Ports { get; } = new();

    private string _globalStatus = "就绪";
    public string GlobalStatus
    {
        get => _globalStatus;
        set { _globalStatus = value; OnPropertyChanged(nameof(GlobalStatus)); }
    }

    public bool CanAddPort => Ports.Count < 2;
    public bool CanRemovePort => Ports.Count > 1;

    public ICommand OpenAllCommand { get; }
    public ICommand CloseAllCommand { get; }
    public ICommand RefreshAllCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand AddPortCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        if (!string.IsNullOrWhiteSpace(_appSettings.NotepadPlusPlusPath))
            NotepadPlusPlusLauncher.SetUserConfiguredPath(_appSettings.NotepadPlusPlusPath);

        // 默认添加一个串口
        AddPortInternal();

        OpenAllCommand = new RelayCommand(_ => OpenAllPorts(), _ => true);
        CloseAllCommand = new RelayCommand(_ => CloseAllPorts(), _ => true);
        RefreshAllCommand = new RelayCommand(_ => RefreshAllPorts(), _ => true);
        ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
        AddPortCommand = new RelayCommand(_ => AddPort(), _ => CanAddPort);
    }

    private void AddPortInternal()
    {
        var port = new SerialPortViewModel(Ports.Count, _appSettings);
        port.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SerialPortViewModel.StatusMessage))
                UpdateGlobalStatus();
        };
        Ports.Add(port);
        port.RefreshPorts();
        UpdateGlobalStatus();
        OnPropertyChanged(nameof(CanAddPort));
        OnPropertyChanged(nameof(CanRemovePort));
    }

    public void AddPort()
    {
        if (Ports.Count >= 2) return;
        AddPortInternal();
    }

    public void RemovePort(SerialPortViewModel port)
    {
        if (Ports.Count <= 1) return; // 至少保留一个
        port.ClosePort();
        port.Dispose();
        Ports.Remove(port);
        UpdateGlobalStatus();
        OnPropertyChanged(nameof(CanAddPort));
        OnPropertyChanged(nameof(CanRemovePort));
    }

    private void UpdateGlobalStatus()
    {
        var statuses = Ports.Select(p => $"[{p.Config.PortName ?? "未选择"}] {(p.Service.IsOpen ? "已打开" : "已关闭")}");
        GlobalStatus = string.Join(" | ", statuses);
    }

    public void OpenAllPorts()
    {
        int opened = 0;
        foreach (var port in Ports)
        {
            if (port.OpenPort()) opened++;
        }
        GlobalStatus = $"成功打开 {opened} 个串口";
    }

    public void CloseAllPorts()
    {
        foreach (var port in Ports)
        {
            port.ClosePort();
        }
        GlobalStatus = "所有串口已关闭";
    }

    public void RefreshAllPorts()
    {
        foreach (var port in Ports)
        {
            port.RefreshPorts();
        }
        GlobalStatus = "串口列表已刷新";
    }

    public void Dispose()
    {
        foreach (var port in Ports)
        {
            port.PersistSettings();
            port.Dispose();
        }
        AppSettingsStore.Save(_appSettings);
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
