using System.Drawing;
using System.Drawing.Imaging;

namespace Clipy.Helpers;

public static class IconEncoder
{
    public static Icon CreateIcon(Bitmap bitmap)
    {
        using var stream = new MemoryStream(EncodeIco(bitmap));
        return new Icon(stream);
    }

    public static void SaveIco(string path, params Bitmap[] bitmaps)
    {
        var bytes = EncodeIco(bitmaps);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, bytes);
    }

    public static byte[] EncodeIco(params Bitmap[] bitmaps)
    {
        if (bitmaps.Length == 0)
            throw new ArgumentException("At least one bitmap is required.", nameof(bitmaps));

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
