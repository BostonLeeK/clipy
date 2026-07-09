using System.Runtime.InteropServices;

namespace Clipy.Helpers;

public sealed class HotkeyService : IDisposable
{
    public const int HotkeyId = 0xC11A;
    private const int WmHotkey = 0x0312;
    private const int GwlpUserData = -21;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNorepeat = 0x4000;
    private const uint VkC = 0x43;
    private const int WsExToolWindow = 0x00000080;
    private const int WsPopup = unchecked((int)0x80000000);
    private const string ClassName = "ClipyHotkeyHost";
    private static readonly IntPtr HwndMessage = new(-3);

    private static bool _classRegistered;
    private static WndProcDelegate? _wndProcKeepAlive;

    private IntPtr _hwnd;
    private bool _registered;
    private bool _disposed;
    private GCHandle _selfHandle;

    public event Action? Triggered;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    public void Register()
    {
        EnsureClass();
        _selfHandle = GCHandle.Alloc(this);
        _hwnd = CreateWindowEx(
            WsExToolWindow,
            ClassName,
            "Clipy Hotkey",
            WsPopup,
            0, 0, 0, 0,
            HwndMessage,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

        SetWindowLongPtr(_hwnd, GwlpUserData, GCHandle.ToIntPtr(_selfHandle));
        _registered = RegisterHotKey(_hwnd, HotkeyId, ModControl | ModShift | ModNorepeat, VkC);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registered && _hwnd != IntPtr.Zero)
            UnregisterHotKey(_hwnd, HotkeyId);
        if (_hwnd != IntPtr.Zero)
        {
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
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            var ptr = GetWindowLongPtr(hWnd, GwlpUserData);
            if (ptr != IntPtr.Zero)
            {
                var handle = GCHandle.FromIntPtr(ptr);
                if (handle.Target is HotkeyService svc)
                    svc.Triggered?.Invoke();
            }
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }
}
