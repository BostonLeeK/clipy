using Microsoft.UI.Dispatching;

namespace Clipy.Helpers;

public sealed class UiCoalescer
{
    private readonly DispatcherQueue _queue;
    private Action? _pending;
    private bool _scheduled;

    public UiCoalescer(DispatcherQueue queue) => _queue = queue;

    public void Enqueue(Action action)
    {
        _pending = action;
        if (_scheduled) return;
        _scheduled = true;
        _queue.TryEnqueue(DispatcherQueuePriority.Low, Drain);
    }

    public void EnqueueHigh(DispatcherQueueHandler handler) =>
        _queue.TryEnqueue(DispatcherQueuePriority.High, handler);

    private void Drain()
    {
        _scheduled = false;
        var action = _pending;
        _pending = null;
        action?.Invoke();
    }
}
