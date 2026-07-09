using System.Text.Json.Serialization;

namespace Clipy.Models;

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("at")]
    public DateTime At { get; set; } = DateTime.UtcNow;
}
