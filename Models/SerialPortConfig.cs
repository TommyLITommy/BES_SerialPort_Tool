using System.ComponentModel;

namespace SerialPortTool.Models;

public class SerialPortConfig : INotifyPropertyChanged
{
    private string _portName = "";
    private int _baudRate = 115200;
    private int _dataBits = 8;
    private string _parity = "None";
    private string _stopBits = "One";
    private string _flowControl = "None";
    private bool _isOpen = false;
    private bool _isHexMode = false;
    private bool _showTimestamp = true;
    private string _filterRegex = "";
    private bool _filterEnabled = false;
    private bool _autoScroll = true;
    private int _maxBufferLines = 10000;

    public string PortName
    {
        get => _portName;
        set { _portName = value; OnPropertyChanged(nameof(PortName)); }
    }

    public int BaudRate
    {
        get => _baudRate;
        set { _baudRate = value; OnPropertyChanged(nameof(BaudRate)); }
    }

    public int DataBits
    {
        get => _dataBits;
        set { _dataBits = value; OnPropertyChanged(nameof(DataBits)); }
    }

    public string Parity
    {
        get => _parity;
        set { _parity = value; OnPropertyChanged(nameof(Parity)); }
    }

    public string StopBits
    {
        get => _stopBits;
        set { _stopBits = value; OnPropertyChanged(nameof(StopBits)); }
    }

    public string FlowControl
    {
        get => _flowControl;
        set { _flowControl = value; OnPropertyChanged(nameof(FlowControl)); }
    }

    public bool IsOpen
    {
        get => _isOpen;
        set { _isOpen = value; OnPropertyChanged(nameof(IsOpen)); }
    }

    public bool IsHexMode
    {
        get => _isHexMode;
        set { _isHexMode = value; OnPropertyChanged(nameof(IsHexMode)); }
    }

    public bool ShowTimestamp
    {
        get => _showTimestamp;
        set { _showTimestamp = value; OnPropertyChanged(nameof(ShowTimestamp)); }
    }

    public string FilterRegex
    {
        get => _filterRegex;
        set { _filterRegex = value; OnPropertyChanged(nameof(FilterRegex)); }
    }

    public bool FilterEnabled
    {
        get => _filterEnabled;
        set { _filterEnabled = value; OnPropertyChanged(nameof(FilterEnabled)); }
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set { _autoScroll = value; OnPropertyChanged(nameof(AutoScroll)); }
    }

    public int MaxBufferLines
    {
        get => _maxBufferLines;
        set { _maxBufferLines = value; OnPropertyChanged(nameof(MaxBufferLines)); }
    }

    public static int[] BaudRateList { get; } = new[] { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600, 1000000, 1152000, 1500000, 2000000, 3000000 };
    public static int[] DataBitsList { get; } = new[] { 5, 6, 7, 8 };
    public static string[] ParityList { get; } = new[] { "None", "Odd", "Even", "Mark", "Space" };
    public static string[] StopBitsList { get; } = new[] { "One", "OnePointFive", "Two" };
    public static string[] FlowControlList { get; } = new[] { "None", "XOnXOff", "RequestToSend", "RequestToSendXOnXOff" };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void ApplyFrom(PortSettingsEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.PortName))
            PortName = entry.PortName;
        if (entry.BaudRate > 0)
            BaudRate = entry.BaudRate;
    }

    public PortSettingsEntry ToSettingsEntry() => new()
    {
        PortName = PortName,
        BaudRate = BaudRate
    };
}