using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Clipy.Controls;

public sealed class ChatInputBox : TextBox
{
    public event EventHandler? SubmitRequested;
    public event EventHandler? ScreenshotRequested;

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (ctrl && shift && e.Key == VirtualKey.S)
        {
            e.Handled = true;
            ScreenshotRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (ctrl && e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            SubmitRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnKeyDown(e);
    }
}
