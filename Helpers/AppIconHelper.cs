using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SerialPortTool.Helpers;

public static class AppIconHelper
{
    private const uint WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;

    private static readonly int[] AppIconSizes = { 16, 32, 48, 256 };
    private static readonly int TaskbarBigIconSize = 48;
    private static readonly int TaskbarSmallIconSize = 16;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static ImageSource? CreateFromDrawing(DrawingImage? drawing, int size = 48)
    {
        if (drawing?.Drawing == null) return null;

        var bounds = drawing.Drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        const double paddingRatio = 0.04;
        var targetSize = size * (1 - paddingRatio * 2);
        var scale = Math.Min(targetSize / bounds.Width, targetSize / bounds.Height);
        var scaledWidth = bounds.Width * scale;
        var scaledHeight = bounds.Height * scale;
        var offsetX = (size - scaledWidth) / 2 - bounds.X * scale;
        var offsetY = (size - scaledHeight) / 2 - bounds.Y * scale;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, offsetX, offsetY));
            dc.DrawDrawing(drawing.Drawing);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void ApplyWindowIcon(Window window, DrawingImage? drawing)
    {
        if (drawing == null) return;

        var wpfIcon = CreateFromDrawing(drawing, TaskbarBigIconSize);
        if (wpfIcon != null)
            window.Icon = wpfIcon;

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            SetNativeIcons(window, drawing);
        }

        if (window.IsInitialized)
            SetNativeIcons(window, drawing);
        else
            window.SourceInitialized += OnSourceInitialized;
    }

    private static void SetNativeIcons(Window window, DrawingImage drawing)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        SetIcon(hwnd, IconBig, CreateNativeIcon(drawing, TaskbarBigIconSize));
        SetIcon(hwnd, IconSmall, CreateNativeIcon(drawing, TaskbarSmallIconSize));
    }

    private static void SetIcon(IntPtr hwnd, int iconType, IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return;

        var previous = SendMessage(hwnd, WmSetIcon, (IntPtr)iconType, hIcon);
        if (previous != IntPtr.Zero)
            DestroyIcon(previous);

        DestroyIcon(hIcon);
    }

    private static IntPtr CreateNativeIcon(DrawingImage drawing, int size)
    {
        if (CreateFromDrawing(drawing, size) is not BitmapSource source)
            return IntPtr.Zero;

        using var bitmap = BitmapSourceToBitmap(source);
        return bitmap.GetHicon();
    }

    private static Bitmap BitmapSourceToBitmap(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[height * stride];
        source.CopyPixels(pixels, stride, 0);

        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            bitmap.PixelFormat);

        try
        {
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>
    /// 从矢量图导出多尺寸 app.ico（供 ApplicationIcon 使用）。
    /// </summary>
    public static void ExportAppIco(string outputPath, DrawingImage drawing)
    {
        var pngFrames = new List<byte[]>();
        var sizes = new List<int>();

        foreach (var size in AppIconSizes)
        {
            if (CreateFromDrawing(drawing, size) is not BitmapSource bitmap)
                continue;

            pngFrames.Add(IcoFileWriter.EncodePng(bitmap));
            sizes.Add(size);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        IcoFileWriter.Write(outputPath, pngFrames, sizes);
    }

    /// <summary>
    /// 命令行生成图标：dotnet run -- --generate-icon
    /// </summary>
    public static bool TryGenerateAppIcoFromArgs(string[] args)
    {
        if (args.Length == 0 || !args.Contains("--generate-icon"))
            return false;

        var baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        if (!File.Exists(Path.Combine(baseDir, "SerialPortTool.csproj")))
            baseDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        var xamlPath = Path.Combine(baseDir, "Resources", "IconResources.xaml");
        var icoPath = Path.Combine(baseDir, "Assets", "app.ico");

        var rd = new ResourceDictionary { Source = new Uri(xamlPath, UriKind.Absolute) };
        if (rd["AppLogoImage"] is not DrawingImage logo)
            throw new InvalidOperationException("未在 IconResources.xaml 中找到 AppLogoImage");

        ExportAppIco(icoPath, logo);
        Console.WriteLine($"已生成: {icoPath}");
        return true;
    }
}
