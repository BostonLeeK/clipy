namespace Clipy.Models;

public sealed class AttachmentItem
{
    public required string Path { get; init; }
    public required string Kind { get; init; }
    public string DisplayName => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;
}
