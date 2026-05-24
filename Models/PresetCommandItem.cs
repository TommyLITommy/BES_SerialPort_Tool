using System;
using System.ComponentModel;
using SerialPortTool.Helpers;

namespace SerialPortTool.Models;

public class PresetCommandItem : INotifyPropertyChanged
{
    private string _format = "String";
    private string _description = "";
    private string _command = "";
    private int _delayMs = 1000;
    private bool _isEnabled;
    private int _index;
    private bool _suppressHexFormat;

    public static string[] FormatOptions { get; } = { "String", "HEX" };

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(nameof(Index)); OnPropertyChanged(nameof(DisplayIndex)); }
    }

    public string DisplayIndex => $"#{Index}";

    public string Format
    {
        get => _format;
        set
        {
            if (_format == value) return;
            _format = value;
            if (IsHexFormat(value) && !string.IsNullOrWhiteSpace(_command))
                ApplyHexFormat(finalize: true);
            OnPropertyChanged(nameof(Format));
        }
    }

    private static bool IsHexFormat(string? format) =>
        string.Equals(format, "HEX", StringComparison.OrdinalIgnoreCase);

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(nameof(Description)); }
    }

    public string Command
    {
        get => _command;
        set
        {
            string v = value ?? "";
            if (IsHexFormat(_format) && !_suppressHexFormat)
            {
                string formatted = HexInputHelper.FormatForInput(v);
                if (formatted != _command)
                {
                    _suppressHexFormat = true;
                    _command = formatted;
                    _suppressHexFormat = false;
                    OnPropertyChanged(nameof(Command));
                    return;
                }
            }
            if (_command == v) return;
            _command = v;
            OnPropertyChanged(nameof(Command));
        }
    }

    /// <summary>命令框失焦时调用，HEX 格式下补齐并加空格</summary>
    public void FinalizeHexCommand()
    {
        if (!IsHexFormat(_format)) return;
        ApplyHexFormat(finalize: true);
    }

    private void ApplyHexFormat(bool finalize)
    {
        if (!IsHexFormat(_format)) return;
        string formatted = finalize
            ? HexInputHelper.FormatFinal(_command)
            : HexInputHelper.FormatForInput(_command);
        if (formatted == _command) return;
        _suppressHexFormat = true;
        _command = formatted;
        _suppressHexFormat = false;
        OnPropertyChanged(nameof(Command));
    }

    public int DelayMs
    {
        get => _delayMs;
        set { _delayMs = value; OnPropertyChanged(nameof(DelayMs)); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static PresetCommandItem FromEntry(PresetCommandEntry entry, int index) => new()
    {
        Index = index,
        Format = entry.Format is "HEX" ? "HEX" : "String",
        Description = entry.Description ?? "",
        Command = entry.Format is "HEX"
            ? HexInputHelper.FormatFinal(entry.Command ?? "")
            : entry.Command ?? "",
        DelayMs = entry.DelayMs > 0 ? entry.DelayMs : 1000,
        IsEnabled = entry.IsEnabled
    };

    public PresetCommandEntry ToEntry() => new()
    {
        Format = Format,
        Description = Description,
        Command = Command,
        DelayMs = DelayMs,
        IsEnabled = IsEnabled
    };
}
