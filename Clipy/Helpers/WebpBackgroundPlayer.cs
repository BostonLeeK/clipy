using Microsoft.UI.Dispatching;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Windows.Storage.Streams;
using XamlImage = Microsoft.UI.Xaml.Controls.Image;
using SharpImage = SixLabors.ImageSharp.Image;

namespace Clipy.Helpers;

public interface IHomeBackgroundPlayer : IDisposable
{
    void Start();
    void Stop();
}

public static class HomeBackgroundFiles
{
    public static bool IsGif(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[6];
            return fs.Read(header) >= 6
                && header[0] == (byte)'G'
                && header[1] == (byte)'I'
                && header[2] == (byte)'F';
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWebp(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            if (fs.Read(header) < 12) return false;
            return header[0] == (byte)'R'
                && header[1] == (byte)'I'
                && header[2] == (byte)'F'
                && header[3] == (byte)'F'
                && header[8] == (byte)'W'
                && header[9] == (byte)'E'
                && header[10] == (byte)'B'
                && header[11] == (byte)'P';
        }
        catch
        {
            return false;
        }
    }
}

public sealed class WebpBackgroundPlayer : IHomeBackgroundPlayer
{
    private const int MaxDecodeWidth = 960;
    private const int MinFrameDelayMs = 80;

    private readonly SharpImage _image;
    private readonly XamlImage _target;
    private readonly DispatcherQueueTimer _timer;
    private int _frameIndex = -1;
    private bool _running;
    private bool _advancing;
    private bool _disposed;

    private WebpBackgroundPlayer(XamlImage target, SharpImage image, DispatcherQueue dispatcher)
    {
        _target = target;
        _image = image;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(MinFrameDelayMs);
        _timer.Tick += (_, _) => AdvanceFrame();
    }

    public static SharpImage? LoadImage(string path)
    {
        try
        {
            var image = SharpImage.Load(path);
            if (image.Width > MaxDecodeWidth)
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(MaxDecodeWidth, 0),
                    Mode = ResizeMode.Max,
                }));
            }

            return image;
        }
        catch
        {
            return null;
        }
    }

    public static WebpBackgroundPlayer? TryCreate(XamlImage target, SharpImage image, DispatcherQueue dispatcher)
    {
        if (image.Frames.Count == 0)
            return null;

        return new WebpBackgroundPlayer(target, image, dispatcher);
    }

    public void Start()
    {
        if (_disposed || _image.Frames.Count == 0) return;
        _running = true;
        AdvanceFrame();
        if (_image.Frames.Count > 1)
            _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _image.Dispose();
    }

    private void AdvanceFrame()
    {
        if (_disposed || !_running || _advancing || _image.Frames.Count == 0) return;

        _advancing = true;
        try
        {
            _frameIndex = (_frameIndex + 1) % _image.Frames.Count;
            using var frame = _image.Frames.CloneFrame(_frameIndex);
            using var ms = new MemoryStream();
            frame.Save(ms, PngFormat.Instance);
            ms.Position = 0;
            var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage
            {
                DecodePixelWidth = MaxDecodeWidth,
            };
            image.SetSource(ms.AsRandomAccessStream());
            if (_disposed) return;
            _target.Source = image;
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(MinFrameDelayMs, FrameDelayMs(_frameIndex)));
        }
        catch
        {
            Stop();
            if (!_disposed)
                _target.Source = null;
        }
        finally
        {
            _advancing = false;
        }
    }

    private int FrameDelayMs(int frameIndex)
    {
        var frame = _image.Frames[frameIndex];
        if (frame.Metadata.TryGetGifMetadata(out var gif) && gif.FrameDelay > 0)
            return Math.Max(MinFrameDelayMs, gif.FrameDelay * 10);
        return MinFrameDelayMs;
    }
}
