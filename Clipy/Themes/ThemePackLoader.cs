using System.Text.Json;

namespace Clipy.Themes;

public static class ThemePackLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<ThemePack> LoadAll()
    {
        var packs = new List<ThemePack>();
        foreach (var root in ResolvePackRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(dir, "theme.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var pack = Load(manifestPath);
                    if (pack is not null)
                        packs.Add(pack);
                }
                catch
                {
                    // skip invalid pack
                }
            }
        }

        return packs.Count > 0
            ? packs.OrderBy(p => p.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : CreateBuiltInFallback();
    }

    public static ThemePack? Load(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ThemeManifest>(json, JsonOpts);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
            return null;

        var root = Path.GetDirectoryName(manifestPath)!;
        var colors = manifest.Colors;
        var palette = new ThemePalette
        {
            Id = manifest.Id,
            Name = manifest.Name,
            Description = BuildDescription(manifest),
            Accent = ThemeColorParser.Parse(colors.Accent),
            Background = ThemeColorParser.Parse(colors.Background),
            Card = ThemeColorParser.Parse(colors.Card),
            Border = ThemeColorParser.Parse(colors.Border),
            Text = ThemeColorParser.Parse(colors.Text),
            Muted = ThemeColorParser.Parse(colors.Muted),
            FrameStart = ThemeColorParser.Parse(colors.FrameStart),
            FrameMid = ThemeColorParser.Parse(colors.FrameMid),
            FrameEnd = ThemeColorParser.Parse(colors.FrameEnd),
        };

        return new ThemePack
        {
            Manifest = manifest,
            RootPath = root,
            Palette = palette,
            Renderer = ThemeComponentRegistry.CreateMascot(manifest.Mascot),
            Frame = ThemeComponentRegistry.CreateFrame(manifest.Frame),
        };
    }

    private static IEnumerable<string> ResolvePackRoots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Themes", "Packs");
        yield return Path.Combine(AppContext.BaseDirectory, "Packs");
    }

    private static string BuildDescription(ThemeManifest manifest)
    {
        if (manifest.Description.TryGetValue("uk", out var uk) && !string.IsNullOrWhiteSpace(uk))
            return string.IsNullOrWhiteSpace(manifest.Author) ? uk : $"{uk} · by {manifest.Author}";
        if (manifest.Description.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
            return string.IsNullOrWhiteSpace(manifest.Author) ? en : $"{en} · by {manifest.Author}";
        return manifest.Name;
    }

    private static IReadOnlyList<ThemePack> CreateBuiltInFallback()
    {
        var devRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Themes", "Packs"));
        if (!Directory.Exists(devRoot))
            devRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Themes", "Packs"));
        if (!Directory.Exists(devRoot))
            throw new InvalidOperationException("No theme packs found.");

        return Directory.EnumerateDirectories(devRoot)
            .Select(dir => Load(Path.Combine(dir, "theme.json")))
            .Where(p => p is not null)
            .Cast<ThemePack>()
            .ToList();
    }
}
