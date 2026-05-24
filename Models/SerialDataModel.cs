using System;

namespace SerialPortTool.Models;

public class SerialDataModel
{
    public DateTime Timestamp { get; set; }
    public string Direction { get; set; } = "RX"; // RX or TX
    public byte[] RawData { get; set; } = Array.Empty<byte>();
    public string DisplayText { get; set; } = "";
    public bool IsFiltered { get; set; } = false;

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");

    public string HexString
    {
        get
        {
            if (RawData.Length == 0) return "";
            return BitConverter.ToString(RawData).Replace("-", " ");
        }
    }

    public string DisplayLine
    {
        get
        {
            if (ShowTimestamp)
                return $"[{FormattedTimestamp}] {DisplayText}";
            return DisplayText;
        }
    }

    public bool ShowTimestamp { get; set; } = true;
}