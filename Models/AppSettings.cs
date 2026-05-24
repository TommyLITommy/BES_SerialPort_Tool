using System.Collections.Generic;

namespace SerialPortTool.Models;

public class AppSettings
{
    public List<PortSettingsEntry> Ports { get; set; } = new();
    /// <summary>可选：Notepad++ 可执行文件完整路径</summary>
    public string NotepadPlusPlusPath { get; set; } = "";
}

public class PortSettingsEntry
{
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public bool LoopBatchSend { get; set; }
    public bool WiresharkPipeEnabled { get; set; }
    public bool EllisysEnabled { get; set; }
    public string EllisysEndpoint { get; set; } = "127.0.0.1:24352";
    public string EllisysTransport { get; set; } = "UDP";
    public double PresetDrawerWidth { get; set; } = 400;
    public List<PresetCommandEntry> PresetCommands { get; set; } = new();
}
