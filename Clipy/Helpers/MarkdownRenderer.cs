using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Clipy.Helpers;

public static class MarkdownRenderer
{
    public static void SetMarkdown(RichTextBlock target, string markdown, Brush foreground, Brush muted, Brush accent)
    {
        target.Blocks.Clear();
        if (string.IsNullOrWhiteSpace(markdown))
            return;

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var code = new System.Text.StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal))
                {
                    if (code.Length > 0) code.Append('\n');
                    code.Append(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++;
                target.Blocks.Add(MakeCodeBlock(code.ToString(), muted, foreground));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (TryHeader(line, out var level, out var headerText))
            {
                target.Blocks.Add(MakeHeader(headerText, level, foreground, accent));
                i++;
                continue;
            }

            if (TryBullet(line, out var bulletText))
            {
                var items = new List<string> { bulletText };
                i++;
                while (i < lines.Length && TryBullet(lines[i], out var next))
                {
                    items.Add(next);
                    i++;
                }
                foreach (var item in items)
                    target.Blocks.Add(MakeParagraph("• " + item, foreground, muted, accent));
                continue;
            }

            if (TryNumbered(line, out var num, out var numberedText))
            {
                var items = new List<(string Num, string Text)> { (num, numberedText) };
                i++;
                while (i < lines.Length && TryNumbered(lines[i], out var n2, out var t2))
                {
                    items.Add((n2, t2));
                    i++;
                }
                foreach (var item in items)
                    target.Blocks.Add(MakeParagraph($"{item.Num}. {item.Text}", foreground, muted, accent));
                continue;
            }

            if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("***", StringComparison.Ordinal))
            {
                var rule = new Paragraph { Margin = new Thickness(0, 6, 0, 6) };
                rule.Inlines.Add(new Run
                {
                    Text = "────────",
                    Foreground = muted,
                    FontSize = 11,
                });
                target.Blocks.Add(rule);
                i++;
                continue;
            }

            var para = MakeParagraph(line, foreground, muted, accent);
            target.Blocks.Add(para);
            i++;
        }

        if (target.Blocks.Count == 0)
            target.Blocks.Add(MakeParagraph(markdown.Trim(), foreground, muted, accent));
    }

    private static bool TryHeader(string line, out int level, out string text)
    {
        level = 0;
        text = "";
        if (!line.StartsWith('#') || line.Length < 2) return false;
        while (level < line.Length && line[level] == '#') level++;
        if (level is < 1 or > 3) return false;
        if (level >= line.Length || line[level] != ' ') return false;
        text = line[(level + 1)..].Trim();
        return text.Length > 0;
    }

    private static bool TryBullet(string line, out string text)
    {
        text = "";
        var t = line.TrimStart();
        if (t.Length < 2) return false;
        if ((t[0] is '-' or '*' or '•') && t[1] == ' ')
        {
            text = t[2..].Trim();
            return text.Length > 0;
        }
        return false;
    }

    private static bool TryNumbered(string line, out string num, out string text)
    {
        num = "";
        text = "";
        var t = line.TrimStart();
        var i = 0;
        while (i < t.Length && char.IsDigit(t[i])) i++;
        if (i == 0 || i >= t.Length || t[i] != '.') return false;
        if (i + 1 >= t.Length || t[i + 1] != ' ') return false;
        num = t[..i];
        text = t[(i + 2)..].Trim();
        return text.Length > 0;
    }

    private static Paragraph MakeHeader(string text, int level, Brush foreground, Brush accent)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(0, level == 1 ? 8 : 6, 0, 4),
        };
        var run = new Run
        {
            Text = StripInlineMarkers(text),
            Foreground = accent,
            FontWeight = FontWeights.SemiBold,
            FontSize = level switch { 1 => 16, 2 => 14.5, _ => 13.5 },
        };
        p.Inlines.Add(run);
        return p;
    }

    private static Paragraph MakeCodeBlock(string code, Brush muted, Brush foreground)
    {
        var p = new Paragraph
        {
            Margin = new Thickness(0, 6, 0, 6),
        };
        p.Inlines.Add(new Run
        {
            Text = code,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = muted,
        });
        return p;
    }

    private static Paragraph MakeParagraph(string text, Brush foreground, Brush muted, Brush accent)
    {
        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
        AppendInlines(p.Inlines, text, foreground, muted, accent);
        return p;
    }

    private static void AppendInlines(InlineCollection inlines, string text, Brush foreground, Brush muted, Brush accent)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (TryTake(text, i, "**", out var bold, out var next)
                || TryTake(text, i, "__", out bold, out next))
            {
                inlines.Add(new Run
                {
                    Text = bold,
                    FontWeight = FontWeights.Bold,
                    Foreground = foreground,
                });
                i = next;
                continue;
            }

            if (TryTake(text, i, "*", out var italic, out next)
                || TryTake(text, i, "_", out italic, out next))
            {
                inlines.Add(new Run
                {
                    Text = italic,
                    FontStyle = FontStyle.Italic,
                    Foreground = foreground,
                });
                i = next;
                continue;
            }

            if (TryTake(text, i, "`", out var code, out next))
            {
                inlines.Add(new Run
                {
                    Text = code,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    Foreground = accent,
                });
                i = next;
                continue;
            }

            var end = FindNextMarker(text, i);
            inlines.Add(new Run
            {
                Text = text[i..end],
                Foreground = foreground,
            });
            i = end;
        }
    }

    private static bool TryTake(string text, int start, string marker, out string content, out int next)
    {
        content = "";
        next = start;
        if (start + marker.Length * 2 > text.Length) return false;
        if (!text.AsSpan(start).StartsWith(marker, StringComparison.Ordinal)) return false;
        if (marker is "*" or "_" && start + 1 < text.Length && text[start + 1] == marker[0])
            return false;

        var close = text.IndexOf(marker, start + marker.Length, StringComparison.Ordinal);
        if (close < 0) return false;
        content = text[(start + marker.Length)..close];
        if (content.Length == 0 || content.Contains('\n')) return false;
        next = close + marker.Length;
        return true;
    }

    private static int FindNextMarker(string text, int start)
    {
        var best = text.Length;
        foreach (var m in new[] { "**", "__", "`", "*", "_" })
        {
            var idx = text.IndexOf(m, start, StringComparison.Ordinal);
            if (idx >= 0 && idx < best) best = idx;
        }
        return best;
    }

    private static string StripInlineMarkers(string text)
    {
        return text
            .Replace("**", "", StringComparison.Ordinal)
            .Replace("__", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);
    }
}
