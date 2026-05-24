using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SerialPortTool.Helpers;

public static class AppIconHelper
{
    public static ImageSource? CreateFromDrawing(DrawingImage? drawing, int size = 32)
    {
        if (drawing?.Drawing == null) return null;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawDrawing(drawing.Drawing);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        return bitmap;
    }

    public static void ApplyWindowIcon(Window window, DrawingImage? drawing)
    {
        var icon = CreateFromDrawing(drawing, 32);
        if (icon != null)
            window.Icon = icon;
    }
}
