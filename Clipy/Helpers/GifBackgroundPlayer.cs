using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using XamlImage = Microsoft.UI.Xaml.Controls.Image;

namespace Clipy.Helpers;

public sealed class GifBackgroundPlayer : IHomeBackgroundPlayer
{
    private readonly Image _gif;
    private readonly XamlImage _target;
    private readonly DispatcherQueue _dispatcher;
    private readonly EventHandler _onFrameChanged;
    private readonly object _sync = new();
    private bool _disposed;
    private bool _running;

    public static bool CanPlay(string path) => HomeBackgroundFiles.IsGif(path);

    public GifBackgroundPlayer(XamlImage target, string path, DispatcherQueue dispatcher)
    {
        if (!CanPlay(path))
            throw new InvalidOperationException("Not a valid GIF file.");

        _target = target;
        _dispatcher = dispatcher;
        _gif = Image.FromFile(path);
        _onFrameChanged = OnFrameChanged;
        ImageAnimator.Animate(_gif, _onFrameChanged);
    }

    public void Start() => _running = true;

    public void Stop() => _running = false;

    public void Dispose()
    {
        _running = false;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            ImageAnimator.StopAnimate(_gif, _onFrameChanged);
            _gif.Dispose();
        }
    }

    private void OnFrameChanged(object? sender, EventArgs e)
    {
        if (!_running || _disposed) return;
        _dispatcher.TryEnqueue(UpdateFrame);
    }

    private void UpdateFrame()
    {
        if (!_running || _disposed) return;
        lock (GdiRenderLock.Sync)
        {
            if (!_running || _disposed) return;
            ImageAnimator.UpdateFrames(_gif);
            using var ms = new MemoryStream();
            _gif.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var image = new BitmapImage();
            image.SetSource(ms.AsRandomAccessStream());
            _target.Source = image;
        }
    }
}
