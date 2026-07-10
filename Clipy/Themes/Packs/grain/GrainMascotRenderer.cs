using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Clipy.Themes;

namespace Clipy.Themes.Packs.Grain;

public sealed class GrainMascotRenderer : IMascotRenderer
{
    private static readonly Dictionary<int, Bitmap> GrainOverlays = new();

    public Bitmap Render(int size, double phase)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var breathe = 0.88f + 0.12f * (float)Math.Sin(phase * 1.05);
        var pulse = 0.5f + 0.5f * (float)Math.Sin(phase * 1.4 + 0.6);
        var swayX = (float)Math.Sin(phase * 0.9) * size * 0.035f;
        var swayY = (float)Math.Cos(phase * 0.65) * size * 0.03f;
        var cx = size * 0.5f + swayX;
        var cy = size * 0.54f + swayY;
        var baseR = size * 0.33f * breathe;

        using var blob = BuildBlobPath(cx, cy, baseR, phase);
        if (blob.GetBounds() is { Width: < 0.5f } or { Height: < 0.5f })
            return bmp;

        using var halo = BuildBlobPath(cx, cy, baseR * 1.18f, phase + 0.4);

        DrawHalo(g, halo, cx, cy, pulse);
        DrawBody(g, blob, size, phase, pulse);
        DrawGrain(g, blob, size, phase);
        DrawContours(g, blob, size, phase, pulse);
        DrawEye(g, cx, cy, baseR, size, phase);
        DrawSatellite(g, cx, cy, size, phase, pulse);

        return bmp;
    }

    private static GraphicsPath BuildBlobPath(float cx, float cy, float radius, double phase)
    {
        var wobble = (float)Math.Sin(phase * 1.25);
        var lean = (float)Math.Sin(phase * 0.55) * 0.08f;
        var path = new GraphicsPath();
        var points = new PointF[9];
        for (var i = 0; i < 9; i++)
        {
            var angle = i * Math.PI * 2 / 9 + phase * 0.12;
            var ripple = 1f
                + 0.18f * (float)Math.Sin(angle * 2.6 + phase * 1.7)
                + 0.08f * wobble
                + (i == 2 || i == 3 ? 0.12f : 0f);
            var squashY = i is 6 or 7 or 8 ? 1.18f : 0.9f;
            var squashX = 1f + lean * (float)Math.Cos(angle);
            var r = radius * ripple;
            points[i] = new PointF(
                cx + MathF.Cos((float)angle) * r * squashX,
                cy + MathF.Sin((float)angle) * r * squashY);
        }
        path.AddClosedCurve(points, 0.62f);
        return path;
    }

    private static void DrawHalo(Graphics g, GraphicsPath halo, float cx, float cy, float pulse)
    {
        using var brush = new PathGradientBrush(halo)
        {
            CenterPoint = new PointF(cx, cy),
            CenterColor = Color.FromArgb(0, 255, 255, 255),
            SurroundColors = new[] { Color.FromArgb((int)(18 + 34 * pulse), 255, 255, 255) },
            FocusScales = new PointF(0.62f, 0.62f),
        };
        g.FillPath(brush, halo);
    }

    private static void DrawBody(Graphics g, GraphicsPath blob, int size, double phase, float pulse)
    {
        var bounds = blob.GetBounds();
        var lightX = bounds.Left + bounds.Width * (0.22f + 0.08f * (float)Math.Cos(phase * 0.7));
        var lightY = bounds.Top + bounds.Height * (0.18f + 0.06f * (float)Math.Sin(phase * 0.9));

        using var fill = new PathGradientBrush(blob)
        {
            CenterPoint = new PointF(lightX, lightY),
            CenterColor = Color.FromArgb(255, 236, 236, 236),
            SurroundColors = new[] { Color.FromArgb(255, 34, 34, 34) },
            FocusScales = new PointF(0.5f, 0.5f),
        };
        g.FillPath(fill, blob);

        using var edge = new Pen(Color.FromArgb((int)(110 + 90 * pulse), 250, 250, 250), Math.Max(1f, size * 0.018f));
        g.DrawPath(edge, blob);
    }

    private static void DrawGrain(Graphics g, GraphicsPath blob, int size, double phase)
    {
        var overlay = GetGrainOverlay(size);
        var shiftX = (int)(((phase * 11) % 1.0) * size * 0.1f);
        var shiftY = (int)(((phase * 7) % 1.0) * size * 0.1f);

        var state = g.Save();
        g.SetClip(blob);
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(new ColorMatrix { Matrix33 = 0.38f });
        g.DrawImage(
            overlay,
            new Rectangle(shiftX - 2, shiftY - 2, size + 4, size + 4),
            0, 0, size, size,
            GraphicsUnit.Pixel,
            attrs);
        g.Restore(state);
    }

    private static void DrawContours(Graphics g, GraphicsPath blob, int size, double phase, float pulse)
    {
        var bounds = blob.GetBounds();
        var inset = size * 0.08f;
        using var pen = new Pen(Color.FromArgb((int)(35 + 55 * pulse), 255, 255, 255), Math.Max(0.8f, size * 0.012f));
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;

        for (var i = 0; i < 3; i++)
        {
            var t = 0.28f + i * 0.18f;
            var arcW = bounds.Width * (0.72f - i * 0.12f);
            var arcH = bounds.Height * (0.22f - i * 0.03f);
            var arcX = bounds.Left + bounds.Width * 0.14f + (float)Math.Sin(phase * 0.8 + i) * inset * 0.2f;
            var arcY = bounds.Top + bounds.Height * t;
            g.DrawArc(pen, arcX, arcY, arcW, arcH, 200, 140);
        }
    }

    private static void DrawEye(Graphics g, float cx, float cy, float baseR, int size, double phase)
    {
        var blink = Math.Pow(Math.Max(0, Math.Sin(phase * 0.42)), 24);
        var lookX = (float)Math.Sin(phase * 0.75) * baseR * 0.08f;
        var eyeY = cy - baseR * 0.08f;
        var eyeW = baseR * 0.72f;
        var eyeH = baseR * 0.34f * (1f - 0.88f * (float)blink);
        if (eyeH < size * 0.02f) return;

        var eyeRect = new RectangleF(cx - eyeW * 0.5f + lookX, eyeY - eyeH * 0.5f, eyeW, eyeH);
        using (var sclera = new GraphicsPath())
        {
            sclera.AddEllipse(eyeRect);
            using var brush = new PathGradientBrush(sclera)
            {
                CenterPoint = new PointF(eyeRect.Left + eyeRect.Width * 0.35f, eyeRect.Top + eyeRect.Height * 0.3f),
                CenterColor = Color.FromArgb(255, 252, 252, 252),
                SurroundColors = new[] { Color.FromArgb(255, 120, 120, 120) },
                FocusScales = new PointF(0.55f, 0.55f),
            };
            g.FillEllipse(brush, eyeRect);
        }

        var pupilW = eyeW * 0.22f;
        var pupilH = Math.Max(eyeH * 0.82f, size * 0.03f);
        var pupilRect = new RectangleF(
            cx - pupilW * 0.5f + lookX,
            eyeY - pupilH * 0.5f,
            pupilW,
            pupilH);
        using var pupilBrush = new SolidBrush(Color.FromArgb(255, 12, 12, 12));
        g.FillEllipse(pupilBrush, pupilRect);

        var gleam = Math.Max(1.5f, size * 0.03f);
        var gleamRect = new RectangleF(
            pupilRect.Left + pupilRect.Width * 0.18f,
            pupilRect.Top + pupilRect.Height * 0.16f,
            gleam,
            gleam * 1.4f);
        using var gleamBrush = new SolidBrush(Color.FromArgb(210, 255, 255, 255));
        g.FillEllipse(gleamBrush, gleamRect);
    }

    private static void DrawSatellite(Graphics g, float cx, float cy, int size, double phase, float pulse)
    {
        var orbitR = size * 0.31f;
        var satX = cx + MathF.Cos((float)(phase * 1.15)) * orbitR;
        var satY = cy + MathF.Sin((float)(phase * 1.15)) * orbitR * 0.55f + size * 0.02f;
        var satR = size * 0.075f * (0.9f + 0.1f * pulse);
        var satRect = new RectangleF(satX - satR, satY - satR, satR * 2f, satR * 2f);

        using (var path = new GraphicsPath())
        {
            path.AddEllipse(satRect);
            using var brush = new PathGradientBrush(path)
            {
                CenterPoint = new PointF(satRect.Left + satRect.Width * 0.3f, satRect.Top + satRect.Height * 0.28f),
                CenterColor = Color.FromArgb(255, 220, 220, 220),
                SurroundColors = new[] { Color.FromArgb(255, 70, 70, 70) },
                FocusScales = new PointF(0.45f, 0.45f),
            };
            g.FillEllipse(brush, satRect);
        }

        using var ring = new Pen(Color.FromArgb((int)(80 + 70 * pulse), 255, 255, 255), Math.Max(0.8f, size * 0.01f));
        g.DrawEllipse(ring, satRect);
    }

    private static Bitmap GetGrainOverlay(int size)
    {
        lock (GrainOverlays)
        {
            if (GrainOverlays.TryGetValue(size, out var cached))
                return cached;

            var overlay = BuildGrainOverlay(size);
            GrainOverlays[size] = overlay;
            return overlay;
        }
    }

    private static Bitmap BuildGrainOverlay(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, size, size);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[stride * size];

            for (var y = 0; y < size; y++)
            {
                var row = y * stride;
                for (var x = 0; x < size; x++)
                {
                    var hash = Hash(x, y, size);
                    if (hash > 0.55f)
                        continue;

                    var alpha = (byte)(40 + hash * 160);
                    var tone = (byte)(165 + hash * 75);
                    var offset = row + x * 4;
                    bytes[offset] = tone;
                    bytes[offset + 1] = tone;
                    bytes[offset + 2] = tone;
                    bytes[offset + 3] = alpha;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        return bmp;
    }

    private static float Hash(int x, int y, int size)
    {
        var n = x * 374761393 + y * 668265263 + size * 362437;
        n = (n ^ (n >> 13)) * 1274126177;
        return (n & 0xFFFF) / 65535f;
    }
}
