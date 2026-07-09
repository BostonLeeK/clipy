using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Clipy.Helpers;

public static class ScreenshotHelper
{
    private static readonly string CaptureDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipyAssistant", "captures");

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int SrcCopy = 0x00CC0020;

    public static string CapturePrimaryScreen()
    {
        Directory.CreateDirectory(CaptureDir);
        var w = GetSystemMetrics(SmCxScreen);
        var h = GetSystemMetrics(SmCyScreen);
        var path = Path.Combine(CaptureDir, $"screen-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var screenDc = GetDC(IntPtr.Zero);
        var destDc = g.GetHdc();
        try
        {
            BitBlt(destDc, 0, 0, w, h, screenDc, 0, 0, SrcCopy);
        }
        finally
        {
            g.ReleaseHdc(destDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    public static async Task<string?> SaveClipboardImageAsync()
    {
        var data = Clipboard.GetContent();
        if (!data.Contains(StandardDataFormats.Bitmap))
            return null;

        var streamRef = await data.GetBitmapAsync();
        using var stream = await streamRef.OpenReadAsync();
        Directory.CreateDirectory(CaptureDir);
        var path = Path.Combine(CaptureDir, $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        using var netStream = stream.AsStreamForRead();
        using var image = Image.FromStream(netStream);
        image.Save(path, ImageFormat.Png);
        return path;
    }

    public static async Task<IReadOnlyList<string>> SaveClipboardFilesAsync()
    {
        var data = Clipboard.GetContent();
        if (!data.Contains(StandardDataFormats.StorageItems))
            return Array.Empty<string>();

        var items = await data.GetStorageItemsAsync();
        return items.Select(i => i.Path).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    public static async Task<string?> GetClipboardTextAsync()
    {
        var data = Clipboard.GetContent();
        if (!data.Contains(StandardDataFormats.Text))
            return null;
        return await data.GetTextAsync();
    }
}
