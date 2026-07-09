using System.Text.Json.Serialization;

namespace Clipy.Models;

public sealed class AppConfig
{
    [JsonPropertyName("workspace")]
    public string Workspace { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [JsonPropertyName("agent_path")]
    public string AgentPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cursor-agent", "agent.cmd");

    [JsonPropertyName("window_x")]
    public int? WindowX { get; set; }

    [JsonPropertyName("window_y")]
    public int? WindowY { get; set; }

    [JsonPropertyName("chat_id")]
    public string? ChatId { get; set; }

    [JsonPropertyName("expanded")]
    public bool Expanded { get; set; }

    [JsonPropertyName("theme_id")]
    public string ThemeId { get; set; } = "default";

    [JsonPropertyName("model_id")]
    public string ModelId { get; set; } = "auto";
}
