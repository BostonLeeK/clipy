using Clipy.Models;

namespace Clipy.Services.Agents;

public interface IAgentProvider
{
    string Id { get; }
    string DisplayName { get; }
    bool SupportsLogin { get; }
    bool SupportsModes { get; }
    bool SupportsModels { get; }

    string Workspace { get; set; }
    string? ChatId { get; }

    bool IsAvailable();
    Task<string> CheckStatusAsync();
    Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync();
    void Login();
    Task LogoutAsync();
    bool IsLoggedIn(string status);
    Task NewChatAsync();
    void Cancel();
    Task RunPromptAsync(
        string prompt,
        Action<string> onStatus,
        Action<string> onThinking,
        Action<string> onChunk,
        Action<string?, int> onDone,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default);
}

public sealed class AgentProviderDescriptor
{
    public required string Id { get; init; }
    public required string NameKey { get; init; }
    public required string HintKey { get; init; }
    public required bool SupportsLogin { get; init; }
    public required bool SupportsModes { get; init; }
    public required bool SupportsModels { get; init; }
}
