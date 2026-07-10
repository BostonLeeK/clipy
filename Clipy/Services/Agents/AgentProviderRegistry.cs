using Clipy.Models;

namespace Clipy.Services.Agents;

public static class AgentProviderRegistry
{
    public static IReadOnlyList<AgentProviderDescriptor> All { get; } =
    [
        new()
        {
            Id = AgentProviders.Cursor,
            NameKey = "provider.cursor.name",
            HintKey = "provider.cursor.hint",
            SupportsLogin = true,
            SupportsModes = true,
            SupportsModels = true,
        },
        new()
        {
            Id = AgentProviders.Codex,
            NameKey = "provider.codex.name",
            HintKey = "provider.codex.hint",
            SupportsLogin = true,
            SupportsModes = true,
            SupportsModels = true,
        },
        new()
        {
            Id = AgentProviders.Claude,
            NameKey = "provider.claude.name",
            HintKey = "provider.claude.hint",
            SupportsLogin = true,
            SupportsModes = true,
            SupportsModels = true,
        },
    ];

    public static IAgentProvider Create(AppConfig config) =>
        Resolve(config.AgentProvider, config);

    public static IAgentProvider Resolve(string? providerId, AppConfig config) =>
        NormalizeId(providerId) switch
        {
            AgentProviders.Codex => new CodexAgentProvider(config),
            AgentProviders.Claude => new ClaudeAgentProvider(config),
            _ => new CursorAgentProvider(config),
        };

    public static string NormalizeId(string? providerId) =>
        providerId?.ToLowerInvariant() switch
        {
            AgentProviders.Codex => AgentProviders.Codex,
            AgentProviders.Claude => AgentProviders.Claude,
            _ => AgentProviders.Cursor,
        };

    public static AgentProviderDescriptor GetDescriptor(string? providerId)
    {
        var id = NormalizeId(providerId);
        return All.First(p => p.Id == id);
    }
}
