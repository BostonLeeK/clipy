using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace IconGen;

internal interface IMascotRenderer
{
    Bitmap Render(int size, double phase);
}

internal sealed class NeonMascotRenderer : IMascotRenderer
{
    public Bitmap Render(int size, double phase)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var breathe = 0.5f + 0.5f * (float)Math.Sin(phase);
        var pulse = 0.5f + 0.5f * (float)Math.Sin(phase * 1.35 + 0.8);
        var swirl = (float)phase * 0.55f;

        var bodyScale = 0.78f + 0.10f * breathe;
        var bodySize = size * bodyScale;
        var bodyPad = (size - bodySize) * 0.5f;
        var bounds = new RectangleF(bodyPad, bodyPad, bodySize, bodySize);

        var auraScale = 0.92f + 0.08f * pulse;
        var auraSize = size * auraScale;
        var auraPad = (size - auraSize) * 0.5f;
        var aura = new RectangleF(auraPad, auraPad, auraSize, auraSize);
        var auraAlpha = (int)(55 + 70 * pulse);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(aura);
            using var auraBrush = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(size * 0.5f, size * 0.5f),
                CenterColor = Color.FromArgb(0, 200, 255, 77),
                SurroundColors = new[] { Color.FromArgb(auraAlpha, 200, 255, 77) },
                FocusScales = new PointF(0.55f, 0.55f),
            };
            g.FillEllipse(auraBrush, aura);
        }

        var coreLight = (int)(48 + 28 * breathe);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(bounds);
            using var body = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(
                    bounds.Left + bounds.Width * (0.34f + 0.08f * (float)Math.Cos(swirl)),
                    bounds.Top + bounds.Height * (0.32f + 0.08f * (float)Math.Sin(swirl))),
                CenterColor = Color.FromArgb(255, coreLight + 20, coreLight + 40, coreLight + 90),
                SurroundColors = new[] { Color.FromArgb(255, 10, 14, 28) },
                FocusScales = new PointF(0.42f, 0.42f),
            };
            g.FillEllipse(body, bounds);
        }

        var rimAlpha = (int)(120 + 100 * pulse);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(bounds);
            using var rim = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(bounds.Left + bounds.Width * 0.5f, bounds.Top + bounds.Height * 0.5f),
                CenterColor = Color.FromArgb(0, 200, 255, 77),
                SurroundColors = new[] { Color.FromArgb(rimAlpha, 200, 255, 77) },
                FocusScales = new PointF(0.78f, 0.78f),
            };
            g.FillEllipse(rim, bounds);
        }

        using (var pen = new Pen(Color.FromArgb((int)(90 + 110 * pulse), 200, 255, 77), 1.6f + pulse))
        {
            var ringInset = 1.2f;
            g.DrawEllipse(pen, bounds.Left + ringInset, bounds.Top + ringInset,
                bounds.Width - ringInset * 2f, bounds.Height - ringInset * 2f);
        }

        var glowSize = bounds.Width * (0.42f + 0.18f * pulse);
        var hx = bounds.Left + bounds.Width * (0.28f + 0.12f * (float)Math.Cos(swirl));
        var hy = bounds.Top + bounds.Height * (0.22f + 0.10f * (float)Math.Sin(swirl * 1.1));
        var glowRect = new RectangleF(hx, hy, glowSize, glowSize);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(glowRect);
            using var glow = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(glowRect.Left + glowRect.Width * 0.4f, glowRect.Top + glowRect.Height * 0.35f),
                CenterColor = Color.FromArgb((int)(120 + 90 * pulse), 210, 230, 255),
                SurroundColors = new[] { Color.FromArgb(0, 184, 208, 240) },
            };
            g.FillEllipse(glow, glowRect);
        }

        return bmp;
    }
}
