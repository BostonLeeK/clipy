using System.Text.Json;
using System.Text.Json.Serialization;
using Clipy.Helpers;
using Clipy.Models;
using Windows.Graphics;

namespace Clipy.Services;

public sealed class ConfigService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipyAssistant");
    private static readonly string PathFile = Path.Combine(Dir, "config.json");
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(PathFile)) return Normalize(new AppConfig());
            var json = File.ReadAllText(PathFile);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
            return Normalize(config);
        }
        catch
        {
            return Normalize(new AppConfig());
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(PathFile, JsonSerializer.Serialize(config, JsonOpts));
    }

    private static AppConfig Normalize(AppConfig config)
    {
        var w = config.Expanded ? WindowHelper.PanelWidth : WindowHelper.OrbSize;
        var h = config.Expanded ? WindowHelper.PanelHeight : WindowHelper.OrbSize;

        if (config.WindowX is not int x || config.WindowY is not int y
            || !WindowHelper.IsOnScreen(new PointInt32(x, y), w, h))
        {
            var pos = config.Expanded
                ? WindowHelper.PanelFromOrb(WindowHelper.DefaultOrbPosition())
                : WindowHelper.DefaultOrbPosition();
            config.WindowX = pos.X;
            config.WindowY = pos.Y;
        }

        if (string.IsNullOrWhiteSpace(config.Workspace) || !Directory.Exists(config.Workspace))
            config.Workspace = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(config.AgentPath) || !File.Exists(config.AgentPath))
        {
            config.AgentPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cursor-agent", "agent.cmd");
        }

        if (string.IsNullOrWhiteSpace(config.ThemeId))
            config.ThemeId = "default";

        if (string.IsNullOrWhiteSpace(config.ModelId))
            config.ModelId = "auto";

        if (string.IsNullOrWhiteSpace(config.AgentMode))
            config.AgentMode = "agent";
        else
            config.AgentMode = config.AgentMode.ToLowerInvariant() switch
            {
                "ask" => "ask",
                "plan" => "plan",
                _ => "agent",
            };

        config.RecentWorkspaces = config.RecentWorkspaces
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (!config.RecentWorkspaces.Contains(config.Workspace, StringComparer.OrdinalIgnoreCase))
            config.RecentWorkspaces.Insert(0, config.Workspace);
        config.RecentWorkspaces = config.RecentWorkspaces.Take(5).ToList();

        return config;
    }
}
