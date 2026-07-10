namespace Clipy.Themes;

public static class ThemeIds
{
    public const string Default = "default";
    public const string Kawaii = "kawaii";
    public const string Grain = "grain";
}

public sealed class ThemePalette
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Windows.UI.Color Accent { get; init; }
    public required Windows.UI.Color Background { get; init; }
    public required Windows.UI.Color Card { get; init; }
    public required Windows.UI.Color Border { get; init; }
    public required Windows.UI.Color Text { get; init; }
    public required Windows.UI.Color Muted { get; init; }
    public required Windows.UI.Color FrameStart { get; init; }
    public required Windows.UI.Color FrameMid { get; init; }
    public required Windows.UI.Color FrameEnd { get; init; }
}

public interface IMascotRenderer
{
    System.Drawing.Bitmap Render(int size, double phase);
}
