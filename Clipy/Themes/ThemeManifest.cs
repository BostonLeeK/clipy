using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json.Serialization;
using Clipy.Themes;

namespace Clipy.Themes;

public sealed class ThemeManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("description")]
    public Dictionary<string, string> Description { get; set; } = new();

    [JsonPropertyName("mascot")]
    public string Mascot { get; set; } = "";

    [JsonPropertyName("frame")]
    public string? Frame { get; set; }

    [JsonPropertyName("emptyIcon")]
    public string EmptyIcon { get; set; } = "💬";

    [JsonPropertyName("onAccent")]
    public string OnAccent { get; set; } = "#0B0B0F";

    [JsonPropertyName("home")]
    public ThemeHomeManifest? Home { get; set; }

    [JsonPropertyName("colors")]
    public ThemeColorsManifest Colors { get; set; } = new();
}

public sealed class ThemeHomeManifest
{
    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 0.45;
}

public sealed class ThemeColorsManifest
{
    [JsonPropertyName("accent")]
    public string Accent { get; set; } = "#FFFFFF";

    [JsonPropertyName("background")]
    public string Background { get; set; } = "#000000";

    [JsonPropertyName("card")]
    public string Card { get; set; } = "#111111";

    [JsonPropertyName("border")]
    public string Border { get; set; } = "#333333";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "#FFFFFF";

    [JsonPropertyName("muted")]
    public string Muted { get; set; } = "#888888";

    [JsonPropertyName("frameStart")]
    public string FrameStart { get; set; } = "#88FFFFFF";

    [JsonPropertyName("frameMid")]
    public string FrameMid { get; set; } = "#44000000";

    [JsonPropertyName("frameEnd")]
    public string FrameEnd { get; set; } = "#66000000";
}

public sealed class ThemePack
{
    public required ThemeManifest Manifest { get; init; }
    public required string RootPath { get; init; }
    public required ThemePalette Palette { get; init; }
    public required IMascotRenderer Renderer { get; init; }
    public IFrameDecorator? Frame { get; init; }

    public string Id => Manifest.Id;

    public string GetDescription(string language)
    {
        var lang = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "uk";
        if (!Manifest.Description.TryGetValue(lang, out var text) || string.IsNullOrWhiteSpace(text))
            Manifest.Description.TryGetValue("uk", out text);
        text ??= Manifest.Name;
        return string.IsNullOrWhiteSpace(Manifest.Author)
            ? text
            : $"{text} · by {Manifest.Author}";
    }

    public string? HomeBackgroundPath
    {
        get
        {
            var relative = Manifest.Home?.Background;
            if (string.IsNullOrWhiteSpace(relative)) return null;
            var path = Path.Combine(RootPath, relative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? path : null;
        }
    }

    public double HomeBackgroundOpacity => Manifest.Home?.Opacity ?? 0.45;

    public Windows.UI.Color OnAccentColor => ThemeColorParser.Parse(Manifest.OnAccent);
}

public interface IFrameDecorator
{
    Bitmap Render(int width, int height);
}
