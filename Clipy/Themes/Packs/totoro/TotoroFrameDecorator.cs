using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

using Clipy.Themes;

namespace Clipy.Themes.Packs.Totoro;

public sealed class TotoroFrameDecorator : IFrameDecorator
{
    public Bitmap Render(int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        DrawCornerCluster(g, 18, 20, -18, 1f);
        DrawCornerCluster(g, width - 18, 22, 22, 0.95f);
        DrawCornerCluster(g, 20, height - 22, -150, 1.05f);
        DrawCornerCluster(g, width - 20, height - 20, 155, 1f);

        DrawEdgeLeaves(g, width, height);
        DrawTinySprouts(g, width, height);

        return bmp;
    }

    private static void DrawCornerCluster(Graphics g, float x, float y, float baseAngle, float scale)
    {
        DrawLeaf(g, x, y, 34 * scale, 16 * scale, baseAngle - 28, Color.FromArgb(235, 110, 160, 70), Color.FromArgb(235, 70, 120, 50));
        DrawLeaf(g, x, y, 30 * scale, 14 * scale, baseAngle + 8, Color.FromArgb(230, 140, 190, 90), Color.FromArgb(230, 85, 140, 55));
        DrawLeaf(g, x, y, 26 * scale, 12 * scale, baseAngle + 38, Color.FromArgb(220, 95, 145, 65), Color.FromArgb(220, 60, 110, 45));
        DrawAcorn(g, x + MathF.Cos(Deg(baseAngle + 5)) * 8 * scale, y + MathF.Sin(Deg(baseAngle + 5)) * 8 * scale, 5.5f * scale, baseAngle);
    }

    private static void DrawEdgeLeaves(Graphics g, int width, int height)
    {
        DrawLeaf(g, width * 0.28f, 10, 22, 10, -70, Color.FromArgb(200, 120, 170, 80), Color.FromArgb(200, 75, 130, 55));
        DrawLeaf(g, width * 0.72f, 12, 20, 9, 75, Color.FromArgb(195, 100, 155, 70), Color.FromArgb(195, 65, 120, 50));
        DrawLeaf(g, width * 0.22f, height - 12, 21, 10, -120, Color.FromArgb(200, 130, 180, 85), Color.FromArgb(200, 80, 135, 55));
        DrawLeaf(g, width * 0.78f, height - 11, 20, 9, 115, Color.FromArgb(190, 105, 160, 75), Color.FromArgb(190, 70, 125, 50));

        DrawLeaf(g, 11, height * 0.32f, 18, 8, -10, Color.FromArgb(185, 115, 165, 75), Color.FromArgb(185, 70, 125, 50));
        DrawLeaf(g, 12, height * 0.68f, 17, 8, 15, Color.FromArgb(180, 95, 150, 70), Color.FromArgb(180, 60, 115, 48));
        DrawLeaf(g, width - 11, height * 0.30f, 18, 8, 190, Color.FromArgb(185, 125, 175, 80), Color.FromArgb(185, 75, 130, 52));
        DrawLeaf(g, width - 12, height * 0.70f, 17, 8, 165, Color.FromArgb(180, 100, 155, 72), Color.FromArgb(180, 65, 120, 48));
    }

    private static void DrawTinySprouts(Graphics g, int width, int height)
    {
        using var stem = new Pen(Color.FromArgb(160, 80, 120, 55), 1.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawLine(stem, width * 0.5f - 8, 8, width * 0.5f, 2);
        g.DrawLine(stem, width * 0.5f + 8, 8, width * 0.5f, 2);
        DrawLeaf(g, width * 0.5f - 6, 6, 11, 5, -50, Color.FromArgb(210, 150, 200, 100), Color.FromArgb(210, 90, 145, 60));
        DrawLeaf(g, width * 0.5f + 6, 6, 11, 5, 50, Color.FromArgb(210, 135, 185, 90), Color.FromArgb(210, 85, 140, 55));
    }

    private static void DrawLeaf(
        Graphics g, float x, float y, float length, float width,
        float angleDeg, Color light, Color dark)
    {
        var state = g.Save();
        g.TranslateTransform(x, y);
        g.RotateTransform(angleDeg);

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(-width * 0.5f, -length, width, length);
            using var brush = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(-width * 0.08f, -length * 0.55f),
                CenterColor = light,
                SurroundColors = new[] { dark },
                FocusScales = new PointF(0.55f, 0.55f),
            };
            g.FillPath(brush, path);
        }

        using var vein = new Pen(Color.FromArgb(140, 45, 85, 35), Math.Max(1f, width * 0.12f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawLine(vein, 0, -length * 0.12f, 0, -length * 0.9f);
        g.DrawLine(vein, 0, -length * 0.45f, width * 0.22f, -length * 0.62f);
        g.DrawLine(vein, 0, -length * 0.45f, -width * 0.22f, -length * 0.62f);
        g.Restore(state);
    }

    private static void DrawAcorn(Graphics g, float x, float y, float size, float angleDeg)
    {
        var state = g.Save();
        g.TranslateTransform(x, y);
        g.RotateTransform(angleDeg * 0.15f);

        using (var nut = new SolidBrush(Color.FromArgb(210, 170, 120, 70)))
            g.FillEllipse(nut, -size * 0.45f, -size * 0.15f, size * 0.9f, size * 1.05f);

        using (var cap = new SolidBrush(Color.FromArgb(220, 110, 80, 45)))
            g.FillEllipse(cap, -size * 0.55f, -size * 0.55f, size * 1.1f, size * 0.7f);

        using (var stem = new Pen(Color.FromArgb(200, 90, 70, 40), Math.Max(1f, size * 0.18f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        })
            g.DrawLine(stem, 0, -size * 0.55f, size * 0.15f, -size * 0.95f);

        g.Restore(state);
    }

    private static float Deg(float degrees) => degrees * MathF.PI / 180f;
}
