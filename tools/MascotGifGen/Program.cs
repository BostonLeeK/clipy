using System.Drawing.Imaging;
using Clipy.Themes.Packs.Grain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SD = System.Drawing;

var root = FindRepoRoot();
var outDir = Path.Combine(root, "docs");
Directory.CreateDirectory(outDir);

const int size = 160;
const int frames = 36;
const int delayCs = 6;

var renderer = new GrainMascotRenderer();
var rendered = new List<Image<Rgba32>>(frames);

for (var i = 0; i < frames; i++)
{
    using var bitmap = renderer.Render(size, i * 0.22);
    rendered.Add(BitmapToImage(bitmap));
}

using var animation = rendered[0].CloneAs<Rgba32>();
for (var i = 1; i < rendered.Count; i++)
{
    animation.Frames.AddFrame(rendered[i].Frames[0]);
    rendered[i].Dispose();
}
rendered[0].Dispose();

for (var i = 0; i < animation.Frames.Count; i++)
    animation.Frames[i].Metadata.GetGifMetadata().FrameDelay = delayCs;

animation.Metadata.GetGifMetadata().RepeatCount = 0;

var outPath = Path.Combine(outDir, "mascot-grain.gif");
await animation.SaveAsGifAsync(outPath, new GifEncoder());

Console.WriteLine($"Wrote {outPath}");

static Image<Rgba32> BitmapToImage(SD.Bitmap bitmap)
{
    using var ms = new MemoryStream();
    bitmap.Save(ms, ImageFormat.Png);
    ms.Position = 0;
    return Image.Load<Rgba32>(ms);
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "Clipy"))
            && File.Exists(Path.Combine(dir.FullName, "version.txt")))
            return dir.FullName;
        dir = dir.Parent;
    }

    throw new InvalidOperationException("Repository root not found.");
}
