using System.Diagnostics;
using System.Text.Json;
using Clipy.Localization;
using Clipy.Models;

namespace Clipy.Services.Agents;

public static class AgentProviders
{
    public const string Cursor = "cursor";
    public const string Codex = "codex";
    public const string Claude = "claude";
}
