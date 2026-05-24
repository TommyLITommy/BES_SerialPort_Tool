using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using SerialPortTool.Models;

namespace SerialPortTool.Services;

public sealed class BtsnoopWriter : IDisposable
{
    private const uint Version = 1;
    private const uint DatalinkTypeHciUart = 1002;
    private const long BtsnoopUnixEpochOffsetMicroseconds = 0x00dcddb30f2f8000L;

    private readonly object _fileLock = new();
    private FileStream? _stream;
    private string? _filePath;
    private string? _safePortName;
    private string? _sessionTimestamp;

    public string? FilePath => _filePath;
    public bool IsActive => _stream != null;

    public void Start(SerialPortConfig config)
    {
        Stop();

        Directory.CreateDirectory(AppSettingsStore.LogsDirectory);
        _safePortName = SanitizeFileName(config.PortName);
        _sessionTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _filePath = Path.Combine(AppSettingsStore.LogsDirectory, $"{_safePortName}_{_sessionTimestamp}.cfa");

        _stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        WriteHeader();
        _stream.Flush();
    }

    public void AppendPacket(HciPacket packet)
    {
        if (packet.Data.Length == 0)
            return;

        lock (_fileLock)
        {
            if (_stream == null)
                return;

            Span<byte> recordHeader = stackalloc byte[24];
            BinaryPrimitives.WriteUInt32BigEndian(recordHeader[0..4], (uint)packet.Data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(recordHeader[4..8], (uint)packet.Data.Length);
            BinaryPrimitives.WriteUInt32BigEndian(recordHeader[8..12], GetPacketFlags(packet));
            BinaryPrimitives.WriteUInt32BigEndian(recordHeader[12..16], 0);
            BinaryPrimitives.WriteUInt64BigEndian(recordHeader[16..24], ToBtsnoopTimestamp(packet.Timestamp));

            _stream.Write(recordHeader);
            _stream.Write(packet.Data, 0, packet.Data.Length);
        }
    }

    public void Flush()
    {
        lock (_fileLock)
        {
            try { _stream?.Flush(); } catch { }
        }
    }

    public void Stop()
    {
        lock (_fileLock)
        {
            try { _stream?.Flush(); } catch { }
            try { _stream?.Dispose(); } catch { }
            _stream = null;
            _safePortName = null;
            _sessionTimestamp = null;
        }
    }

    private void WriteHeader()
    {
        if (_stream == null)
            return;

        Span<byte> header = stackalloc byte[16];
        Encoding.ASCII.GetBytes("btsnoop\0", header[0..8]);
        BinaryPrimitives.WriteUInt32BigEndian(header[8..12], Version);
        BinaryPrimitives.WriteUInt32BigEndian(header[12..16], DatalinkTypeHciUart);
        _stream.Write(header);
    }

    private static uint GetPacketFlags(HciPacket packet)
    {
        return packet.Data[0] switch
        {
            0x01 => 2u,
            0x04 => 3u,
            0x02 => packet.Direction == HciPacketDirection.Rx ? 1u : 0u,
            0x03 => packet.Direction == HciPacketDirection.Rx ? 1u : 0u,
            0x05 => packet.Direction == HciPacketDirection.Rx ? 1u : 0u,
            _ => packet.Direction == HciPacketDirection.Rx ? 1u : 0u
        };
    }

    private static ulong ToBtsnoopTimestamp(DateTime timestamp)
    {
        var unixMicroseconds = (ulong)((timestamp.Ticks - DateTime.UnixEpoch.Ticks) / 10);
        return unixMicroseconds + (ulong)BtsnoopUnixEpochOffsetMicroseconds;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Unknown";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }

    public void Dispose() => Stop();
}
