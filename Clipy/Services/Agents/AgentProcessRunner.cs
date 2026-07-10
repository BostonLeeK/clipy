using System.Diagnostics;
using System.Text;
using Clipy.Localization;

namespace Clipy.Services.Agents;

internal static class AgentProcessRunner
{
    public static async Task RunStreamingAsync(
        ProcessStartInfo psi,
        Func<string, AgentStreamUpdate> parseLine,
        Action<string> onStatus,
        Action<string> onThinking,
        Action<string> onChunk,
        Action<string?, int> onDone,
        Action<Process?>? onStarted = null,
        CancellationToken ct = default)
    {
        var collected = new List<string>();
        var lastFull = "";
        var stderrBuf = new StringBuilder();
        Process? process = null;

        try
        {
            process = Process.Start(psi);
            onStarted?.Invoke(process);
            if (process is null)
            {
                onDone(Loc.Get("agent.start_failed"), 1);
                return;
            }

            var stderrTask = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var errLine = await process.StandardError.ReadLineAsync(ct);
                        if (errLine is null) break;
                        stderrBuf.AppendLine(errLine);
                    }
                }
                catch (OperationCanceledException) { }
            }, ct);

            while (!ct.IsCancellationRequested)
            {
                string? line;
                try
                {
                    using var lineCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lineCts.CancelAfter(TimeSpan.FromMinutes(2));
                    line = await process.StandardOutput.ReadLineAsync(lineCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    onStatus(Loc.Get("agent.still_working"));
                    continue;
                }

                if (line is null) break;
                line = line.Trim();
                if (line.Length == 0) continue;

                var evt = parseLine(line);
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
            await process.WaitForExitAsync(ct);
            var code = process.ExitCode;
            var stderr = stderrBuf.ToString();
            var final = string.Concat(collected).Trim();
            if (code != 0 && string.IsNullOrEmpty(final))
                final = string.IsNullOrWhiteSpace(stderr)
                    ? Loc.Format("agent.error_code", code)
                    : stderr.Trim();

            onDone(string.IsNullOrEmpty(final) ? null : final, code);
        }
        catch (OperationCanceledException)
        {
            try { process?.Kill(entireProcessTree: true); } catch { }
            onDone(null, -1);
        }
        catch (Exception ex)
        {
            onDone(ex.Message, 1);
        }
        finally
        {
            onStarted?.Invoke(null);
        }
    }

    public static ProcessStartInfo CreateRedirectedPsi(string fileName, params string[] prefixArgs)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in prefixArgs)
            psi.ArgumentList.Add(arg);
        return psi;
    }

    public static async Task<(int ExitCode, string Output)> RunCaptureAsync(ProcessStartInfo psi)
    {
        using var process = Process.Start(psi);
        if (process is null)
            return (-1, "");

        var output = await process.StandardOutput.ReadToEndAsync();
        output += await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}

internal readonly record struct AgentStreamUpdate(
    string? Delta,
    string LastFull,
    string? Thinking,
    string? Status);
