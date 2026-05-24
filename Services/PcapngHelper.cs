using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SerialPortTool.Services;

public static class PcapngHelper
{
    private const uint BlockTypeShb = 0x0A0D0D0A;
    private const uint BlockTypeIdb = 0x00000001;
    private const uint BlockTypeEpb = 0x00000006;
    private const uint ByteOrderMagic = 0x1A2B3C4D;
    private const ushort LinkTypeBluetoothHciH4WithPhdr = 201;

    public static byte[] CreateGlobalHeader()
    {
        var shb = CreateSectionHeaderBlock();
        var idb = CreateInterfaceDescriptionBlock();
        var result = new byte[shb.Length + idb.Length];
        Buffer.BlockCopy(shb, 0, result, 0, shb.Length);
        Buffer.BlockCopy(idb, 0, result, shb.Length, idb.Length);
        return result;
    }

    public static byte[] CreatePacket(HciPacket packet)
    {
        if (packet.Data.Length == 0)
            return Array.Empty<byte>();

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(BlockTypeEpb);
        var blockLengthPos = ms.Position;
        writer.Write(0);

        writer.Write(0);

        var epochMicroseconds = ToEpochMicroseconds(packet.Timestamp);
        writer.Write((uint)(epochMicroseconds >> 32));
        writer.Write((uint)(epochMicroseconds & 0xFFFFFFFF));

        var pseudoHeader = CreateBluetoothDirectionPseudoHeader(packet.Direction);
        var capturedLength = pseudoHeader.Length + packet.Data.Length;
        writer.Write((uint)capturedLength);
        writer.Write((uint)capturedLength);
        writer.Write(pseudoHeader);
        writer.Write(packet.Data);

        WritePadding(writer, capturedLength);
        WriteEndOfOptions(writer);

        WriteBlockLength(ms, writer, blockLengthPos);
        return ms.ToArray();
    }

    private static byte[] CreateSectionHeaderBlock()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(BlockTypeShb);
        var blockLengthPos = ms.Position;
        writer.Write(0);
        writer.Write(ByteOrderMagic);
        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write(0xFFFFFFFFFFFFFFFFUL);
        WriteEndOfOptions(writer);
        WriteBlockLength(ms, writer, blockLengthPos);
        return ms.ToArray();
    }

    private static byte[] CreateInterfaceDescriptionBlock()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(BlockTypeIdb);
        var blockLengthPos = ms.Position;
        writer.Write(0);
        writer.Write(LinkTypeBluetoothHciH4WithPhdr);
        writer.Write((ushort)0);
        writer.Write(0x0000FFFF);

        WriteOption(writer, 2, Encoding.ASCII.GetBytes("hci0"));
        WriteOption(writer, 9, new byte[] { 6 });
        WriteEndOfOptions(writer);

        WriteBlockLength(ms, writer, blockLengthPos);
        return ms.ToArray();
    }

    private static byte[] CreateBluetoothDirectionPseudoHeader(HciPacketDirection direction)
    {
        var pseudoHeader = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(pseudoHeader, direction == HciPacketDirection.Rx ? 1u : 0u);
        return pseudoHeader;
    }

    private static long ToEpochMicroseconds(DateTime timestamp)
    {
        var utc = timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Local).ToUniversalTime()
            : timestamp.ToUniversalTime();
        return (utc.Ticks - DateTime.UnixEpoch.Ticks) / 10;
    }

    private static void WriteOption(BinaryWriter writer, ushort code, byte[] value)
    {
        writer.Write(code);
        writer.Write((ushort)value.Length);
        writer.Write(value);
        WritePadding(writer, value.Length);
    }

    private static void WriteEndOfOptions(BinaryWriter writer)
    {
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        while (writer.BaseStream.Position % 4 != 0)
            writer.Write((byte)0);
    }

    private static void WritePadding(BinaryWriter writer, int dataLength)
    {
        var padding = (4 - (dataLength % 4)) % 4;
        for (var i = 0; i < padding; i++)
            writer.Write((byte)0);
    }

    private static void WriteBlockLength(MemoryStream ms, BinaryWriter writer, long blockLengthPos)
    {
        var blockLength = (int)ms.Position + 4;
        ms.Position = blockLengthPos;
        writer.Write(blockLength);
        ms.Position = ms.Length;
        writer.Write(blockLength);
    }
}
