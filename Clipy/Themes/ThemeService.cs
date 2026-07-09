using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Clipy.Themes;

public sealed class ThemeService
{
    private readonly Dictionary<string, ThemePalette> _palettes;
    private readonly Dictionary<string, IMascotRenderer> _renderers;

    public ThemeService()
    {
        _palettes = new Dictionary<string, ThemePalette>(StringComparer.OrdinalIgnoreCase)
        {
            [ThemeIds.Default] = new ThemePalette
            {
                Id = ThemeIds.Default,
                Name = "Clipy Neon",
                Description = "Темна тема з лаймовим акцентом",
                Accent = Color(0xC8, 0xFF, 0x4D),
                Background = Color(0x0C, 0x0C, 0x12),
                Card = Color(0x1A, 0x1A, 0x22),
                Border = Color(0x2A, 0x2A, 0x35),
                Text = Color(0xF0, 0xF0, 0xF5),
                Muted = Color(0x8A, 0x8A, 0x9A),
                FrameStart = Color(0xA8, 0xC8, 0xFF, 0x4D),
                FrameMid = Color(0x33, 0x2A, 0x2A, 0x35),
                FrameEnd = Color(0x88, 0xA8, 0xC8, 0xFF),
            },
            [ThemeIds.Kawaii] = new ThemePalette
            {
                Id = ThemeIds.Kawaii,
                Name = "Totoro Forest",
                Description = "Сірий лісовий маскот у стилі Тоторо",
                Accent = Color(0x8F, 0xC0, 0x6E),
                Background = Color(0x14, 0x18, 0x14),
                Card = Color(0x22, 0x28, 0x22),
                Border = Color(0x3A, 0x46, 0x38),
                Text = Color(0xEE, 0xF2, 0xE8),
                Muted = Color(0xA0, 0xB0, 0x98),
                FrameStart = Color(0xCC, 0x8F, 0xC0, 0x6E),
                FrameMid = Color(0x55, 0xC8, 0xB8, 0x88),
                FrameEnd = Color(0xAA, 0x7A, 0x9A, 0xC8),
            },
        };

        _renderers = new Dictionary<string, IMascotRenderer>(StringComparer.OrdinalIgnoreCase)
        {
            [ThemeIds.Default] = new DefaultMascotRenderer(),
            [ThemeIds.Kawaii] = new KawaiiMascotRenderer(),
        };
    }

    public IReadOnlyList<ThemePalette> All => _palettes.Values.ToList();

    public ThemePalette Current { get; private set; } = null!;

    public IMascotRenderer CurrentRenderer { get; private set; } = null!;

    public event Action? ThemeChanged;

    public void Apply(string? themeId)
    {
        var id = string.IsNullOrWhiteSpace(themeId) || !_palettes.ContainsKey(themeId)
            ? ThemeIds.Default
            : themeId;
        Current = _palettes[id];
        CurrentRenderer = _renderers[id];
        ApplyResources(Current);
        ThemeChanged?.Invoke();
    }

    private static void ApplyResources(ThemePalette theme)
    {
        var resources = Application.Current.Resources;
        resources["AccentBrush"] = new SolidColorBrush(theme.Accent);
        resources["BgBrush"] = new SolidColorBrush(theme.Background);
        resources["CardBrush"] = new SolidColorBrush(theme.Card);
        resources["BorderBrush"] = new SolidColorBrush(theme.Border);
        resources["TextBrush"] = new SolidColorBrush(theme.Text);
        resources["MutedBrush"] = new SolidColorBrush(theme.Muted);
        resources["PanelFrameGradient"] = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = theme.FrameStart, Offset = 0 },
                new GradientStop { Color = theme.FrameMid, Offset = 0.55 },
                new GradientStop { Color = theme.FrameEnd, Offset = 1 },
            },
        };
    }

    private static Windows.UI.Color Color(byte r, byte g, byte b) =>
        Windows.UI.Color.FromArgb(255, r, g, b);

    private static Windows.UI.Color Color(byte a, byte r, byte g, byte b) =>
        Windows.UI.Color.FromArgb(a, r, g, b);
}
