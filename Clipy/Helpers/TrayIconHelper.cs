using System.Drawing;
using Clipy.Helpers;
using Clipy.Themes;

namespace Clipy.Helpers;

public static class TrayIconHelper
{
    public static Icon Create(IMascotRenderer renderer)
    {
        lock (GdiRenderLock.Sync)
        {
            using var source = AppIconRenderer.Render(renderer, 32);
            return IconEncoder.CreateIcon(source);
        }
    }
}
