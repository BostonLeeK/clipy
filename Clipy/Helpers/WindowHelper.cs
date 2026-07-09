using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace Clipy.Helpers;

public static class WindowHelper
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_BORDER = 0x00800000;
    private const int WS_DLGFRAME = 0x00400000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_VISIBLE_FRAME_BORDER_THICKNESS = 36;
    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMNCRP_DISABLED = 1;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    public const int OrbSize = 60;
    public const int PanelWidth = 380;
    public const int PanelHeight = 600;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref NativeRect pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern bool SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    private const uint SPI_GETWORKAREA = 0x0030;

    public static AppWindow GetAppWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(id);
    }

    public static void ConfigureWidget(Window window, AppWindow appWindow)
    {
        if (appWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(false, false);
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }

        appWindow.IsShownInSwitchers = false;

        var hwnd = WindowNative.GetWindowHandle(window);
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (style | WS_EX_TOOLWINDOW | WS_EX_TOPMOST) & ~WS_EX_LAYERED);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    public static void HideWindow(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        ShowWindow(hwnd, 0);
    }

    public static void ShowWindowTopmost(Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        ShowWindow(hwnd, 5);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    public static void ApplyPanelChrome(Window window, AppWindow appWindow)
    {
        window.SystemBackdrop = null;

        var hwnd = WindowNative.GetWindowHandle(window);
        if (appWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.SetBorderAndTitleBar(false, false);
        }

        var style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~(WS_BORDER | WS_DLGFRAME | WS_THICKFRAME | WS_CAPTION);
        SetWindowLong(hwnd, GWL_STYLE, style);

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle | WS_EX_TOOLWINDOW | WS_EX_TOPMOST) & ~WS_EX_LAYERED);

        var ncPolicy = DWMNCRP_DISABLED;
        DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref ncPolicy, sizeof(int));

        var corner = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        var borderColor = DwmColorNone;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

        var captionColor = DwmColorNone;
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

        var borderThickness = 0;
        DwmSetWindowAttribute(hwnd, DWMWA_VISIBLE_FRAME_BORDER_THICKNESS, ref borderThickness, sizeof(int));

        var margins = new Margins { Left = 0, Right = 0, Top = 0, Bottom = 0 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        var radius = 40;
        var rgn = CreateRoundRectRgn(0, 0, PanelWidth + 1, PanelHeight + 1, radius, radius);
        SetWindowRgn(hwnd, rgn, true);

        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    public static RectInt32 GetWorkArea()
    {
        var rect = new NativeRect();
        SystemParametersInfo(SPI_GETWORKAREA, 0, ref rect, 0);
        return new RectInt32(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public static PointInt32 DefaultOrbPosition()
    {
        var wa = GetWorkArea();
        return new PointInt32(
            wa.X + wa.Width - OrbSize - 24,
            wa.Y + wa.Height - OrbSize - 56);
    }

    public static PointInt32 Clamp(PointInt32 pos, int w, int h)
    {
        var wa = GetWorkArea();
        var x = Math.Clamp(pos.X, wa.X, wa.X + wa.Width - w);
        var y = Math.Clamp(pos.Y, wa.Y, wa.Y + wa.Height - h);
        return new PointInt32(x, y);
    }

    public static PointInt32 BottomRight(PointInt32 topLeft, int width, int height) =>
        new(topLeft.X + width, topLeft.Y + height);

    public static PointInt32 TopLeftFromAnchor(PointInt32 anchor, int width, int height) =>
        Clamp(new PointInt32(anchor.X - width, anchor.Y - height), width, height);

    public static PointInt32 PanelFromOrb(PointInt32 orb) =>
        TopLeftFromAnchor(BottomRight(orb, OrbSize, OrbSize), PanelWidth, PanelHeight);

    public static PointInt32 OrbFromPanel(PointInt32 panel) =>
        TopLeftFromAnchor(BottomRight(panel, PanelWidth, PanelHeight), OrbSize, OrbSize);

    public static bool IsOnScreen(PointInt32 pos, int w, int h)
    {
        var wa = GetWorkArea();
        return pos.X < wa.X + wa.Width && pos.X + w > wa.X
            && pos.Y < wa.Y + wa.Height && pos.Y + h > wa.Y;
    }
}
