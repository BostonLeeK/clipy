using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Clipy.Themes;

namespace Clipy.Helpers;

public static class AppIconRenderer
{
    private static readonly Color Background = Color.FromArgb(255, 11, 11, 15);

    public static Bitmap Render(IMascotRenderer renderer, int size, double phase = 0)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Background);
        g.CompositingMode = CompositingMode.SourceOver;

        var inset = Math.Max(1, size / 16);
        using (var path = RoundedRect(inset, inset, size - inset * 2, size - inset * 2, size / 5f))
        using (var brush = new SolidBrush(Background))
            g.FillPath(brush, path);

        using var mascot = renderer.Render(size, phase);
        g.DrawImage(mascot, 0, 0, size, size);
        return bmp;
    }

    public static void SavePng(IMascotRenderer renderer, string path, int size, double phase = 0)
    {
        using var bmp = Render(renderer, size, phase);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        bmp.Save(path, ImageFormat.Png);
    }

    public static void SaveIco(IMascotRenderer renderer, string path, params int[] sizes)
    {
        if (sizes.Length == 0)
            sizes = [256, 128, 64, 48, 32, 16];

        var bitmaps = sizes.Select(size => Render(renderer, size)).ToArray();
        try
        {
            IconEncoder.SaveIco(path, bitmaps);
        }
        finally
        {
            foreach (var bitmap in bitmaps)
                bitmap.Dispose();
        }
    }

    private static GraphicsPath RoundedRect(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2f;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
