using IconGen;

var root = FindRepoRoot();
var assetsDir = Path.Combine(root, "Clipy", "Assets");
var installerDir = Path.Combine(root, "installer");
var renderer = new NeonMascotRenderer();

Directory.CreateDirectory(assetsDir);
Directory.CreateDirectory(installerDir);

AppIconRenderer.SaveIco(renderer, Path.Combine(assetsDir, "clipy.ico"), 256, 128, 64, 48, 32, 16);
AppIconRenderer.SaveIco(renderer, Path.Combine(installerDir, "clipy.ico"), 256, 128, 64, 48, 32, 16);
AppIconRenderer.SavePng(renderer, Path.Combine(assetsDir, "Square44x44Logo.png"), 44);
AppIconRenderer.SavePng(renderer, Path.Combine(assetsDir, "Square150x150Logo.png"), 150);

Console.WriteLine($"Generated icons in {assetsDir} and {installerDir}");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "Clipy"))
            && File.Exists(Path.Combine(dir.FullName, "version.txt")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    throw new InvalidOperationException("Repository root not found.");
}
