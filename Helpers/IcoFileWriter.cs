using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace SerialPortTool.Helpers;

/// <summary>
/// 将 PNG 帧写入多尺寸 .ico 文件（Windows Vista+ 支持 PNG 嵌入）。
/// </summary>
internal static class IcoFileWriter
{
    public static void Write(string path, IReadOnlyList<byte[]> pngFrames, IReadOnlyList<int> sizes)
    {
        if (pngFrames.Count != sizes.Count)
            throw new ArgumentException("PNG 帧数量与尺寸数量不一致");

        var count = pngFrames.Count;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)count);

        var dataOffset = 6 + count * 16;
        for (var i = 0; i < count; i++)
        {
            WriteDirectoryEntry(writer, sizes[i], pngFrames[i].Length, dataOffset);
            dataOffset += pngFrames[i].Length;
        }

        foreach (var png in pngFrames)
            writer.Write(png);
    }

    private static void WriteDirectoryEntry(BinaryWriter writer, int size, int bytesInRes, int offset)
    {
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(bytesInRes);
        writer.Write(offset);
    }

    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
