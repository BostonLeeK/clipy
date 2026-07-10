using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace IconGen;

internal static class AppIconRenderer
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
}

internal static class IconEncoder
{
    public static void SaveIco(string path, params Bitmap[] bitmaps)
    {
        var bytes = EncodeIco(bitmaps);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] EncodeIco(params Bitmap[] bitmaps)
    {
        var images = new List<(int Width, int Height, byte[] Png)>(bitmaps.Length);
        foreach (var bitmap in bitmaps)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            images.Add((bitmap.Width, bitmap.Height, stream.ToArray()));
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        var offset = 6 + images.Count * 16;
        foreach (var (width, height, png) in images)
        {
            writer.Write(ToIcoDimension(width));
            writer.Write(ToIcoDimension(height));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, _, png) in images)
            writer.Write(png);

        return output.ToArray();
    }

    private static byte ToIcoDimension(int value) => value >= 256 ? (byte)0 : (byte)value;
}
