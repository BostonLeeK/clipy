using System.Text.RegularExpressions;

namespace Clipy.Helpers;

public static class OrbSummary
{
    private static readonly Regex SummaryLine = new(
        @"^Summary:\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

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
            var summary = CleanSummary(match.Groups[1].Value);
            var body = markdown[..match.Index].TrimEnd();
            if (string.IsNullOrWhiteSpace(body))
                body = summary;
            return (body, summary);
        }

        var fallback = ExtractLastSentence(markdown);
        return (markdown.Trim(), string.IsNullOrWhiteSpace(fallback) ? null : fallback);
    }

    private static string CleanSummary(string value)
    {
        var text = value.Trim().TrimEnd('.', '!', '?', '…');
        if (text.Length > 120)
            text = text[..117] + "…";
        return text;
    }

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
            .ToList();

        if (lines.Count == 0)
            return "";

        var last = lines[^1];
        if (last.Length > 140)
            last = last[..137] + "…";
        return CleanSummary(last);
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
