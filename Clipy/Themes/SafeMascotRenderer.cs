using System.Drawing;
using System.Drawing.Imaging;

namespace Clipy.Themes;

internal sealed class SafeMascotRenderer : IMascotRenderer
{
    private readonly IMascotRenderer _inner;

    public SafeMascotRenderer(IMascotRenderer inner) => _inner = inner;

    public Bitmap Render(int size, double phase)
    {
        try
        {
            return _inner.Render(size, phase);
        }
        catch
        {
            return CreateFallback(size);
        }
    }

    private static Bitmap CreateFallback(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.FromArgb(255, 210, 210, 210));
        g.FillEllipse(brush, size * 0.16f, size * 0.16f, size * 0.68f, size * 0.68f);
        return bmp;
    }
}
