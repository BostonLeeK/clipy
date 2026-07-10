using System.Text.Json.Serialization;
using Clipy.Localization;

namespace Clipy.Models;

public sealed class ChatSession
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("agent_chat_id")]
    public string? AgentChatId { get; set; }

    [JsonPropertyName("agent_provider")]
    public string? AgentProvider { get; set; }

    [JsonPropertyName("workspace")]
    public string Workspace { get; set; } = "";

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = new();
}
