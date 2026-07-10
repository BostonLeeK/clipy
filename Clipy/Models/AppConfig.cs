using System.Text.Json.Serialization;

namespace Clipy.Models;

public sealed class AppConfig
{
    [JsonPropertyName("workspace")]
    public string Workspace { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [JsonPropertyName("agent_provider")]
    public string AgentProvider { get; set; } = "cursor";

    [JsonPropertyName("agent_path")]
    public string AgentPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "cursor-agent", "agent.cmd");

    [JsonPropertyName("codex_path")]
    public string CodexPath { get; set; } = "codex";

    [JsonPropertyName("claude_path")]
    public string ClaudePath { get; set; } = "claude";

    [JsonPropertyName("window_x")]
    public int? WindowX { get; set; }

    [JsonPropertyName("window_y")]
    public int? WindowY { get; set; }

    [JsonPropertyName("chat_id")]
    public string? ChatId { get; set; }

    [JsonPropertyName("local_session_id")]
    public string? LocalSessionId { get; set; }

    [JsonPropertyName("expanded")]
    public bool Expanded { get; set; }

    [JsonPropertyName("panel_width")]
    public int? PanelWidth { get; set; }

    [JsonPropertyName("panel_height")]
    public int? PanelHeight { get; set; }

    [JsonPropertyName("theme_id")]
    public string ThemeId { get; set; } = "default";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "uk";

    [JsonPropertyName("model_id")]
    public string ModelId { get; set; } = "auto";

    [JsonPropertyName("agent_mode")]
    public string AgentMode { get; set; } = "agent";

    [JsonPropertyName("recent_workspaces")]
    public List<string> RecentWorkspaces { get; set; } = new();

    [JsonPropertyName("skipped_update_version")]
    public string? SkippedUpdateVersion { get; set; }

    [JsonPropertyName("last_update_check_utc")]
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
}
