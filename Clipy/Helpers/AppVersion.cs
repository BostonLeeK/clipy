using System.Reflection;

namespace Clipy.Helpers;

public static class AppVersion
{
    public static string Current =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    public static bool IsNewer(string remote, string local) => Parse(remote) > Parse(local);

    public static string Normalize(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];
        return trimmed;
    }

    public static Version Parse(string version)
    {
        var normalized = Normalize(version);
        return Version.TryParse(normalized, out var parsed)
            ? parsed
            : new Version(0, 0, 0);
    }
}
