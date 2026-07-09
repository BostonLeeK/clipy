using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Clipy.Themes;

public sealed class KawaiiMascotRenderer : IMascotRenderer
{
    public Bitmap Render(int size, double phase)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounce = (float)Math.Sin(phase * 1.15) * size * 0.028f;
        var blink = Math.Pow(Math.Max(0, Math.Sin(phase * 0.48)), 28);
        var breath = 0.5f + 0.5f * (float)Math.Sin(phase * 1.6);
        var earWiggle = (float)Math.Sin(phase * 1.9) * 2.4f;

        var bodyW = size * 0.70f;
        var bodyH = size * 0.66f;
        var body = new RectangleF((size - bodyW) * 0.5f, size * 0.28f + bounce, bodyW, bodyH);

        DrawAura(g, size, bounce, breath);
        DrawEars(g, body, earWiggle);
        DrawBody(g, body);
        DrawBelly(g, body);
        DrawChevrons(g, body);
        DrawEyes(g, body, blink);
        DrawNose(g, body);
        DrawWhiskers(g, body);
        DrawLeaf(g, body, phase, bounce);

        return bmp;
    }

    private static void DrawAura(Graphics g, int size, float bounce, float pulse)
    {
        var aura = new RectangleF(size * 0.08f, size * 0.16f + bounce, size * 0.84f, size * 0.84f);
        using var path = new GraphicsPath();
        path.AddEllipse(aura);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(size * 0.5f, size * 0.52f + bounce),
            CenterColor = Color.FromArgb(0, 180, 210, 160),
            SurroundColors = new[] { Color.FromArgb((int)(28 + 36 * pulse), 140, 170, 120) },
            FocusScales = new PointF(0.52f, 0.52f),
        };
        g.FillEllipse(brush, aura);
    }

    private static void DrawEars(Graphics g, RectangleF body, float wiggle)
    {
        var earW = body.Width * 0.22f;
        var earH = body.Height * 0.48f;

        using var fur = new SolidBrush(Color.FromArgb(255, 112, 118, 122));
        using var tip = new SolidBrush(Color.FromArgb(255, 88, 92, 96));
        using var inner = new SolidBrush(Color.FromArgb(255, 210, 205, 198));

        DrawPointyEar(g, new PointF(body.Left + body.Width * 0.22f, body.Top + body.Height * 0.08f + wiggle * 0.15f),
            earW, earH, -8f + wiggle * 0.4f, fur, tip, inner);
        DrawPointyEar(g, new PointF(body.Right - body.Width * 0.22f, body.Top + body.Height * 0.08f - wiggle * 0.15f),
            earW, earH, 8f - wiggle * 0.4f, fur, tip, inner);
    }

    private static void DrawPointyEar(
        Graphics g, PointF baseCenter, float earW, float earH, float tiltDeg,
        Brush fur, Brush tip, Brush inner)
    {
        var state = g.Save();
        g.TranslateTransform(baseCenter.X, baseCenter.Y);
        g.RotateTransform(tiltDeg);

        var outer = new[]
        {
            new PointF(0, -earH),
            new PointF(earW * 0.55f, earH * 0.18f),
            new PointF(0, earH * 0.05f),
            new PointF(-earW * 0.55f, earH * 0.18f),
        };
        g.FillPolygon(fur, outer);

        var tipPts = new[]
        {
            new PointF(0, -earH),
            new PointF(earW * 0.22f, -earH * 0.55f),
            new PointF(-earW * 0.22f, -earH * 0.55f),
        };
        g.FillPolygon(tip, tipPts);

        var innerPts = new[]
        {
            new PointF(0, -earH * 0.72f),
            new PointF(earW * 0.22f, earH * 0.02f),
            new PointF(-earW * 0.22f, earH * 0.02f),
        };
        g.FillPolygon(inner, innerPts);
        g.Restore(state);
    }

    private static void DrawBody(Graphics g, RectangleF body)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(body);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(body.Left + body.Width * 0.42f, body.Top + body.Height * 0.30f),
            CenterColor = Color.FromArgb(255, 168, 172, 176),
            SurroundColors = new[] { Color.FromArgb(255, 98, 104, 110) },
            FocusScales = new PointF(0.58f, 0.58f),
        };
        g.FillEllipse(brush, body);

        using var outline = new Pen(Color.FromArgb(160, 70, 76, 82), 1.3f);
        g.DrawEllipse(outline, body);
    }

    private static void DrawBelly(Graphics g, RectangleF body)
    {
        var belly = new RectangleF(
            body.Left + body.Width * 0.18f,
            body.Top + body.Height * 0.42f,
            body.Width * 0.64f,
            body.Height * 0.50f);

        using var path = new GraphicsPath();
        path.AddEllipse(belly);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(belly.Left + belly.Width * 0.45f, belly.Top + belly.Height * 0.35f),
            CenterColor = Color.FromArgb(255, 248, 244, 232),
            SurroundColors = new[] { Color.FromArgb(255, 224, 216, 198) },
            FocusScales = new PointF(0.62f, 0.62f),
        };
        g.FillEllipse(brush, belly);
    }

    private static void DrawChevrons(Graphics g, RectangleF body)
    {
        using var pen = new Pen(Color.FromArgb(200, 120, 126, 132), Math.Max(1.2f, body.Width * 0.028f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        var cx = body.Left + body.Width * 0.5f;
        var top = body.Top + body.Height * 0.50f;
        var span = body.Width * 0.16f;
        var step = body.Height * 0.07f;

        for (var i = 0; i < 3; i++)
        {
            var y = top + i * step;
            var w = span * (0.85f + i * 0.12f);
            g.DrawLines(pen, new[]
            {
                new PointF(cx - w, y + step * 0.35f),
                new PointF(cx, y),
                new PointF(cx + w, y + step * 0.35f),
            });
        }
    }

    private static void DrawEyes(Graphics g, RectangleF body, double blink)
    {
        var eyeW = body.Width * 0.20f;
        var eyeH = body.Height * (0.22f * (float)(1.0 - 0.88 * blink));
        if (eyeH < 1.4f) eyeH = 1.4f;
        var eyeY = body.Top + body.Height * 0.28f + (body.Height * 0.22f - eyeH) * 0.5f;
        var left = new RectangleF(body.Left + body.Width * 0.22f, eyeY, eyeW, eyeH);
        var right = new RectangleF(body.Right - body.Width * 0.22f - eyeW, eyeY, eyeW, eyeH);

        using (var white = new SolidBrush(Color.FromArgb(255, 250, 248, 240)))
        {
            g.FillEllipse(white, left);
            g.FillEllipse(white, right);
        }

        if (blink < 0.72)
        {
            var pupilW = eyeW * 0.58f;
            var pupilH = eyeH * 0.68f;
            var pupilL = new RectangleF(left.Left + eyeW * 0.24f, left.Top + eyeH * 0.22f, pupilW, pupilH);
            var pupilR = new RectangleF(right.Left + eyeW * 0.24f, right.Top + eyeH * 0.22f, pupilW, pupilH);
            using var pupil = new SolidBrush(Color.FromArgb(255, 36, 38, 42));
            g.FillEllipse(pupil, pupilL);
            g.FillEllipse(pupil, pupilR);

            var shine = Math.Min(eyeW, eyeH) * 0.24f;
            using var spark = new SolidBrush(Color.White);
            g.FillEllipse(spark, left.Left + eyeW * 0.16f, left.Top + eyeH * 0.14f, shine, shine);
            g.FillEllipse(spark, right.Left + eyeW * 0.16f, right.Top + eyeH * 0.14f, shine, shine);
        }
        else
        {
            using var lid = new Pen(Color.FromArgb(220, 60, 64, 70), Math.Max(1.2f, body.Width * 0.02f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLine(lid, left.Left, left.Top + left.Height * 0.5f, left.Right, left.Top + left.Height * 0.5f);
            g.DrawLine(lid, right.Left, right.Top + right.Height * 0.5f, right.Right, right.Top + right.Height * 0.5f);
        }
    }

    private static void DrawNose(Graphics g, RectangleF body)
    {
        var nose = new RectangleF(
            body.Left + body.Width * 0.45f,
            body.Top + body.Height * 0.46f,
            body.Width * 0.10f,
            body.Height * 0.07f);
        using var brush = new SolidBrush(Color.FromArgb(255, 52, 54, 58));
        g.FillEllipse(brush, nose);
    }

    private static void DrawWhiskers(Graphics g, RectangleF body)
    {
        using var pen = new Pen(Color.FromArgb(170, 70, 74, 80), Math.Max(1f, body.Width * 0.015f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        var y = body.Top + body.Height * 0.48f;
        var leftX = body.Left + body.Width * 0.18f;
        var rightX = body.Right - body.Width * 0.18f;
        var len = body.Width * 0.16f;

        g.DrawLine(pen, leftX, y - body.Height * 0.02f, leftX - len, y - body.Height * 0.05f);
        g.DrawLine(pen, leftX, y + body.Height * 0.02f, leftX - len, y + body.Height * 0.05f);
        g.DrawLine(pen, rightX, y - body.Height * 0.02f, rightX + len, y - body.Height * 0.05f);
        g.DrawLine(pen, rightX, y + body.Height * 0.02f, rightX + len, y + body.Height * 0.05f);
    }

    private static void DrawLeaf(Graphics g, RectangleF body, double phase, float bounce)
    {
        var sway = (float)Math.Sin(phase * 1.7) * 10f;
        var cx = body.Left + body.Width * 0.72f;
        var cy = body.Top - body.Height * 0.02f + bounce * 0.4f;
        var leafW = body.Width * 0.18f;
        var leafH = body.Height * 0.22f;

        var state = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform(-28f + sway);

        using (var leaf = new GraphicsPath())
        {
            leaf.AddEllipse(-leafW * 0.5f, -leafH, leafW, leafH);
            using var brush = new PathGradientBrush(leaf)
            {
                CenterPoint = new PointF(-leafW * 0.1f, -leafH * 0.55f),
                CenterColor = Color.FromArgb(255, 170, 210, 120),
                SurroundColors = new[] { Color.FromArgb(255, 90, 140, 70) },
                FocusScales = new PointF(0.55f, 0.55f),
            };
            g.FillPath(brush, leaf);
        }

        using var vein = new Pen(Color.FromArgb(160, 60, 100, 50), Math.Max(1f, leafW * 0.08f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawLine(vein, 0, -leafH * 0.15f, 0, -leafH * 0.92f);
        g.Restore(state);
    }
}
