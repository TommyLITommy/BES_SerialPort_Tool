using System;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace SerialPortTool.Services;

public sealed class WiresharkPipeServer : IDisposable
{
    public const string PipeName = "BluetoothHciToWireshark";

    private readonly ConcurrentQueue<byte[]> _queue = new();
    private readonly SemaphoreSlim _dataSemaphore = new(0);
    private NamedPipeServerStream? _pipe;
    private Thread? _serverThread;
    private Thread? _sendThread;
    private volatile bool _running;
    private volatile bool _clientConnected;
    private bool _headerSent;

    public bool IsRunning => _running;
    public bool IsConnected => _clientConnected;
    public string LastError { get; private set; } = "";
    public int QueueCount => _queue.Count;
    public event EventHandler<string>? StatusChanged;

    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _serverThread = new Thread(ServerLoop)
        {
            IsBackground = true,
            Name = "WiresharkPipeServer"
        };
        _serverThread.Start();
    }

    public void Stop()
    {
        _running = false;
        _clientConnected = false;
        _headerSent = false;
        try { _dataSemaphore.Release(10); } catch { }
        ClosePipe();
    }

    public void EnqueuePacket(HciPacket packet)
    {
        if (!_running || packet.Data.Length == 0)
            return;

        var data = PcapngHelper.CreatePacket(packet);
        if (data.Length == 0)
            return;

        while (_queue.Count > 500)
            _queue.TryDequeue(out _);

        _queue.Enqueue(data);
        _dataSemaphore.Release();
    }

    private void ServerLoop()
    {
        while (_running)
        {
            try
            {
                _pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough);

                SetStatus("等待 Wireshark 连接");
                _clientConnected = false;
                _headerSent = false;

                var connectTask = _pipe.WaitForConnectionAsync();
                var timeoutTask = Task.Delay(2000);
                Task.WhenAny(connectTask, timeoutTask).Wait();

                if (!connectTask.IsCompletedSuccessfully)
                {
                    ClosePipe();
                    Thread.Sleep(300);
                    continue;
                }

                _clientConnected = true;
                SetStatus("Wireshark 已连接");
                _sendThread = new Thread(SendLoop)
                {
                    IsBackground = true,
                    Name = "WiresharkPipeSender"
                };
                _sendThread.Start();

                while (_running && _pipe?.IsConnected == true)
                    Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                SetStatus($"Wireshark pipe 错误: {ex.Message}");
            }
            finally
            {
                _clientConnected = false;
                _headerSent = false;
                _sendThread?.Join(500);
                ClosePipe();
            }
        }
    }

    private async void SendLoop()
    {
        try
        {
            while (_running && _pipe is { IsConnected: true })
            {
                if (!_dataSemaphore.Wait(10))
                    continue;

                if (!_headerSent)
                {
                    var header = PcapngHelper.CreateGlobalHeader();
                    await _pipe.WriteAsync(header, 0, header.Length);
                    _headerSent = true;
                }

                while (_queue.TryDequeue(out var packet))
                {
                    if (_pipe is not { IsConnected: true })
                        return;

                    await _pipe.WriteAsync(packet, 0, packet.Length);
                }
            }
        }
        catch (Exception ex)
        {
            _clientConnected = false;
            SetStatus($"Wireshark pipe 断开: {ex.Message}");
        }
    }

    private void SetStatus(string status)
    {
        LastError = status;
        StatusChanged?.Invoke(this, status);
    }

    private void ClosePipe()
    {
        try { _pipe?.Close(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
    }

    public void Dispose()
    {
        Stop();
        _dataSemaphore.Dispose();
    }
}
