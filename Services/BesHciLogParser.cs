using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SerialPortTool.Models;

namespace SerialPortTool.Services;

public enum HciPacketDirection
{
    Tx,
    Rx
}

public sealed class HciPacket
{
    public HciPacket(DateTime timestamp, HciPacketDirection direction, byte[] data)
    {
        Timestamp = timestamp;
        Direction = direction;
        Data = data;
    }

    public DateTime Timestamp { get; }
    public HciPacketDirection Direction { get; }
    public byte[] Data { get; }
}

public sealed class BesHciLogParser
{
    private static readonly Regex DirectionRegex = new(@"\[(TX|RX)\]\s*:?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeadingTimestampRegex = new(@"^\s*(?:\[(?<time>\d{2}:\d{2}:\d{2}\.\d{3})\]\s*)+", RegexOptions.Compiled);
    private static readonly Regex HexLineRegex = new(@"^(?:[0-9A-Fa-f]{2}\s*)+$", RegexOptions.Compiled);

    private readonly List<byte> _packetBuffer = new();
    private HciPacketDirection? _pendingDirection;
    private DateTime _pendingTimestamp;

    public void Reset()
    {
        _packetBuffer.Clear();
        _pendingDirection = null;
        _pendingTimestamp = default;
    }

    public List<HciPacket> ParseLine(SerialDataModel data)
    {
        var packets = new List<HciPacket>();
        if (data == null || string.IsNullOrWhiteSpace(data.DisplayText))
            return packets;

        var line = data.DisplayText;
        var directionMatch = DirectionRegex.Match(line);
        if (directionMatch.Success)
        {
            _pendingDirection = directionMatch.Groups[1].Value.Equals("RX", StringComparison.OrdinalIgnoreCase)
                ? HciPacketDirection.Rx
                : HciPacketDirection.Tx;
            _pendingTimestamp = ExtractLogTimestamp(line, data.Timestamp);
            _packetBuffer.Clear();

            var trailing = line[(directionMatch.Index + directionMatch.Length)..];
            if (!string.IsNullOrWhiteSpace(trailing))
                AppendHexLine(trailing, data.Timestamp, packets);

            return packets;
        }

        if (_pendingDirection == null)
            return packets;

        AppendHexLine(line, data.Timestamp, packets);
        return packets;
    }

    private void AppendHexLine(string line, DateTime timestamp, List<HciPacket> packets)
    {
        var logTimestamp = ExtractLogTimestamp(line, timestamp);
        var hexText = LeadingTimestampRegex.Replace(line, "").Trim();
        if (hexText.Length == 0)
            return;

        if (!HexLineRegex.IsMatch(hexText))
        {
            Reset();
            return;
        }

        var bytes = ParseHexBytes(hexText);
        if (_packetBuffer.Count == 0)
            _pendingTimestamp = logTimestamp;

        _packetBuffer.AddRange(bytes);
        if (_pendingTimestamp == default)
            _pendingTimestamp = logTimestamp;

        TryEmitPackets(packets);
    }

    private void TryEmitPackets(List<HciPacket> packets)
    {
        while (_pendingDirection != null && _packetBuffer.Count > 0)
        {
            var expectedLength = GetHciH4PacketLength(_packetBuffer);
            if (expectedLength < 0)
            {
                Reset();
                return;
            }

            if (expectedLength == 0)
            {
                if (_packetBuffer.Count > 4096)
                    Reset();
                return;
            }

            if (_packetBuffer.Count < expectedLength)
                return;

            var packet = _packetBuffer.GetRange(0, expectedLength).ToArray();
            _packetBuffer.RemoveRange(0, expectedLength);
            packets.Add(new HciPacket(_pendingTimestamp, _pendingDirection.Value, packet));

            if (_packetBuffer.Count == 0)
                Reset();
        }
    }

    private static int GetHciH4PacketLength(IReadOnlyList<byte> bytes)
    {
        return bytes[0] switch
        {
            0x01 => bytes.Count >= 4 ? 4 + bytes[3] : 0,
            0x02 => bytes.Count >= 5 ? 5 + bytes[3] + (bytes[4] << 8) : 0,
            0x03 => bytes.Count >= 4 ? 4 + bytes[3] : 0,
            0x04 => bytes.Count >= 3 ? 3 + bytes[2] : 0,
            0x05 => bytes.Count >= 5 ? 5 + ((bytes[3] + (bytes[4] << 8)) & 0x3FFF) : 0,
            _ => -1
        };
    }

    private static byte[] ParseHexBytes(string hexText)
    {
        var parts = hexText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];

        for (var i = 0; i < parts.Length; i++)
            bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return bytes;
    }

    private static DateTime ExtractLogTimestamp(string line, DateTime fallback)
    {
        var match = LeadingTimestampRegex.Match(line);
        if (!match.Success)
            return fallback;

        var captures = match.Groups["time"].Captures;
        if (captures.Count == 0)
            return fallback;

        if (!TimeSpan.TryParseExact(captures[^1].Value, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var timeOfDay))
            return fallback;

        var candidate = fallback.Date.Add(timeOfDay);
        if (candidate - fallback > TimeSpan.FromHours(12))
            candidate = candidate.AddDays(-1);
        else if (fallback - candidate > TimeSpan.FromHours(12))
            candidate = candidate.AddDays(1);

        return candidate;
    }
}
