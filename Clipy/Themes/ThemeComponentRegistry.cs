using Clipy.Themes.Packs.Grain;
using Clipy.Themes.Packs.Neon;
using Clipy.Themes.Packs.Totoro;

namespace Clipy.Themes;

public static class ThemeComponentRegistry
{
    public static IMascotRenderer CreateMascot(string type) =>
        new SafeMascotRenderer(type.ToLowerInvariant() switch
        {
            "neon" or "default" => new NeonMascotRenderer(),
            "kawaii" or "totoro" => new KawaiiMascotRenderer(),
            "grain" => new GrainMascotRenderer(),
            _ => new NeonMascotRenderer(),
        });

    public static IFrameDecorator? CreateFrame(string? type) =>
        type?.ToLowerInvariant() switch
        {
            "totoro" => new TotoroFrameDecorator(),
            _ => null,
        };
}
