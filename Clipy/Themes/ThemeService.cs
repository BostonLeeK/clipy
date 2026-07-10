using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Clipy.Themes;

public sealed class ThemeService
{
    private readonly Dictionary<string, ThemePack> _packs;

    public ThemeService()
    {
        _packs = ThemePackLoader.LoadAll()
            .ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ThemePack> All => _packs.Values.OrderBy(p => p.Manifest.Name).ToList();

    public ThemePack CurrentPack { get; private set; } = null!;

    public ThemePalette Current => CurrentPack.Palette;

    public IMascotRenderer CurrentRenderer => CurrentPack.Renderer;

    public event Action? ThemeChanged;

    public void Apply(string? themeId)
    {
        var id = string.IsNullOrWhiteSpace(themeId) || !_packs.ContainsKey(themeId)
            ? ThemeIds.Default
            : themeId;
        CurrentPack = _packs[id];
        ApplyResources(CurrentPack.Palette);
        ThemeChanged?.Invoke();
    }

    public ThemePack? Get(string themeId) =>
        _packs.TryGetValue(themeId, out var pack) ? pack : null;

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
}
