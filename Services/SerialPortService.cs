using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SerialPortTool.Helpers;
using SerialPortTool.Models;

namespace SerialPortTool.Services;

public class SerialPortService : IDisposable
{
    private SerialPort? _serialPort;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private readonly StringBuilder _lineBuffer = new();
    private readonly object _lockObj = new();
    private readonly List<SerialDataModel> _batchBuffer = new();
    private Timer? _flushTimer;

    public event EventHandler<List<SerialDataModel>>? DataReceivedBatch;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? PortOpened;
    public event EventHandler? PortClosed;

    public SerialPortConfig Config { get; } = new();
    public bool IsOpen => _serialPort?.IsOpen ?? false;
    public string PortName => Config.PortName;

    public bool Open()
    {
        try
        {
            if (_serialPort?.IsOpen == true)
                return true;

            Close();

            _serialPort = new SerialPort(Config.PortName, Config.BaudRate)
            {
                DataBits = Config.DataBits,
                Parity = (Parity)Enum.Parse(typeof(Parity), Config.Parity),
                StopBits = (StopBits)Enum.Parse(typeof(StopBits), Config.StopBits),
                Handshake = (Handshake)Enum.Parse(typeof(Handshake), Config.FlowControl),
                ReadBufferSize = 262144,
                WriteBufferSize = 262144,
                ReadTimeout = 50,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };

            _serialPort.Open();
            Config.IsOpen = true;

            _cts = new CancellationTokenSource();
            _readTask = Task.Run(ReadLoop, _cts.Token);

            // 启动定时刷新器，每100ms批量刷新一次UI（降低频率减少UI压力）
            _flushTimer = new Timer(FlushBatch, null, 100, 100);

            PortOpened?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"打开串口 {Config.PortName} 失败: {ex.Message}");
            return false;
        }
    }

    public void Close()
    {
        try
        {
            _flushTimer?.Dispose();
            _flushTimer = null;

            _cts?.Cancel();
            if (_readTask != null)
            {
                try { _readTask.Wait(TimeSpan.FromSeconds(2)); }
                catch { }
            }

            _serialPort?.Close();
            _serialPort?.Dispose();
            _serialPort = null;
            Config.IsOpen = false;
            PortClosed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"关闭串口异常: {ex.Message}");
        }
    }

    private void ReadLoop()
    {
        if (_serialPort == null || _cts == null) return;

        var token = _cts.Token;
        var buffer = new byte[8192]; // 减小缓冲区，更频繁地处理

        while (!token.IsCancellationRequested && _serialPort.IsOpen)
        {
            try
            {
                int bytesRead;
                try
                {
                    bytesRead = _serialPort.Read(buffer, 0, buffer.Length);
                }
                catch (TimeoutException)
                {
                    bytesRead = 0;
                }

                if (bytesRead > 0)
                {
                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);
                    ProcessReceivedData(data);
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke(this, $"读取异常: {ex.Message}");
                }
                break;
            }
        }
    }

    private void ProcessReceivedData(byte[] data)
    {
        bool flushNow = false;

        lock (_lockObj)
        {
            if (Config.IsHexMode)
            {
                var model = new SerialDataModel
                {
                    Timestamp = DateTime.Now,
                    Direction = "RX",
                    RawData = data,
                    DisplayText = BitConverter.ToString(data).Replace("-", " "),
                    ShowTimestamp = Config.ShowTimestamp
                };
                _batchBuffer.Add(model);
            }
            else
            {
                string text = Encoding.UTF8.GetString(data);
                _lineBuffer.Append(text);

                int newlineIndex;
                while ((newlineIndex = _lineBuffer.ToString().IndexOf('\n')) >= 0)
                {
                    string line = _lineBuffer.ToString(0, newlineIndex);
                    if (line.EndsWith('\r'))
                        line = line[..^1];

                    _lineBuffer.Remove(0, newlineIndex + 1);

                    var model = new SerialDataModel
                    {
                        Timestamp = DateTime.Now,
                        Direction = "RX",
                        RawData = Encoding.UTF8.GetBytes(line + "\n"),
                        DisplayText = line,
                        ShowTimestamp = Config.ShowTimestamp
                    };
                    _batchBuffer.Add(model);
                }
            }

            // 限制批处理缓冲区大小，防止内存无限增长
            if (_batchBuffer.Count > 5000)
            {
                // 紧急刷新，避免内存溢出
                flushNow = true;
            }
        }

        if (flushNow)
        {
            FlushBatch(null);
        }
    }

    private void FlushBatch(object? state)
    {
        List<SerialDataModel> batch;
        lock (_lockObj)
        {
            if (_batchBuffer.Count == 0) return;
            batch = new List<SerialDataModel>(_batchBuffer);
            _batchBuffer.Clear();
        }

        // 在UI线程上批量通知
        DataReceivedBatch?.Invoke(this, batch);
    }

    public bool SendData(string text)
    {
        if (_serialPort?.IsOpen != true) return false;

        try
        {
            byte[] data;
            if (Config.IsHexMode)
            {
                data = HexStringToBytes(text);
            }
            else
            {
                data = Encoding.UTF8.GetBytes(text);
            }

            _serialPort.Write(data, 0, data.Length);

            var model = new SerialDataModel
            {
                Timestamp = DateTime.Now,
                Direction = "TX",
                RawData = data,
                DisplayText = Config.IsHexMode ? BitConverter.ToString(data).Replace("-", " ") : text,
                ShowTimestamp = Config.ShowTimestamp
            };

            DataReceivedBatch?.Invoke(this, new List<SerialDataModel> { model });

            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"发送失败: {ex.Message}");
            return false;
        }
    }

    public bool SendPresetData(string content, bool isHex)
    {
        if (_serialPort?.IsOpen != true) return false;

        try
        {
            byte[] data = isHex ? HexStringToBytes(content) : Encoding.UTF8.GetBytes(content);
            _serialPort.Write(data, 0, data.Length);

            var model = new SerialDataModel
            {
                Timestamp = DateTime.Now,
                Direction = "TX",
                RawData = data,
                DisplayText = isHex ? BitConverter.ToString(data).Replace("-", " ") : content,
                ShowTimestamp = Config.ShowTimestamp
            };

            DataReceivedBatch?.Invoke(this, new List<SerialDataModel> { model });
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"发送失败: {ex.Message}");
            return false;
        }
    }

    public bool SendData(byte[] data)
    {
        if (_serialPort?.IsOpen != true) return false;

        try
        {
            _serialPort.Write(data, 0, data.Length);

            var model = new SerialDataModel
            {
                Timestamp = DateTime.Now,
                Direction = "TX",
                RawData = data,
                DisplayText = Config.IsHexMode ? BitConverter.ToString(data).Replace("-", " ") : Encoding.UTF8.GetString(data),
                ShowTimestamp = Config.ShowTimestamp
            };

            DataReceivedBatch?.Invoke(this, new List<SerialDataModel> { model });

            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"发送失败: {ex.Message}");
            return false;
        }
    }

    private static byte[] HexStringToBytes(string hex)
    {
        hex = HexInputHelper.ExtractHexDigits(hex);
        if (hex.Length == 0)
            throw new ArgumentException("HEX 内容为空");
        if (hex.Length % 2 != 0) hex = "0" + hex;

        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }

    public void Dispose()
    {
        Close();
        _cts?.Dispose();
        _flushTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
