using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Clipy.Themes;
using Windows.Graphics;

namespace Clipy.Helpers;

public sealed class NativeOrbWindow : IDisposable
{
    private const string ClassName = "ClipyNativeOrb";
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopmost = 0x00000008;
    private const int WmDestroy = 0x0002;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmMouseMove = 0x0200;
    private const int WmTimer = 0x0113;
    private const int WmNcDestroy = 0x0082;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private const int TimerId = 1;
    private const int DragThreshold = 4;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static bool _classRegistered;
    private static WndProc? _wndProcKeepAlive;

    private readonly int _size;
    private IMascotRenderer _renderer;
    private IntPtr _hwnd;
    private bool _disposed;
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragScreenStart;
    private Point _dragWindowStart;
    private double _phase;
    private double _phaseStep = 0.18;
    private MascotState _mascotState = MascotState.Idle;
    private GCHandle _selfHandle;

    public event Action? Clicked;
    public event Action<PointInt32>? Moved;

    public void SetMascotState(MascotState state)
    {
        _mascotState = state;
        _phaseStep = state switch
        {
            MascotState.Thinking => 0.34,
            MascotState.Success => 0.26,
            MascotState.Error => 0.10,
            _ => 0.18,
        };
    }

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
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
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
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
        IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BlendFunction pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

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

    public NativeOrbWindow(int size, PointInt32 position, IMascotRenderer renderer)
    {
        _size = size;
        _renderer = renderer;
        EnsureClass();
        _selfHandle = GCHandle.Alloc(this);

        _hwnd = CreateWindowEx(
            WsExLayered | WsExToolWindow | WsExTopmost,
            ClassName,
            "Clipy Orb",
            WsPopup,
            position.X,
            position.Y,
            size,
            size,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create orb window.");

        SetWindowLongPtr(_hwnd, -21, GCHandle.ToIntPtr(_selfHandle));
        Present();
        SetWindowPos(_hwnd, HwndTopmost, position.X, position.Y, size, size, 0);
        ShowWindow(_hwnd, 5);
        SetTimer(_hwnd, new IntPtr(TimerId), 33, IntPtr.Zero);
    }

    public IntPtr Handle => _hwnd;

    public PointInt32 Position
    {
        get
        {
            GetWindowRect(_hwnd, out var r);
            return new PointInt32(r.Left, r.Top);
        }
    }

    public void SetRenderer(IMascotRenderer renderer)
    {
        _renderer = renderer;
        Present();
    }

    public void Show()
    {
        ShowWindow(_hwnd, 5);
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, 0x0001 | 0x0002);
        Present();
    }

    public void Hide()
    {
        ShowWindow(_hwnd, 0);
    }

    public void Move(PointInt32 pos)
    {
        SetWindowPos(_hwnd, HwndTopmost, pos.X, pos.Y, _size, _size, 0);
        Present();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
        {
            KillTimer(_hwnd, new IntPtr(TimerId));
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
    }

    private static void EnsureClass()
    {
        if (_classRegistered) return;
        _wndProcKeepAlive = StaticWndProc;
        var wc = new WndClassEx
        {
            CbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeepAlive),
            HInstance = GetModuleHandle(null),
            LpszClassName = ClassName,
            HCursor = IntPtr.Zero,
        };
        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 1410)
                throw new InvalidOperationException($"RegisterClassEx failed: {err}");
        }
        _classRegistered = true;
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var ptr = GetWindowLongPtr(hWnd, -21);
        if (ptr != IntPtr.Zero)
        {
            var handle = GCHandle.FromIntPtr(ptr);
            if (handle.Target is NativeOrbWindow orb)
                return orb.InstanceWndProc(hWnd, msg, wParam, lParam);
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr InstanceWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmTimer:
                if (wParam.ToInt32() == TimerId)
                {
                    _phase += _phaseStep;
                    Present();
                }
                return IntPtr.Zero;

            case WmLButtonDown:
                GetCursorPos(out _dragScreenStart);
                GetWindowRect(hWnd, out var r);
                _dragWindowStart = new Point { X = r.Left, Y = r.Top };
                _dragging = true;
                _dragMoved = false;
                return IntPtr.Zero;

            case WmMouseMove:
                if (!_dragging) return IntPtr.Zero;
                GetCursorPos(out var cursor);
                var dx = cursor.X - _dragScreenStart.X;
                var dy = cursor.Y - _dragScreenStart.Y;
                if (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold)
                    _dragMoved = true;
                var next = WindowHelper.Clamp(
                    new PointInt32(_dragWindowStart.X + dx, _dragWindowStart.Y + dy),
                    _size, _size);
                SetWindowPos(hWnd, HwndTopmost, next.X, next.Y, _size, _size, 0);
                Present();
                return IntPtr.Zero;

            case WmLButtonUp:
                if (!_dragging) return IntPtr.Zero;
                _dragging = false;
                if (_dragMoved)
                    Moved?.Invoke(Position);
                else
                    Clicked?.Invoke();
                return IntPtr.Zero;

            case WmDestroy:
                KillTimer(hWnd, new IntPtr(TimerId));
                return IntPtr.Zero;

            case WmNcDestroy:
                return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void Present()
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            lock (GdiRenderLock.Sync)
            {
                using var bitmap = _renderer.Render(_size, _phase);
                PresentBitmap(_hwnd, bitmap);
            }
        }
        catch
        {
        }
    }

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
        var src = new Point { X = 0, Y = 0 };
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
        ReleaseDC(IntPtr.Zero, screenDc);
    }
}
