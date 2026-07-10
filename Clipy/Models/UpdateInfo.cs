namespace Clipy.Models;

public sealed class UpdateInfo
{
    public required string Version { get; init; }
    public required string ReleaseNotes { get; init; }
    public required string InstallerUrl { get; init; }
    public required string PortableUrl { get; init; }
    public required string InstallerFileName { get; init; }
    public required string PortableFileName { get; init; }
}
