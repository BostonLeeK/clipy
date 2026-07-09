using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clipy.Models;

namespace Clipy.Services;

public sealed class AgentService
{
    private static readonly Regex ModelLine = new(
        @"^(?<id>[^\s-]+(?:-[^\s-]+)*)\s+-\s+(?<name>.+)$",
        RegexOptions.Compiled);

    private readonly AppConfig _config;
    private Process? _process;
    private bool _hasSession;
    private string? _resolvedNode;
    private string? _resolvedIndex;

    public AgentService(AppConfig config) => _config = config;

    public string Workspace
    {
        get => _config.Workspace;
        set => _config.Workspace = value;
    }

    public string? ChatId => _config.ChatId;

    public bool IsAvailable() => ResolveAgentBinary() is not null || File.Exists(_config.AgentPath);

    public async Task<string> CheckStatusAsync()
    {
        if (!IsAvailable()) return "missing";
        try
        {
            using var p = StartAgentProcess(["status"]);
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
        var fallback = new List<AgentModelInfo>
        {
            new() { Id = "auto", Name = "Auto" },
        };
        if (!IsAvailable()) return fallback;

        try
        {
            using var p = StartAgentProcess(["models"]);
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

    public void Login()
    {
        Process.Start(new ProcessStartInfo(_config.AgentPath, "login") { UseShellExecute = true });
    }

    public async Task LogoutAsync()
    {
        if (!IsAvailable()) return;
        try
        {
            using var p = StartAgentProcess(["logout"]);
            if (p is not null) await p.WaitForExitAsync();
        }
        catch { /* ignore */ }

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
            using var p = StartAgentProcess(["create-chat"]);
            if (p is null) return;
            var id = (await p.StandardOutput.ReadToEndAsync()).Trim();
            await p.WaitForExitAsync();
            if (!string.IsNullOrEmpty(id))
            {
                _config.ChatId = id;
                _hasSession = true;
            }
        }
        catch { /* ignore */ }
    }

    public void Cancel()
    {
        try { _process?.Kill(entireProcessTree: true); } catch { /* ignore */ }
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
            onDone("Cursor Agent не знайдено", 1);
            return;
        }

        var prepared = PrepareAttachments(attachments);
        var psi = CreateAgentStartInfo();
        if (psi is null)
        {
            onDone("Cursor Agent не знайдено", 1);
            return;
        }

        AddRunArgs(psi.ArgumentList, prompt, prepared);
        psi.WorkingDirectory = ResolveWorkingDirectory();
        psi.Environment["CURSOR_AGENT_TRUST"] = "1";
        psi.Environment["NO_OPEN_BROWSER"] = "1";

        onStatus(prepared.Count > 0 ? $"Надсилаю {prepared.Count} файл(и)…" : "Думаю…");
        var collected = new List<string>();
        var lastFull = "";
        var thinkingBuf = new StringBuilder();
        var stderrBuf = new StringBuilder();

        try
        {
            _process = Process.Start(psi);
            if (_process is null)
            {
                onDone("Не вдалося запустити агента", 1);
                return;
            }

            var stderrTask = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var errLine = await _process.StandardError.ReadLineAsync(ct);
                        if (errLine is null) break;
                        stderrBuf.AppendLine(errLine);
                    }
                }
                catch (OperationCanceledException) { /* ignore */ }
            }, ct);

            while (!ct.IsCancellationRequested)
            {
                string? line;
                try
                {
                    using var lineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lineCts.CancelAfter(TimeSpan.FromMinutes(2));
                    line = await _process.StandardOutput.ReadLineAsync(lineCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    onStatus("Агент ще працює…");
                    continue;
                }

                if (line is null) break;
                line = line.Trim();
                if (line.Length == 0) continue;
                if (LooksLikeTrustPrompt(line))
                {
                    collected.Add(line);
                    onChunk(line + "\n");
                    continue;
                }

                var evt = ParseEvent(line, lastFull, thinkingBuf);
                lastFull = evt.LastFull;
                if (!string.IsNullOrEmpty(evt.Status))
                    onStatus(evt.Status);
                if (!string.IsNullOrEmpty(evt.Thinking))
                    onThinking(evt.Thinking);
                if (!string.IsNullOrEmpty(evt.Delta))
                {
                    collected.Add(evt.Delta);
                    onChunk(evt.Delta);
                }
            }

            await stderrTask;
            await _process.WaitForExitAsync(ct);
            var code = _process.ExitCode;
            var stderr = stderrBuf.ToString();
            var final = string.Concat(collected).Trim();
            if (code != 0 && string.IsNullOrEmpty(final))
            {
                final = string.IsNullOrWhiteSpace(stderr)
                    ? $"Помилка агента (код {code})"
                    : stderr.Trim();
            }
            else if (code != 0 && LooksLikeTrustPrompt(stderr))
            {
                final = "Workspace Trust: агент не довіряє папці. Перевір workspace у налаштуваннях.";
            }
            if (code == 0) _hasSession = true;
            onDone(string.IsNullOrEmpty(final) ? null : final, code);
        }
        catch (OperationCanceledException)
        {
            Cancel();
            onDone(null, -1);
        }
        catch (Exception ex)
        {
            onDone(ex.Message, 1);
        }
        finally
        {
            _process = null;
        }
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

    private ProcessStartInfo? CreateAgentStartInfo()
    {
        var binary = ResolveAgentBinary();
        ProcessStartInfo psi;
        if (binary is { } b)
        {
            psi = new ProcessStartInfo(b.Node)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add(b.Index);
            return psi;
        }

        if (!File.Exists(_config.AgentPath)) return null;
        return new ProcessStartInfo(_config.AgentPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
    }

    private Process? StartAgentProcess(IEnumerable<string> arguments)
    {
        var psi = CreateAgentStartInfo();
        if (psi is null) return null;
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);
        return Process.Start(psi);
    }

    private void AddRunArgs(IList<string> args, string prompt, IReadOnlyList<string> attachments)
    {
        var fullPrompt = BuildPrompt(prompt, attachments);
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

        var hasImages = attachments.Any(IsImage);
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

    private IReadOnlyList<string> PrepareAttachments(IReadOnlyList<string>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return Array.Empty<string>();

        var inbox = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipyAssistant", "inbox");
        try { Directory.CreateDirectory(inbox); } catch { /* ignore */ }

        var result = new List<string>();
        foreach (var path in attachments)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var exists = File.Exists(path) || Directory.Exists(path);
            if (!exists) continue;

            if (Directory.Exists(path))
            {
                result.Add(Path.GetFullPath(path));
                continue;
            }

            try
            {
                var ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext)) ext = ".bin";
                var dest = Path.Combine(inbox, $"attach-{DateTime.Now:yyyyMMdd-HHmmss-fff}{ext}");
                File.Copy(path, dest, overwrite: true);
                result.Add(dest);
            }
            catch
            {
                result.Add(Path.GetFullPath(path));
            }
        }
        return result;
    }

    private string ResolveWorkingDirectory()
    {
        try
        {
            if (Directory.Exists(_config.Workspace))
                return Path.GetFullPath(_config.Workspace);
        }
        catch { /* ignore */ }
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

    public static string BuildPrompt(string prompt, IReadOnlyList<string>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return CollapsePrompt(prompt);

        var parts = new List<string>();
        var images = attachments.Where(IsImage).ToList();
        var others = attachments.Where(p => !IsImage(p)).ToList();

        if (images.Count > 0)
        {
            parts.Add("IMPORTANT: An image file is attached to this message. Open/read ONLY these exact file paths with the read tool before answering. Do not use other images from chat history.");
            foreach (var path in images)
                parts.Add($"IMAGE_FILE={path}");
        }

        if (others.Count > 0)
        {
            parts.Add("Attached files/folders:");
            foreach (var path in others)
                parts.Add($"FILE={path}");
        }

        var user = string.IsNullOrWhiteSpace(prompt) ? "Analyze the attached files." : prompt.Trim();
        parts.Add("USER_REQUEST: " + CollapsePrompt(user));
        return string.Join(" ", parts);
    }

    private static string CollapsePrompt(string text) =>
        string.Join(" ", text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    private readonly record struct StreamEvent(string? Delta, string LastFull, string? Thinking, string? Status);

    private static StreamEvent ParseEvent(string line, string lastFull, StringBuilder thinkingBuf)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl))
                return new StreamEvent(null, lastFull, null, null);
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
                        return new StreamEvent(null, lastFull, t, "Думаю…");
                    }
                }
                if (subtype == "completed")
                    return new StreamEvent(null, lastFull, null, "Думаю…");
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
                        var name = string.IsNullOrEmpty(path) ? "файл" : Path.GetFileName(path);
                        return new StreamEvent(null, lastFull, null, $"Читаю {name}…");
                    }
                    if (tool.TryGetProperty("writeToolCall", out var write)
                        && write.TryGetProperty("args", out var wargs)
                        && wargs.TryGetProperty("path", out var wpath))
                    {
                        var path = wpath.GetString();
                        var name = string.IsNullOrEmpty(path) ? "файл" : Path.GetFileName(path);
                        return new StreamEvent(null, lastFull, null, $"Пишу {name}…");
                    }
                    if (tool.TryGetProperty("shellToolCall", out _))
                        return new StreamEvent(null, lastFull, null, "Виконую команду…");
                    return new StreamEvent(null, lastFull, null, "Працюю з інструментами…");
                }
            }

            if (type is "text-delta" or "content_delta")
            {
                var d = root.TryGetProperty("delta", out var de) ? de.GetString()
                    : root.TryGetProperty("text", out var te) ? te.GetString() : "";
                if (string.IsNullOrEmpty(d))
                    return new StreamEvent(null, lastFull, null, null);
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
                    return new StreamEvent(null, lastFull, null, null);

                return MergeAnswerText(lastFull, piece, incomingIsFull: true, status: "Пишу відповідь…");
            }
        }
        catch (JsonException)
        {
            if (!line.StartsWith('{'))
            {
                var merged = MergeAnswerText(lastFull, line, incomingIsFull: false);
                return merged;
            }
        }
        return new StreamEvent(null, lastFull, null, null);
    }

    private static StreamEvent MergeAnswerText(
        string lastFull,
        string incoming,
        bool incomingIsFull,
        string? status = null)
    {
        if (string.IsNullOrEmpty(incoming))
            return new StreamEvent(null, lastFull, null, status);

        if (incomingIsFull)
        {
            if (incoming.StartsWith(lastFull, StringComparison.Ordinal))
            {
                var delta = incoming.Length > lastFull.Length ? incoming[lastFull.Length..] : null;
                return new StreamEvent(delta, incoming, null, status);
            }

            if (lastFull.StartsWith(incoming, StringComparison.Ordinal))
                return new StreamEvent(null, lastFull, null, status);

            if (lastFull.EndsWith(incoming, StringComparison.Ordinal))
                return new StreamEvent(null, lastFull, null, status);

            return new StreamEvent(incoming, lastFull + incoming, null, status);
        }

        if (lastFull.EndsWith(incoming, StringComparison.Ordinal))
            return new StreamEvent(null, lastFull, null, status);

        return new StreamEvent(incoming, lastFull + incoming, null, status);
    }
}
