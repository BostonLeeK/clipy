using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Clipy.Themes;

namespace Clipy.Helpers;

public static class TrayIconHelper
{
    public static Icon Create(IMascotRenderer renderer)
    {
        using var source = renderer.Render(64, 0);
        using var sized = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(sized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.CompositingMode = CompositingMode.SourceOver;
            g.Clear(Color.Transparent);
            g.DrawImage(source, 0, 0, 32, 32);
        }

        var handle = sized.GetHicon();
        using var temp = Icon.FromHandle(handle);
        return (Icon)temp.Clone();
    }
}
