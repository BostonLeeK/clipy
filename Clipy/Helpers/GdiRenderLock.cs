namespace Clipy.Helpers;

internal static class GdiRenderLock
{
    internal static readonly object Sync = new();
}
