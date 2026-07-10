using System.Text.RegularExpressions;

namespace Clipy.Helpers;

public static class OrbSummary
{
    private static readonly Regex SummaryLine = new(
        @"^Summary:\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex FirstSentence = new(
        @"^(.+?[.!?…])(?:\s+|$)",
        RegexOptions.Compiled);

    public static string AugmentPrompt(string prompt) =>
        string.IsNullOrWhiteSpace(prompt)
            ? prompt
            : prompt.TrimEnd() +
              " End your reply with a final line exactly: Summary: <one short sentence, max 15 words, same language as user>.";

    public static (string Body, string? Summary) Split(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return (markdown ?? "", null);

        var match = SummaryLine.Match(markdown);
        if (match.Success)
        {
            var summary = ForBubble(match.Groups[1].Value);
            var body = markdown[..match.Index].TrimEnd();
            body = TrimSummaryEcho(body, summary);
            if (string.IsNullOrWhiteSpace(body))
                body = summary;
            return (body, summary);
        }

        var fallback = ForBubble(ExtractLastSentence(markdown));
        var trimmedBody = TrimSummaryEcho(markdown.Trim(), fallback);
        return (trimmedBody, string.IsNullOrWhiteSpace(fallback) ? null : fallback);
    }

    public static string ForBubble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var trimmed = text.Trim().Replace('\n', ' ');
        while (trimmed.Contains("  ", StringComparison.Ordinal))
            trimmed = trimmed.Replace("  ", " ", StringComparison.Ordinal);

        var sentence = FirstSentence.Match(trimmed);
        var core = sentence.Success ? sentence.Groups[1].Value.Trim() : trimmed;
        return LimitWords(core, 15);
    }

    private static string LimitWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length <= maxWords)
            return text;
        return string.Join(' ', words.Take(maxWords)) + "…";
    }

    private static string TrimSummaryEcho(string body, string? summary)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(summary))
            return body;

        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        while (lines.Count > 0 && IsSimilarLine(lines[^1], summary))
            lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines).TrimEnd();
    }

    private static bool IsSimilarLine(string a, string b)
    {
        a = NormalizeLine(a);
        b = NormalizeLine(b);
        if (a.Length == 0 || b.Length == 0) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        if (a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static string NormalizeLine(string value) =>
        value.Trim().TrimEnd('.', '!', '?', '…');

    private static string ExtractLastSentence(string markdown)
    {
        var text = markdown
            .Replace("```", "\n", StringComparison.Ordinal)
            .Replace("**", "", StringComparison.Ordinal)
            .Replace("*", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);

        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('#') && !l.StartsWith('-') && !l.StartsWith('>'))
            .Where(l => !IsThinkingLine(l))
            .Where(l => !l.StartsWith("Summary:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (lines.Count == 0)
            return "";

        var last = lines[^1];
        if (last.Length > 140)
            last = last[..137] + "…";
        return last.Trim();
    }

    private static bool IsThinkingLine(string line)
    {
        if (line.Length > 120) return true;
        if (line.StartsWith("The user", StringComparison.OrdinalIgnoreCase)) return true;
        if (line.StartsWith("Користувач", StringComparison.OrdinalIgnoreCase)) return true;
        if (line.StartsWith("User ", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
