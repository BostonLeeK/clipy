using Clipy.Models;
using Clipy.Services.Agents;

namespace Clipy.Services;

public sealed class AgentService
{
    private readonly AppConfig _config;
    private IAgentProvider _provider;

    public AgentService(AppConfig config)
    {
        _config = config;
        _provider = AgentProviderRegistry.Create(config);
    }

    public IReadOnlyList<AgentProviderDescriptor> Providers => AgentProviderRegistry.All;

    public string ProviderId => _provider.Id;
    public string ProviderDisplayName => _provider.DisplayName;
    public bool SupportsLogin => _provider.SupportsLogin;
    public bool SupportsModes => _provider.SupportsModes;
    public bool SupportsModels => _provider.SupportsModels;

    public string Workspace
    {
        get => _provider.Workspace;
        set => _provider.Workspace = value;
    }

    public string? ChatId => _provider.ChatId;

    public void SetProvider(string providerId)
    {
        var normalized = AgentProviderRegistry.NormalizeId(providerId);
        if (string.Equals(_config.AgentProvider, normalized, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_provider.Id, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        _provider.Cancel();
        _config.AgentProvider = normalized;
        _config.ChatId = null;
        _provider = AgentProviderRegistry.Resolve(normalized, _config);
    }

    public bool IsAvailable() => _provider.IsAvailable();
    public Task<string> CheckStatusAsync() => _provider.CheckStatusAsync();
    public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync() => _provider.ListModelsAsync();
    public void Login() => _provider.Login();
    public Task LogoutAsync() => _provider.LogoutAsync();
    public bool IsLoggedIn(string status) => _provider.IsLoggedIn(status);
    public Task NewChatAsync() => _provider.NewChatAsync();
    public void Cancel() => _provider.Cancel();

    public Task RunPromptAsync(
        string prompt,
        Action<string> onStatus,
        Action<string> onThinking,
        Action<string> onChunk,
        Action<string?, int> onDone,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default) =>
        _provider.RunPromptAsync(prompt, onStatus, onThinking, onChunk, onDone, attachments, ct);

    public static string BuildPrompt(string prompt, IReadOnlyList<string>? attachments) =>
        AgentAttachments.BuildPrompt(prompt, attachments);
}
