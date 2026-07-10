using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clipy.Localization;
using Clipy.Models;

namespace Clipy.Services.Agents;

public sealed class CursorAgentProvider : IAgentProvider
{
    private static readonly Regex ModelLine = new(
        @"^(?<id>[^\s-]+(?:-[^\s-]+)*)\s+-\s+(?<name>.+)$",
        RegexOptions.Compiled);

    private readonly AppConfig _config;
    private Process? _process;
    private bool _hasSession;
    private string? _resolvedNode;
    private string? _resolvedIndex;

    public CursorAgentProvider(AppConfig config) => _config = config;

    public string Id => AgentProviders.Cursor;
    public string DisplayName => "Cursor Agent";
    public bool SupportsLogin => true;
    public bool SupportsModes => true;
    public bool SupportsModels => true;

    public string Workspace
    {
        get => _config.Workspace;
        set => _config.Workspace = value;
    }

    public string? ChatId => _config.ChatId;

    public bool IsAvailable() => ResolveAgentBinary() is not null || CliResolver.Exists(_config.AgentPath);

    public async Task<string> CheckStatusAsync()
    {
        if (!IsAvailable()) return "missing";
        try
        {
            using var p = StartProcess(["status"]);
            if (p is null) return "error";
            var output = await p.StandardOutput.ReadToEndAsync();
            output += await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (output.Contains("not logged in", StringComparison.OrdinalIgnoreCase)) return "logged_out";
            return p.ExitCode == 0 ? "ready" : "error";
        }
        catch
        {
            return "error";
        }
    }

    public async Task<IReadOnlyList<AgentModelInfo>> ListModelsAsync()
    {
        var fallback = new List<AgentModelInfo> { new() { Id = "auto", Name = "Auto" } };
        if (!IsAvailable()) return fallback;

        try
        {
            using var p = StartProcess(["models"]);
            if (p is null) return fallback;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var models = new List<AgentModelInfo>();
            foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (raw.StartsWith("Available", StringComparison.OrdinalIgnoreCase)
                    || raw.StartsWith("Tip:", StringComparison.OrdinalIgnoreCase))
                    continue;
                var m = ModelLine.Match(raw);
                if (!m.Success) continue;
                models.Add(new AgentModelInfo
                {
                    Id = m.Groups["id"].Value.Trim(),
                    Name = m.Groups["name"].Value.Trim(),
                });
            }
            if (models.Count == 0) return fallback;
            if (!models.Any(x => x.Id.Equals("auto", StringComparison.OrdinalIgnoreCase)))
                models.Insert(0, new AgentModelInfo { Id = "auto", Name = "Auto" });
            return models;
        }
        catch
        {
            return fallback;
        }
    }

    public void Login() =>
        Process.Start(new ProcessStartInfo(_config.AgentPath, "login") { UseShellExecute = true });

    public async Task LogoutAsync()
    {
        if (!IsAvailable()) return;
        try
        {
            using var p = StartProcess(["logout"]);
            if (p is not null) await p.WaitForExitAsync();
        }
        catch { }

        _config.ChatId = null;
        _hasSession = false;
    }

    public bool IsLoggedIn(string status) => status == "ready";

    public async Task NewChatAsync()
    {
        _config.ChatId = null;
        _hasSession = false;
        try
        {
            using var p = StartProcess(["create-chat"]);
            if (p is null) return;
            var id = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            if (!string.IsNullOrEmpty(id))
            {
                _config.ChatId = id;
                _hasSession = true;
            }
        }
        catch { }
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
        var psi = CreateStartInfo();
        if (psi is null)
        {
            onDone(Loc.Get("agent.not_found"), 1);
            return;
        }

        AddRunArgs(psi.ArgumentList, prompt, prepared);
        psi.WorkingDirectory = ResolveWorkingDirectory();
        psi.Environment["CURSOR_AGENT_TRUST"] = "1";
        psi.Environment["NO_OPEN_BROWSER"] = "1";

        onStatus(prepared.Count > 0
            ? Loc.Format("agent.sending_files", prepared.Count)
            : Loc.Get("status.thinking_short"));

        var lastFull = "";
        var thinkingBuf = new StringBuilder();

        await AgentProcessRunner.RunStreamingAsync(
            psi,
            line =>
            {
                if (LooksLikeTrustPrompt(line))
                    return new AgentStreamUpdate(line + "\n", lastFull, null, null);

                var evt = ParseEvent(line, lastFull, thinkingBuf);
                lastFull = evt.LastFull;
                return evt;
            },
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

    private (string Node, string Index)? ResolveAgentBinary()
    {
        if (_resolvedNode is not null && _resolvedIndex is not null
            && File.Exists(_resolvedNode) && File.Exists(_resolvedIndex))
            return (_resolvedNode, _resolvedIndex);

        try
        {
            var agentPath = _config.AgentPath;
            if (string.IsNullOrWhiteSpace(agentPath)) return null;
            var root = Path.GetDirectoryName(Path.GetFullPath(agentPath));
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;

            var localNode = Path.Combine(root, "node.exe");
            var localIndex = Path.Combine(root, "index.js");
            if (File.Exists(localNode) && File.Exists(localIndex))
            {
                _resolvedNode = localNode;
                _resolvedIndex = localIndex;
                return (_resolvedNode, _resolvedIndex);
            }

            var versions = Path.Combine(root, "versions");
            if (!Directory.Exists(versions)) return null;
            var latest = Directory.GetDirectories(versions)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(d =>
                    File.Exists(Path.Combine(d, "node.exe")) &&
                    File.Exists(Path.Combine(d, "index.js")));
            if (latest is null) return null;

            _resolvedNode = Path.Combine(latest, "node.exe");
            _resolvedIndex = Path.Combine(latest, "index.js");
            return (_resolvedNode, _resolvedIndex);
        }
        catch
        {
            return null;
        }
    }

    private ProcessStartInfo? CreateStartInfo()
    {
        var binary = ResolveAgentBinary();
        if (binary is { } b)
        {
            var psi = AgentProcessRunner.CreateRedirectedPsi(b.Node);
            psi.ArgumentList.Add(b.Index);
            return psi;
        }

        if (!CliResolver.Exists(_config.AgentPath)) return null;
        return AgentProcessRunner.CreateRedirectedPsi(_config.AgentPath);
    }

    private Process? StartProcess(IEnumerable<string> arguments)
    {
        var psi = CreateStartInfo();
        if (psi is null) return null;
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);
        return Process.Start(psi);
    }

    private void AddRunArgs(IList<string> args, string prompt, IReadOnlyList<string> attachments)
    {
        var fullPrompt = AgentAttachments.BuildPrompt(prompt, attachments);
        args.Add("-p");
        args.Add(fullPrompt);
        args.Add("--print");
        args.Add("--output-format");
        args.Add("stream-json");
        args.Add("--stream-partial-output");
        args.Add("--workspace");
        args.Add(_config.Workspace);
        args.Add("--trust");
        args.Add("--force");
        args.Add("--yolo");

        if (!string.IsNullOrWhiteSpace(_config.ModelId)
            && !_config.ModelId.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--model");
            args.Add(_config.ModelId.Trim());
        }

        if (string.Equals(_config.AgentMode, "ask", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--mode");
            args.Add("ask");
        }
        else if (string.Equals(_config.AgentMode, "plan", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--mode");
            args.Add("plan");
        }

        foreach (var dir in attachments
            .Select(p => Path.GetDirectoryName(p))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(d => !IsUnderWorkspace(d!))
            .Take(5))
        {
            args.Add("--add-dir");
            args.Add(dir!);
        }

        var hasImages = attachments.Any(AgentAttachments.IsImage);
        if (!hasImages)
        {
            if (!string.IsNullOrEmpty(_config.ChatId))
            {
                args.Add("--resume");
                args.Add(_config.ChatId);
            }
            else if (_hasSession)
            {
                args.Add("--continue");
            }
        }
    }

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

    private static bool LooksLikeTrustPrompt(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (text.Contains("Workspace Trust", StringComparison.OrdinalIgnoreCase)
         || text.Contains("Do you trust", StringComparison.OrdinalIgnoreCase)
         || text.Contains("--trust", StringComparison.OrdinalIgnoreCase));

    private bool IsUnderWorkspace(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetFullPath(_config.Workspace).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static AgentStreamUpdate ParseEvent(string line, string lastFull, StringBuilder thinkingBuf)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl))
                return new AgentStreamUpdate(null, lastFull, null, null);
            var type = typeEl.GetString();

            if (type == "thinking")
            {
                var subtype = root.TryGetProperty("subtype", out var st) ? st.GetString() : null;
                if (subtype == "delta")
                {
                    var t = root.TryGetProperty("text", out var te) ? te.GetString() : null;
                    if (!string.IsNullOrEmpty(t))
                    {
                        thinkingBuf.Append(t);
                        return new AgentStreamUpdate(null, lastFull, t, Loc.Get("status.thinking_short"));
                    }
                }
                if (subtype == "completed")
                    return new AgentStreamUpdate(null, lastFull, null, Loc.Get("status.thinking_short"));
            }

            if (type == "tool_call")
            {
                var subtype = root.TryGetProperty("subtype", out var st) ? st.GetString() : null;
                if (subtype == "started" && root.TryGetProperty("tool_call", out var tool))
                {
                    if (tool.TryGetProperty("readToolCall", out var read)
                        && read.TryGetProperty("args", out var args)
                        && args.TryGetProperty("path", out var pathEl))
                    {
                        var path = pathEl.GetString();
                        var name = string.IsNullOrEmpty(path) ? Loc.Get("agent.file") : Path.GetFileName(path);
                        return new AgentStreamUpdate(null, lastFull, null, Loc.Format("agent.reading", name));
                    }
                    if (tool.TryGetProperty("writeToolCall", out var write)
                        && write.TryGetProperty("args", out var wargs)
                        && wargs.TryGetProperty("path", out var wpath))
                    {
                        var path = wpath.GetString();
                        var name = string.IsNullOrEmpty(path) ? Loc.Get("agent.file") : Path.GetFileName(path);
                        return new AgentStreamUpdate(null, lastFull, null, Loc.Format("agent.writing", name));
                    }
                    if (tool.TryGetProperty("shellToolCall", out _))
                        return new AgentStreamUpdate(null, lastFull, null, Loc.Get("agent.shell"));
                    return new AgentStreamUpdate(null, lastFull, null, Loc.Get("agent.tools"));
                }
            }

            if (type is "text-delta" or "content_delta")
            {
                var d = root.TryGetProperty("delta", out var de) ? de.GetString()
                    : root.TryGetProperty("text", out var te) ? te.GetString() : "";
                if (string.IsNullOrEmpty(d))
                    return new AgentStreamUpdate(null, lastFull, null, null);
                return MergeAnswerText(lastFull, d, incomingIsFull: false);
            }

            if (type == "assistant" && root.TryGetProperty("message", out var msg)
                && msg.TryGetProperty("content", out var content))
            {
                var piece = "";
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                        && block.TryGetProperty("text", out var tx))
                        piece += tx.GetString();
                }
                if (string.IsNullOrEmpty(piece))
                    return new AgentStreamUpdate(null, lastFull, null, null);

                return MergeAnswerText(lastFull, piece, incomingIsFull: true, status: Loc.Get("agent.answering"));
            }
        }
        catch (JsonException)
        {
            if (!line.StartsWith('{'))
                return MergeAnswerText(lastFull, line, incomingIsFull: false);
        }

        return new AgentStreamUpdate(null, lastFull, null, null);
    }

    private static AgentStreamUpdate MergeAnswerText(
        string lastFull,
        string incoming,
        bool incomingIsFull,
        string? status = null)
    {
        if (string.IsNullOrEmpty(incoming))
            return new AgentStreamUpdate(null, lastFull, null, status);

        if (incomingIsFull)
        {
            if (string.Equals(incoming, lastFull, StringComparison.Ordinal))
                return new AgentStreamUpdate(null, lastFull, null, status);

            if (incoming.StartsWith(lastFull, StringComparison.Ordinal))
            {
                var delta = incoming.Length > lastFull.Length ? incoming[lastFull.Length..] : null;
                return new AgentStreamUpdate(delta, incoming, null, status);
            }

            if (lastFull.StartsWith(incoming, StringComparison.Ordinal))
                return new AgentStreamUpdate(null, lastFull, null, status);

            if (lastFull.EndsWith(incoming, StringComparison.Ordinal))
                return new AgentStreamUpdate(null, lastFull, null, status);

            return new AgentStreamUpdate(null, incoming, null, status);
        }

        if (lastFull.EndsWith(incoming, StringComparison.Ordinal))
            return new AgentStreamUpdate(null, lastFull, null, status);

        return new AgentStreamUpdate(incoming, lastFull + incoming, null, status);
    }
}
