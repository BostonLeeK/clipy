namespace Clipy.Services.Agents;

internal static class AgentAttachments
{
    public static IReadOnlyList<string> Prepare(IReadOnlyList<string>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return Array.Empty<string>();

        var inbox = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipyAssistant", "inbox");
        try { Directory.CreateDirectory(inbox); } catch { }

        var result = new List<string>();
        foreach (var path in attachments)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!File.Exists(path) && !Directory.Exists(path)) continue;

            if (Directory.Exists(path))
            {
                result.Add(Path.GetFullPath(path));
                continue;
            }

            try
            {
                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext)) ext = ".bin";
                var dest = Path.Combine(inbox, $"attach-{DateTime.Now:yyyyMMdd-HHmmss-fff}{ext}");
                File.Copy(path, dest, overwrite: true);
                result.Add(dest);
            }
            catch
            {
                result.Add(Path.GetFullPath(path));
            }
        }

        return result;
    }

    public static string BuildPrompt(string prompt, IReadOnlyList<string>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return Collapse(prompt);

        var parts = new List<string>();
        var images = attachments.Where(IsImage).ToList();
        var others = attachments.Where(p => !IsImage(p)).ToList();

        if (images.Count > 0)
        {
            parts.Add("IMPORTANT: An image file is attached to this message. Open/read ONLY these exact file paths before answering. Do not use other images from chat history.");
            foreach (var path in images)
                parts.Add($"IMAGE_FILE={path}");
        }

        if (others.Count > 0)
        {
            parts.Add("Attached files/folders:");
            foreach (var path in others)
                parts.Add($"FILE={path}");
        }

        var user = string.IsNullOrWhiteSpace(prompt) ? "Analyze the attached files." : prompt.Trim();
        parts.Add("USER_REQUEST: " + Collapse(user));
        return string.Join(" ", parts);
    }

    public static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    private static string Collapse(string text) =>
        string.Join(" ", text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
