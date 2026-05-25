using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SerialPortTool.Helpers;

/// <summary>
/// 生成任务栏图标角标（32×32），显示已打开串口数量。
/// </summary>
public static class TaskbarBadgeHelper
{
    private const int Size = 32;
    private static readonly Dictionary<int, ImageSource> Cache = new();

    public static ImageSource CreateOverlay(int count)
    {
        count = Math.Clamp(count, 0, 9);
        if (Cache.TryGetValue(count, out var cached))
            return cached;

        var background = count == 0
            ? Color.FromRgb(0x9E, 0x9E, 0x9E)
            : Color.FromRgb(0x21, 0x96, 0xF3);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var radius = Size / 2.0 - 1;
            dc.DrawEllipse(
                new SolidColorBrush(background),
                null,
                new Point(Size / 2.0, Size / 2.0),
                radius,
                radius);

            var text = new FormattedText(
                count.ToString(),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                count > 1 ? 18 : 20,
                Brushes.White,
                1.0);

            dc.DrawText(
                text,
                new Point((Size - text.Width) / 2, (Size - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        Cache[count] = bitmap;
        return bitmap;
    }

    public static string GetDescription(int count) =>
        count switch
        {
            0 => "未打开串口",
            1 => "已打开 1 个串口",
            _ => $"已打开 {count} 个串口"
        };
}
