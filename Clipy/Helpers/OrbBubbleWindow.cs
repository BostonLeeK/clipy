using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Clipy.Themes;
using Windows.Graphics;

namespace Clipy.Helpers;

public sealed class OrbBubbleWindow : IDisposable
{
    private const string ClassName = "ClipyOrbBubble";
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopmost = 0x00000008;
    private const int WmLButtonUp = 0x0202;
    private const int WmTimer = 0x0113;
    private const int WmDestroy = 0x0002;
    private const int WmSetCursor = 0x0020;
    private const int HtClient = 1;
    private const int IdcArrow = 32512;
    private const int IdcHand = 32649;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private const int HideTimerId = 2;
    private const int MaxWidth = 280;
    private const int PadX = 14;
    private const int PadY = 11;
    private const int AccentBar = 4;
    private const int Tail = 11;
    private const int Gap = 8;
    private const int ShadowPad = 6;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static bool _classRegistered;
    private static WndProc? _wndProcKeepAlive;
    private static IntPtr _arrowCursor;
    private static IntPtr _handCursor;

    private IntPtr _hwnd;
    private bool _disposed;
    private string _text = "";
    private ThemePalette? _theme;
    private int _width;
    private int _height;
    private bool _tailPointsDown = true;
    private GCHandle _selfHandle;

    public event Action? Dismissed;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
        IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BlendFunction pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint CbSize;
        public uint Style;
        public IntPtr LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public IntPtr HInstance;
        public IntPtr HIcon;
        public IntPtr HCursor;
        public IntPtr HbrBackground;
        public string? LpszMenuName;
        public string LpszClassName;
        public IntPtr HIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    public void Show(string text, PointInt32 orbPos, int orbSize, ThemePalette theme, int autoHideMs = 12000)
    {
        _text = text.Trim();
        _theme = theme;
        Measure();
        EnsureWindow();
        MoveNearOrb(orbPos, orbSize);
        Present();
        ShowWindow(_hwnd, 5);
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, 0x0001 | 0x0002);
        KillTimer(_hwnd, new IntPtr(HideTimerId));
        SetTimer(_hwnd, new IntPtr(HideTimerId), (uint)autoHideMs, IntPtr.Zero);
    }

    public void MoveNearOrb(PointInt32 orbPos, int orbSize)
    {
        if (_hwnd == IntPtr.Zero) return;
        var x = orbPos.X + (orbSize - _width) / 2;
        var y = orbPos.Y - _height - Gap;
        _tailPointsDown = true;
        if (y < 8)
        {
            y = orbPos.Y + orbSize + Gap;
            _tailPointsDown = false;
        }
        var clamped = WindowHelper.Clamp(new PointInt32(x, y), _width, _height);
        SetWindowPos(_hwnd, HwndTopmost, clamped.X, clamped.Y, _width, _height, 0);
        Present();
    }

    public void Hide()
    {
        if (_hwnd == IntPtr.Zero) return;
        KillTimer(_hwnd, new IntPtr(HideTimerId));
        ShowWindow(_hwnd, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            KillTimer(_hwnd, new IntPtr(HideTimerId));
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    private void EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero) return;
        EnsureClass();
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = CreateWindowEx(
            WsExLayered | WsExToolWindow | WsExTopmost,
            ClassName,
            "Clipy Bubble",
            WsPopup,
            0, 0, _width, _height,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create orb bubble window.");
        SetWindowLongPtr(_hwnd, -21, GCHandle.ToIntPtr(_selfHandle));
    }

    private void Measure()
    {
        using var probe = new Bitmap(1, 1);
        using var g = Graphics.FromImage(probe);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        var layout = new SizeF(MaxWidth - PadX * 2 - AccentBar - 4, 1000);
        var size = g.MeasureString(_text, font, layout);
        _width = Math.Clamp((int)Math.Ceiling(size.Width) + PadX * 2 + AccentBar + 4, 96, MaxWidth) + ShadowPad * 2;
        _height = (int)Math.Ceiling(size.Height) + PadY * 2 + Tail + ShadowPad * 2;
    }

    private void Present()
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            lock (GdiRenderLock.Sync)
            {
                using var bitmap = RenderBubble();
                PresentBitmap(_hwnd, bitmap);
            }
        }
        catch
        {
        }
    }

    private Bitmap RenderBubble()
    {
        if (_theme is null)
            return new Bitmap(1, 1);

        var light = IsLightStyle(_theme);
        var accent = ToColor(_theme.Accent);
        var textColor = light ? Color.FromArgb(255, 26, 26, 26) : ToColor(_theme.Text);
        var muted = light ? Color.FromArgb(200, 70, 70, 70) : ToColor(_theme.Muted);
        var fillTop = light ? Color.FromArgb(248, 255, 255, 255) : Color.FromArgb(238, _theme.Card.R, _theme.Card.G, _theme.Card.B);
        var fillBottom = light ? Color.FromArgb(235, 240, 240, 242) : Color.FromArgb(228, Blend(_theme.Background.R, _theme.Card.R, 0.35f), Blend(_theme.Background.G, _theme.Card.G, 0.35f), Blend(_theme.Background.B, _theme.Card.B, 0.35f));
        var border = light ? Color.FromArgb(200, 200, 200, 205) : Color.FromArgb(170, _theme.Border.R, _theme.Border.G, _theme.Border.B);
        var glow = Color.FromArgb(light ? 90 : 110, accent);

        var bmp = new Bitmap(_width, _height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);

        var ox = ShadowPad;
        var oy = ShadowPad;
        var bubbleW = _width - ShadowPad * 2;
        var bubbleH = _height - ShadowPad * 2;
        var tailCx = ox + bubbleW / 2f;

        DrawShadow(g, ox, oy, bubbleW, bubbleH, _tailPointsDown);

        using var bubblePath = BuildBubblePath(ox, oy, bubbleW, bubbleH, tailCx, _tailPointsDown);
        using (var fillBrush = new LinearGradientBrush(
                   new PointF(ox, oy),
                   new PointF(ox, oy + bubbleH),
                   fillTop,
                   fillBottom))
        {
            g.FillPath(fillBrush, bubblePath);
        }

        using (var gloss = new GraphicsPath())
        {
            gloss.AddEllipse(ox + bubbleW * 0.12f, oy + 4, bubbleW * 0.76f, bubbleH * 0.28f);
            using var glossBrush = new PathGradientBrush(gloss)
            {
                CenterColor = Color.FromArgb(light ? 55 : 38, 255, 255, 255),
                SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) },
            };
            g.SetClip(bubblePath);
            g.FillPath(glossBrush, gloss);
            g.ResetClip();
        }

        using (var glowPen = new Pen(glow, 2.2f))
            g.DrawPath(glowPen, bubblePath);
        using (var borderPen = new Pen(border, 1f))
            g.DrawPath(borderPen, bubblePath);

        var barTop = oy + (_tailPointsDown ? 10 : 10 + Tail);
        var barBottom = oy + bubbleH - (_tailPointsDown ? 10 + Tail : 10);
        using var accentBrush = new SolidBrush(Color.FromArgb(255, accent));
        g.FillRectangle(accentBrush, ox + 8, barTop, AccentBar, barBottom - barTop);

        using var font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Point);
        var textTop = oy + (_tailPointsDown ? PadY : PadY + Tail);
        var textRect = new RectangleF(
            ox + PadX + AccentBar,
            textTop,
            bubbleW - PadX * 2 - AccentBar,
            bubbleH - PadY * 2 - Tail);
        using var textBrush = new SolidBrush(textColor);
        g.DrawString(_text, font, textBrush, textRect);

        using var dotBrush = new SolidBrush(Color.FromArgb(180, muted));
        g.FillEllipse(dotBrush, ox + bubbleW - 16, textTop + 2, 5, 5);

        return bmp;
    }

    private static void DrawShadow(Graphics g, int ox, int oy, int w, int h, bool tailDown)
    {
        var layers = new (int dx, int dy, int alpha, float expand)[]
        {
            (0, 3, 28, 2f),
            (0, 6, 18, 4f),
            (0, 9, 10, 6f),
        };
        foreach (var (dx, dy, alpha, expand) in layers)
        {
            var top = oy + (tailDown ? 0 : Tail) - expand / 2;
            var height = h - Tail - expand;
            using var path = RoundedRect(new RectangleF(ox + dx - expand / 2, top + dy, w + expand, height), 14 + expand / 2);
            using var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
            g.FillPath(brush, path);
        }
    }

    private static GraphicsPath BuildBubblePath(int ox, int oy, int w, int h, float tailCx, bool tailDown)
    {
        var r = 14f;
        var tailW = 9f;
        var path = new GraphicsPath();

        if (tailDown)
        {
            var body = new RectangleF(ox, oy, w, h - Tail);
            path.AddArc(body.Right - r * 2, body.Top, r * 2, r * 2, 270, 90);
            path.AddArc(body.Right - r * 2, body.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddLine(tailCx + tailW, body.Bottom, tailCx, oy + h);
            path.AddLine(tailCx - tailW, body.Bottom, body.Left + r, body.Bottom);
            path.AddArc(body.Left, body.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.AddArc(body.Left, body.Top, r * 2, r * 2, 180, 90);
            path.CloseFigure();
            return path;
        }

        var bodyDown = new RectangleF(ox, oy + Tail, w, h - Tail);
        path.AddArc(bodyDown.Right - r * 2, bodyDown.Top, r * 2, r * 2, 270, 90);
        path.AddArc(bodyDown.Right - r * 2, bodyDown.Bottom - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(bodyDown.Left, bodyDown.Bottom - r * 2, r * 2, r * 2, 90, 90);
        path.AddArc(bodyDown.Left, bodyDown.Top, r * 2, r * 2, 180, 90);
        path.AddLine(tailCx - tailW, bodyDown.Top, tailCx, oy);
        path.AddLine(tailCx + tailW, bodyDown.Top, bodyDown.Right - r, bodyDown.Top);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d > bounds.Width) d = bounds.Width;
        if (d > bounds.Height) d = bounds.Height;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static bool IsLightStyle(ThemePalette theme) =>
        string.Equals(theme.Id, ThemeIds.Grain, StringComparison.OrdinalIgnoreCase);

    private static Color ToColor(Windows.UI.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static byte Blend(byte a, byte b, float t) => (byte)(a + (b - a) * t);

    private static void PresentBitmap(IntPtr hwnd, Bitmap bitmap)
    {
        if (!GetWindowRect(hwnd, out var windowRect))
            return;

        var width = bitmap.Width;
        var height = bitmap.Height;
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
            Compression = 0,
        };

        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var dib = CreateDIBSection(screenDc, ref header, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(memDc, dib);

        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppPArgb);
        try
        {
            var byteCount = Math.Abs(data.Stride) * height;
            var buffer = new byte[byteCount];
            Marshal.Copy(data.Scan0, buffer, 0, byteCount);
            Marshal.Copy(buffer, 0, bits, width * height * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        var dst = new Point { X = windowRect.Left, Y = windowRect.Top };
        var src = new Point();
        var size = new Size { Cx = width, Cy = height };
        var blend = new BlendFunction
        {
            BlendOp = AcSrcOver,
            SourceConstantAlpha = 255,
            AlphaFormat = AcSrcAlpha,
        };

        UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, UlwAlpha);

        SelectObject(memDc, old);
        DeleteObject(dib);
        DeleteDC(memDc);
    }

    private static void EnsureClass()
    {
        if (_classRegistered) return;
        _wndProcKeepAlive = StaticWndProc;
        _arrowCursor = LoadCursor(IntPtr.Zero, IdcArrow);
        _handCursor = LoadCursor(IntPtr.Zero, IdcHand);
        var wc = new WndClassEx
        {
            CbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive),
            HInstance = GetModuleHandle(null),
            LpszClassName = ClassName,
            HCursor = _arrowCursor,
        };
        RegisterClassEx(ref wc);
        _classRegistered = true;
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var ptr = GetWindowLongPtr(hWnd, -21);
        if (ptr != IntPtr.Zero)
        {
            var handle = GCHandle.FromIntPtr(ptr);
            if (handle.Target is OrbBubbleWindow bubble)
                return bubble.InstanceWndProc(hWnd, msg, wParam, lParam);
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr InstanceWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmSetCursor:
                if ((int)(lParam.ToInt64() & 0xFFFF) == HtClient)
                {
                    SetCursor(_handCursor);
                    return IntPtr.Zero;
                }
                break;
            case WmTimer:
                if (wParam.ToInt32() == HideTimerId)
                {
                    Hide();
                    Dismissed?.Invoke();
                }
                return IntPtr.Zero;
            case WmLButtonUp:
                Hide();
                Dismissed?.Invoke();
                return IntPtr.Zero;
            case WmDestroy:
                KillTimer(hWnd, new IntPtr(HideTimerId));
                return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
