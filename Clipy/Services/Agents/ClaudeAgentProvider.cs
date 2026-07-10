using System.Diagnostics;
using System.Text.Json;
using Clipy.Localization;
using Clipy.Models;

namespace Clipy.Services.Agents;

public sealed class ClaudeAgentProvider : IAgentProvider
{
    private readonly AppConfig _config;
    private Process? _process;
    private bool _hasSession;
    private string _cliPath;

    public ClaudeAgentProvider(AppConfig config)
    {
        _config = config;
        _cliPath = ResolvePath();
    }

    public string Id => AgentProviders.Claude;
    public string DisplayName => "Claude Code";
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

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            return "ready";

        try
        {
            var psi = AgentProcessRunner.CreateRedirectedPsi(_cliPath, "--version");
            var (code, _) = await AgentProcessRunner.RunCaptureAsync(psi);
            return code == 0 ? "logged_out" : "error";
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
            new() { Id = "claude-sonnet-4-6", Name = "Claude Sonnet 4.6" },
            new() { Id = "claude-opus-4-6", Name = "Claude Opus 4.6" },
            new() { Id = "claude-haiku-4-5", Name = "Claude Haiku 4.5" },
        };
        return Task.FromResult(models);
    }

    public void Login() =>
        Process.Start(new ProcessStartInfo(_cliPath) { UseShellExecute = true });

    public Task LogoutAsync()
    {
        _config.ChatId = null;
        _hasSession = false;
        return Task.CompletedTask;
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
        var fullPrompt = BuildPromptForMode(AgentAttachments.BuildPrompt(prompt, prepared));
        var psi = AgentProcessRunner.CreateRedirectedPsi(_cliPath);
        psi.WorkingDirectory = ResolveWorkingDirectory();

        psi.ArgumentList.Add("--bare");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(fullPrompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("--include-partial-messages");

        if (!string.IsNullOrWhiteSpace(_config.ModelId)
            && !_config.ModelId.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(_config.ModelId.Trim());
        }

        ApplyModeFlags(psi.ArgumentList);

        if (!prepared.Any(AgentAttachments.IsImage))
        {
            if (_hasSession)
                psi.ArgumentList.Add("--continue");
        }

        onStatus(prepared.Count > 0
            ? Loc.Format("agent.sending_files", prepared.Count)
            : Loc.Get("status.thinking_short"));

        var lastFull = "";
        await AgentProcessRunner.RunStreamingAsync(
            psi,
            line => ParseClaudeLine(line, ref lastFull),
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
        CliResolver.Resolve(_config.ClaudePath, "claude", "claude.cmd", "claude.exe");

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

    private string BuildPromptForMode(string prompt)
    {
        if (string.Equals(_config.AgentMode, "plan", StringComparison.OrdinalIgnoreCase))
            return "PLAN ONLY: create a step-by-step plan without editing files. " + prompt;

        if (string.Equals(_config.AgentMode, "ask", StringComparison.OrdinalIgnoreCase))
            return "ASK ONLY: answer questions without editing files. " + prompt;

        return prompt;
    }

    private void ApplyModeFlags(IList<string> args)
    {
        if (string.Equals(_config.AgentMode, "ask", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--permission-mode");
            args.Add("dontAsk");
            return;
        }

        args.Add("--permission-mode");
        args.Add("acceptEdits");
    }

    private static AgentStreamUpdate ParseClaudeLine(string line, ref string lastFull)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeEl))
            {
                var type = typeEl.GetString();
                if (type is "thinking" or "thinking_delta")
                {
                    var text = ExtractClaudeText(root);
                    if (!string.IsNullOrEmpty(text))
                        return new AgentStreamUpdate(null, lastFull, text, Loc.Get("status.thinking_short"));
                }

                if (type is "text_delta" or "content_block_delta" or "assistant")
                {
                    var text = ExtractClaudeText(root);
                    if (!string.IsNullOrEmpty(text))
                    {
                        lastFull += text;
                        return new AgentStreamUpdate(text, lastFull, null, Loc.Get("agent.answering"));
                    }
                }
            }

            var fallback = ExtractClaudeText(root);
            if (!string.IsNullOrEmpty(fallback))
            {
                lastFull += fallback;
                return new AgentStreamUpdate(fallback, lastFull, null, Loc.Get("agent.answering"));
            }
        }
        catch (JsonException)
        {
            if (!line.StartsWith('{'))
            {
                lastFull += line;
                return new AgentStreamUpdate(line, lastFull, null, null);
            }
        }

        return new AgentStreamUpdate(null, lastFull, null, null);
    }

    private static string? ExtractClaudeText(JsonElement root)
    {
        if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            return text.GetString();

        if (root.TryGetProperty("delta", out var delta))
        {
            if (delta.ValueKind == JsonValueKind.String)
                return delta.GetString();
            if (delta.TryGetProperty("text", out var deltaText))
                return deltaText.GetString();
        }

        if (root.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
                return content.GetString();
            if (content.ValueKind == JsonValueKind.Array)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var blockText))
                        sb.Append(blockText.GetString());
                }
                return sb.Length > 0 ? sb.ToString() : null;
            }
        }

        if (root.TryGetProperty("message", out var message))
            return ExtractClaudeText(message);

        return null;
    }
}
