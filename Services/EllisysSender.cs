using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace SerialPortTool.Services;

public enum EllisysTransport
{
    Udp,
    Tcp
}

public sealed class EllisysSender : IDisposable
{
    private const byte InjectedHciPacketTypeCommand = 0x01;
    private const byte InjectedHciPacketTypeAclFromHost = 0x02;
    private const byte InjectedHciPacketTypeScoFromHost = 0x03;
    private const byte InjectedHciPacketTypeEvent = 0x84;

    private Socket? _udpSocket;
    private IPEndPoint? _udpEndPoint;
    private TcpClient? _tcpClient;
    private NetworkStream? _tcpStream;
    private readonly object _sendLock = new();

    public bool IsOpen { get; private set; }
    public string LastError { get; private set; } = "";

    public bool Open(string endpoint, EllisysTransport transport)
    {
        try
        {
            Close();
            var remote = ParseEndpoint(endpoint);

            if (transport == EllisysTransport.Udp)
            {
                _udpEndPoint = remote;
                _udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            }
            else
            {
                _tcpClient = new TcpClient();
                _tcpClient.Connect(remote.Address, remote.Port);
                _tcpStream = _tcpClient.GetStream();
            }

            IsOpen = true;
            LastError = $"{transport} 已就绪";
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Ellisys 打开失败: {ex.Message}";
            Close();
            return false;
        }
    }

    public void Close()
    {
        IsOpen = false;
        try { _tcpStream?.Dispose(); } catch { }
        try { _tcpClient?.Close(); } catch { }
        try { _udpSocket?.Close(); } catch { }
        try { _udpSocket?.Dispose(); } catch { }
        _tcpStream = null;
        _tcpClient = null;
        _udpSocket = null;
        _udpEndPoint = null;
    }

    public void SendPacket(HciPacket packet)
    {
        if (!IsOpen || packet.Data.Length < 1)
            return;

        try
        {
            var ellisysPacket = GenerateEllisysPacket(packet);
            lock (_sendLock)
            {
                if (_udpSocket != null && _udpEndPoint != null)
                {
                    _udpSocket.SendTo(ellisysPacket, _udpEndPoint);
                    return;
                }

                if (_tcpStream != null)
                {
                    _tcpStream.Write(ellisysPacket, 0, ellisysPacket.Length);
                    _tcpStream.Flush();
                }
            }
        }
        catch (Exception ex)
        {
            LastError = $"Ellisys 发送失败: {ex.Message}";
        }
    }

    private static byte[] GenerateEllisysPacket(HciPacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((ushort)0x0002);
        writer.Write((byte)0x01);

        writer.Write((byte)0x02);
        writer.Write((ushort)packet.Timestamp.Year);
        writer.Write((byte)packet.Timestamp.Month);
        writer.Write((byte)packet.Timestamp.Day);

        var ns = (long)(packet.Timestamp.TimeOfDay.TotalMilliseconds * 1_000_000);
        var nsBytes = BitConverter.GetBytes(ns);
        ms.Write(nsBytes, 0, 6);

        writer.Write((byte)0x80);
        writer.Write((uint)12000000);

        writer.Write((byte)0x81);
        writer.Write(ToEllisysPacketType(packet));
        writer.Write((byte)0x82);
        ms.Write(packet.Data, 1, packet.Data.Length - 1);

        return ms.ToArray();
    }

    private static byte ToEllisysPacketType(HciPacket packet)
    {
        return packet.Data[0] switch
        {
            0x01 => InjectedHciPacketTypeCommand,
            0x02 => InjectedHciPacketTypeAclFromHost,
            0x03 => InjectedHciPacketTypeScoFromHost,
            0x04 => InjectedHciPacketTypeEvent,
            _ => throw new InvalidOperationException($"不支持的 HCI 类型: 0x{packet.Data[0]:X2}")
        };
    }

    private static IPEndPoint ParseEndpoint(string endpoint)
    {
        var parts = endpoint.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new FormatException("地址格式应为 IP:Port");

        return new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
    }

    public void Dispose() => Close();
}
