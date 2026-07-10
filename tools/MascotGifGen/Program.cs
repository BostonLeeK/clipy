using System.Drawing.Imaging;
using Clipy.Themes.Packs.Grain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using SixLabors.ImageSharp.PixelFormats;
using SD = System.Drawing;

var root = FindRepoRoot();
var outDir = Path.Combine(root, "docs");
Directory.CreateDirectory(outDir);

const int size = 160;
const int frames = 24;
const int delayCs = 8;
var background = SD.Color.FromArgb(0x1A, 0x1A, 0x1A);

var renderer = new GrainMascotRenderer();
var rendered = new List<Image<Rgba32>>(frames);

for (var i = 0; i < frames; i++)
{
    using var bitmap = renderer.Render(size, i * 0.33, animateGrain: false);
    rendered.Add(FlattenBitmap(bitmap, background));
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

var encoder = new GifEncoder
{
    ColorTableMode = GifColorTableMode.Global,
    Quantizer = new WuQuantizer(new QuantizerOptions
    {
        MaxColors = 256,
        Dither = null,
    }),
};

var outPath = Path.Combine(outDir, "mascot-grain.gif");
await animation.SaveAsGifAsync(outPath, encoder);

Console.WriteLine($"Wrote {outPath}");

static Image<Rgba32> FlattenBitmap(SD.Bitmap bitmap, SD.Color background)
{
    using var flat = new SD.Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
    using (var g = SD.Graphics.FromImage(flat))
    {
        g.Clear(background);
        g.DrawImage(bitmap, 0, 0);
    }

    using var ms = new MemoryStream();
    flat.Save(ms, ImageFormat.Png);
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
