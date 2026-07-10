using System.Runtime.InteropServices;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;

namespace Clipy.Helpers;

public sealed class NativeTrayMenuService
{
    private const int SwShowNa = 8;
    private const uint WmNull = 0x0000;

    private readonly DispatcherQueue _queue;
    private readonly Func<nint> _hwndProvider;
    private readonly List<(string Text, Action Action, bool Separator)> _entries = new();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    public NativeTrayMenuService(DispatcherQueue queue, Func<nint> hwndProvider)
    {
        _queue = queue;
        _hwndProvider = hwndProvider;
    }

    public void Clear() => _entries.Clear();

    public void AddItem(string text, Action action) =>
        _entries.Add((text, action, false));

    public void AddSeparator() =>
        _entries.Add((string.Empty, () => { }, true));

    public void Show()
    {
        var hwnd = _hwndProvider();
        if (hwnd == nint.Zero) return;

        var menu = new PopupMenu();
        foreach (var (text, action, separator) in _entries)
        {
            if (separator)
            {
                menu.Items.Add(new PopupMenuItem("-", (_, _) => { }));
                continue;
            }

            menu.Items.Add(new PopupMenuItem(text, (_, _) =>
                _queue.TryEnqueue(() => action())));
        }

        ShowWindow(hwnd, SwShowNa);
        SetForegroundWindow(hwnd);
        WindowHelper.GetCursorPos(out var cursor);
        menu.Show(hwnd, cursor.X, cursor.Y);
        PostMessage(hwnd, WmNull, nint.Zero, nint.Zero);
    }
}
