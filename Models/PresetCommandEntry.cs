namespace SerialPortTool.Models;

public class PresetCommandEntry
{
    public string Format { get; set; } = "String";
    public string Description { get; set; } = "";
    public string Command { get; set; } = "";
    public int DelayMs { get; set; } = 1000;
    public bool IsEnabled { get; set; }
}
