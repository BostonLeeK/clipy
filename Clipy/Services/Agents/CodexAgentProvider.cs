using System.Diagnostics;
using System.Text.Json;
using Clipy.Localization;
using Clipy.Models;

namespace Clipy.Services.Agents;

public sealed class CodexAgentProvider : IAgentProvider
{
    private readonly AppConfig _config;
    private Process? _process;
    private bool _hasSession;
    private string _cliPath;

    public CodexAgentProvider(AppConfig config)
    {
        _config = config;
        _cliPath = ResolvePath();
    }

    public string Id => AgentProviders.Codex;
    public string DisplayName => "OpenAI Codex";
    public bool SupportsLogin => true;
    public bool SupportsModes => true;
    public bool SupportsModels => true;

    public string Workspace
    {
        get => _config.Workspace;
        set => _config.Workspace = value;
    }

    public string? ChatId => _config.ChatId;

    public bool IsAvailable() => CliResolver.Exists(_cliPath);

    public async Task<string> CheckStatusAsync()
    {
        if (!IsAvailable()) return "missing";
        try
        {
            var psi = AgentProcessRunner.CreateRedirectedPsi(_cliPath, "login", "status");
            var (code, _) = await AgentProcessRunner.RunCaptureAsync(psi);
            return code == 0 ? "ready" : "logged_out";
        }
        catch
        {
            return "error";
        }
    }

    public Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync()
    {
        IReadOnlyList<AgentModelInfo> models = new List<AgentModelInfo>
        {
            new() { Id = "auto", Name = "Auto" },
            new() { Id = "gpt-5.4", Name = "GPT-5.4" },
            new() { Id = "gpt-5.3-codex", Name = "GPT-5.3 Codex" },
            new() { Id = "gpt-5-codex-mini", Name = "GPT-5 Codex Mini" },
        };
        return Task.FromResult(models);
    }

    public void Login() =>
        Process.Start(new ProcessStartInfo(_cliPath, "login") { UseShellExecute = true });

    public async Task LogoutAsync()
    {
        if (!IsAvailable()) return;
        try
        {
            var psi = AgentProcessRunner.CreateRedirectedPsi(_cliPath, "logout");
            await AgentProcessRunner.RunCaptureAsync(psi);
        }
        catch { }

        _config.ChatId = null;
        _hasSession = false;
    }

    public bool IsLoggedIn(string status) => status == "ready";

    public Task NewChatAsync()
    {
        _config.ChatId = null;
        _hasSession = false;
        return Task.CompletedTask;
    }

    public void Cancel()
    {
        try { _process?.Kill(entireProcessTree: true); } catch { }
        _process = null;
    }

    public async Task RunPromptAsync(
        string prompt,
        Action<string> onStatus,
        Action<string> onThinking,
        Action<string> onChunk,
        Action<string?, int> onDone,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default)
    {
        if (!IsAvailable())
        {
            onDone(Loc.Get("agent.not_found"), 1);
            return;
        }

        var prepared = AgentAttachments.Prepare(attachments);
        var fullPrompt = AgentAttachments.BuildPrompt(prompt, prepared);
        var psi = AgentProcessRunner.CreateRedirectedPsi(_cliPath);
        psi.WorkingDirectory = ResolveWorkingDirectory();

        if (_hasSession && string.IsNullOrEmpty(_config.ChatId))
        {
            psi.ArgumentList.Add("exec");
            psi.ArgumentList.Add("resume");
            psi.ArgumentList.Add("--last");
        }
        else
        {
            psi.ArgumentList.Add("exec");
        }

        psi.ArgumentList.Add("--json");
        psi.ArgumentList.Add("--sandbox");
        psi.ArgumentList.Add(SandboxMode());
        psi.ArgumentList.Add("--skip-git-repo-check");

        if (!string.IsNullOrWhiteSpace(_config.ModelId)
            && !_config.ModelId.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add(_config.ModelId.Trim());
        }

        psi.ArgumentList.Add(fullPrompt);

        onStatus(prepared.Count > 0
            ? Loc.Format("agent.sending_files", prepared.Count)
            : Loc.Get("status.thinking_short"));

        var lastFull = "";
        await AgentProcessRunner.RunStreamingAsync(
            psi,
            line => ParseCodexLine(line, ref lastFull),
            onStatus,
            onThinking,
            onChunk,
            (final, code) =>
            {
                if (code == 0) _hasSession = true;
                onDone(final, code);
            },
            p => _process = p,
            ct);
        _process = null;
    }

    private string ResolvePath() =>
        CliResolver.Resolve(_config.CodexPath, "codex", "codex.cmd", "codex.exe");

    private string ResolveWorkingDirectory()
    {
        try
        {
            if (Directory.Exists(_config.Workspace))
                return Path.GetFullPath(_config.Workspace);
        }
        catch { }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private string SandboxMode() =>
        string.Equals(_config.AgentMode, "ask", StringComparison.OrdinalIgnoreCase)
            ? "read-only"
            : "workspace-write";

    private static AgentStreamUpdate ParseCodexLine(string line, ref string lastFull)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var text = ExtractJsonText(root);
            if (string.IsNullOrEmpty(text))
                return new AgentStreamUpdate(null, lastFull, null, null);

            if (text.StartsWith(lastFull, StringComparison.Ordinal))
            {
                var delta = text.Length > lastFull.Length ? text[lastFull.Length..] : null;
                lastFull = text;
                return new AgentStreamUpdate(delta, lastFull, null, Loc.Get("agent.answering"));
            }

            lastFull += text;
            return new AgentStreamUpdate(text, lastFull, null, Loc.Get("agent.answering"));
        }
        catch (JsonException)
        {
            if (line.StartsWith('{')) return new AgentStreamUpdate(null, lastFull, null, null);
            lastFull += line;
            return new AgentStreamUpdate(line, lastFull, null, null);
        }
    }

    private static string? ExtractJsonText(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.String)
            return root.GetString();

        if (root.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
            return textEl.GetString();

        if (root.TryGetProperty("message", out var messageEl))
            return ExtractJsonText(messageEl);

        if (root.TryGetProperty("content", out var contentEl))
        {
            if (contentEl.ValueKind == JsonValueKind.String)
                return contentEl.GetString();
            if (contentEl.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var item in contentEl.EnumerateArray())
                {
                    var piece = ExtractJsonText(item);
                    if (!string.IsNullOrEmpty(piece))
                        sb.Append(piece);
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
        }

        if (root.TryGetProperty("item", out var itemEl))
            return ExtractJsonText(itemEl);

        if (root.TryGetProperty("delta", out var deltaEl) && deltaEl.ValueKind == JsonValueKind.String)
            return deltaEl.GetString();

        return null;
    }
}
