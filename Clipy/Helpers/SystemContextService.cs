using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Clipy.Helpers;

public sealed class ForegroundWindowInfo
{
    public required string ProcessName { get; init; }
    public required string Title { get; init; }

    public string ShortTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title)) return ProcessName;
            return Title.Length <= 42 ? Title : Title[..39] + "…";
        }
    }

    public string ToContextBlock() =>
        $"[Активне вікно]\nПроцес: {ProcessName}\nЗаголовок: {Title}";
}

public sealed class ClipboardContext
{
    public required string Kind { get; init; }
    public required string Preview { get; init; }
    public required string FullValue { get; init; }

    public string ToContextBlock() => Kind switch
    {
        "path" => $"[Буфер обміну — шлях]\n{FullValue}",
        _ => $"[Буфер обміну — текст]\n{FullValue}",
    };
}

public static class SystemContextService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static ForegroundWindowInfo? TryGetForegroundWindow()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            var title = ReadWindowTitle(hwnd);
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;

            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            if (IsClipyProcess(name)) return null;

            return new ForegroundWindowInfo
            {
                ProcessName = name,
                Title = string.IsNullOrWhiteSpace(title) ? name : title,
            };
        }
        catch
        {
            return null;
        }
    }

    public static async Task<ClipboardContext?> TryGetClipboardAsync()
    {
        try
        {
            var text = await ScreenshotHelper.GetClipboardTextAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var full = text.Trim();
                var preview = full.Length <= 36 ? full : full[..33] + "…";
                return new ClipboardContext
                {
                    Kind = "text",
                    Preview = preview.Replace('\n', ' '),
                    FullValue = full,
                };
            }

            var paths = await ScreenshotHelper.SaveClipboardFilesAsync();
            if (paths.Count > 0)
            {
                var path = paths[0];
                var preview = Path.GetFileName(path);
                if (string.IsNullOrEmpty(preview)) preview = path;
                if (preview.Length > 36) preview = preview[..33] + "…";
                return new ClipboardContext
                {
                    Kind = "path",
                    Preview = preview,
                    FullValue = path,
                };
            }
        }
        catch { /* ignore */ }

        return null;
    }

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    private static bool IsClipyProcess(string name) =>
        name.Equals("Clipy", StringComparison.OrdinalIgnoreCase);
}
