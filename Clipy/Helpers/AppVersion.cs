using System.Reflection;

namespace Clipy.Helpers;

public static class AppVersion
{
    public static string Current =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    public static string Display => SemVer(Current);

    public static bool IsNewer(string remote, string local) => Parse(remote) > Parse(local);

    public static string Normalize(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];
        return trimmed;
    }

    public static string SemVer(string version)
    {
        var normalized = Normalize(version);
        var plus = normalized.IndexOf('+');
        if (plus >= 0)
            normalized = normalized[..plus];
        var minus = normalized.IndexOf('-');
        if (minus >= 0)
            normalized = normalized[..minus];
        return normalized.Trim();
    }

    public static Version Parse(string version)
    {
        var core = SemVer(version);
        return Version.TryParse(core, out var parsed)
            ? parsed
            : new Version(0, 0, 0);
    }
}
