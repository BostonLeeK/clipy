namespace Clipy.Services.Agents;

internal static class CliResolver
{
    public static string Resolve(string? configured, params string[] names)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        foreach (var name in names)
        {
            var found = FindOnPath(name);
            if (found is not null)
                return found;
        }

        return string.IsNullOrWhiteSpace(configured) ? names[0] : configured;
    }

    public static bool Exists(string path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static string? FindOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            return null;

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var ext in extensions)
                {
                    var candidate = Path.Combine(dir, name + ext);
                    if (File.Exists(candidate))
                        return candidate;
                }

                var exact = Path.Combine(dir, name);
                if (File.Exists(exact))
                    return exact;
            }
            catch
            {
            }
        }

        return null;
    }
}
