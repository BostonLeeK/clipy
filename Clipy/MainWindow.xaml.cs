using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Clipy.Helpers;
using Clipy.Models;
using Clipy.Services;
using Clipy.Themes;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;
using NativePoint = Clipy.Helpers.WindowHelper.Point;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;

namespace Clipy;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly ConfigService _configService = new();
    private readonly AppConfig _config;
    private readonly AgentService _agent;
    private readonly ThemeService _themes = new();
    private readonly TaskbarIcon _tray = new() { ToolTipText = "Clipy" };
    private readonly HotkeyService _hotkey = new();
    private readonly List<AttachmentItem> _attachments = new();
    private NativeOrbWindow? _orb;
    private CancellationTokenSource? _runCts;
    private bool _expanded;
    private bool _dragging;
    private NativePoint _dragScreenStart;
    private PointInt32 _dragWindowStart;
    private bool _hasMessages;
    private PointInt32 _orbPos;

    private static Mutex? _mutex;
    private FileSystemWatcher? _signalWatcher;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public MainWindow()
    {
        _mutex = new Mutex(true, "Global\\ClipyAssistant", out var created);
        if (!created)
        {
            SignalExisting();
            MessageBox(IntPtr.Zero, "Clipy вже працює.", "Clipy", 0x40);
            Environment.Exit(0);
            return;
        }

        InitializeComponent();
        Title = "";

        _config = _configService.Load();
        _agent = new AgentService(_config);
        _expanded = false;
        _themes.Apply(_config.ThemeId);
        _themes.ThemeChanged += OnThemeChanged;

        _appWindow = WindowHelper.GetAppWindow(this);
        WindowHelper.ConfigureWidget(this, _appWindow);
        WindowHelper.HideWindow(this);

        _orbPos = ResolveOrbPosition();
        CreateOrb();
        ShowOrbMode();

        RootGrid.PointerMoved += RootGrid_PointerMoved;
        RootGrid.PointerReleased += RootGrid_PointerReleased;
        RootGrid.PointerCaptureLost += RootGrid_PointerReleased;
        InputBox.SubmitRequested += async (_, _) => await SendAsync();
        InputBox.ScreenshotRequested += (_, _) => CaptureScreenshot();
        Closed += (_, _) =>
        {
            _hotkey.Dispose();
            _orb?.Dispose();
            _orb = null;
        };

        try
        {
            _hotkey.Register();
            _hotkey.Triggered += () => DispatcherQueue.TryEnqueue(ToggleFromHotkey);
        }
        catch
        {
            FooterStatus.Text = "Хоткей Ctrl+Shift+C зайнятий";
        }

        _ = RefreshStatusAsync();
        SetupTray();
        SetupSignalWatcher();
        RefreshThemeCards();
        ApplyPanelChromeColors();
        RefreshHeaderAvatar();
        RefreshFrameDecor();
    }

    private void OnThemeChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _orb?.SetRenderer(_themes.CurrentRenderer);
            ApplyPanelChromeColors();
            RefreshHeaderAvatar();
            RefreshFrameDecor();
            RefreshThemeCards();
        });
    }

    private void ApplyPanelChromeColors()
    {
        var theme = _themes.Current;
        var accent = new SolidColorBrush(theme.Accent);
        var card = new SolidColorBrush(theme.Card);
        var text = new SolidColorBrush(theme.Text);
        var muted = new SolidColorBrush(theme.Muted);
        var bg = new SolidColorBrush(theme.Background);

        PanelView.Background = bg;
        FrameBorder.Background = (Brush)Application.Current.Resources["PanelFrameGradient"];
        SettingsOverlay.Background = bg;
        StatusDot.Fill = accent;
        StatusText.Foreground = muted;

        if (HeaderBar.Children.OfType<Button>().ToList() is { Count: > 0 } headerButtons)
        {
            foreach (var btn in headerButtons)
            {
                btn.Background = card;
                if (btn.Content is FontIcon icon)
                    icon.Foreground = text;
            }
        }

        GreetingCard.Background = card;
        if (GreetingCard.Child is TextBlock greet)
            greet.Foreground = text;

        foreach (var child in QuickActions.Children)
        {
            if (child is not Button btn) continue;
            var isPrimary = QuickActions.Children.IndexOf(btn) == 0;
            if (isPrimary)
            {
                btn.Background = accent;
                btn.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x0B, 0x0B, 0x0F));
            }
            else
            {
                btn.Background = card;
                btn.Foreground = text;
            }
        }
    }

    private void RefreshHeaderAvatar()
    {
        try
        {
            using var bmp = _themes.CurrentRenderer.Render(64, 1.15);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var image = new BitmapImage();
            image.SetSource(ms.AsRandomAccessStream());
            HeaderAvatarImage.Source = image;
        }
        catch
        {
            HeaderAvatarImage.Source = null;
        }
    }

    private void RefreshFrameDecor()
    {
        if (!string.Equals(_themes.Current.Id, ThemeIds.Kawaii, StringComparison.OrdinalIgnoreCase))
        {
            FrameDecorImage.Source = null;
            FrameDecorImage.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            using var bmp = TotoroFrameDecorator.Render(WindowHelper.PanelWidth, WindowHelper.PanelHeight);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var image = new BitmapImage();
            image.SetSource(ms.AsRandomAccessStream());
            FrameDecorImage.Source = image;
            FrameDecorImage.Visibility = Visibility.Visible;
        }
        catch
        {
            FrameDecorImage.Source = null;
            FrameDecorImage.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshThemeCards()
    {
        ThemesPanel.Children.Clear();
        foreach (var theme in _themes.All)
        {
            var selected = string.Equals(theme.Id, _themes.Current.Id, StringComparison.OrdinalIgnoreCase);
            var card = new Border
            {
                Background = new SolidColorBrush(selected
                    ? Windows.UI.Color.FromArgb(0x33, theme.Accent.R, theme.Accent.G, theme.Accent.B)
                    : Windows.UI.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(selected
                    ? theme.Accent
                    : Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(selected ? 1.5 : 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10, 12, 10),
                Tag = theme.Id,
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var swatch = new Ellipse
            {
                Width = 22,
                Height = 22,
                Fill = new SolidColorBrush(theme.Accent),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(swatch, 0);

            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = theme.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(_themes.Current.Text),
            });
            text.Children.Add(new TextBlock
            {
                Text = theme.Description,
                FontSize = 11,
                Foreground = new SolidColorBrush(_themes.Current.Muted),
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(text, 1);

            var mark = new TextBlock
            {
                Text = selected ? "✓" : "",
                FontSize = 14,
                Foreground = new SolidColorBrush(theme.Accent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(mark, 2);

            row.Children.Add(swatch);
            row.Children.Add(text);
            row.Children.Add(mark);
            card.Child = row;
            card.Tapped += (_, _) => SelectTheme(theme.Id);
            ThemesPanel.Children.Add(card);
        }
    }

    private void SelectTheme(string themeId)
    {
        if (string.Equals(_config.ThemeId, themeId, StringComparison.OrdinalIgnoreCase))
            return;
        _config.ThemeId = themeId;
        _themes.Apply(themeId);
        Save();
        FooterStatus.Text = $"Тема: {_themes.Current.Name}";
    }

    private void ToggleFromHotkey()
    {
        if (_expanded)
            Collapse();
        else
            Expand();
    }

    private PointInt32 ResolveOrbPosition()
    {
        var pos = new PointInt32(_config.WindowX ?? 0, _config.WindowY ?? 0);
        if (!WindowHelper.IsOnScreen(pos, WindowHelper.OrbSize, WindowHelper.OrbSize))
            pos = WindowHelper.DefaultOrbPosition();
        return WindowHelper.Clamp(pos, WindowHelper.OrbSize, WindowHelper.OrbSize);
    }

    private void CreateOrb()
    {
        _orb?.Dispose();
        _orb = new NativeOrbWindow(WindowHelper.OrbSize, _orbPos, _themes.CurrentRenderer);
        _orb.Clicked += () => DispatcherQueue.TryEnqueue(Expand);
        _orb.Moved += pos => DispatcherQueue.TryEnqueue(() =>
        {
            _orbPos = pos;
            Save();
        });
    }

    private void ShowOrbMode()
    {
        _expanded = false;
        WindowHelper.HideWindow(this);
        if (_orb is null)
            CreateOrb();
        else
            _orb.Move(_orbPos);
        _orb!.Show();
        Save();
    }

    private void ShowPanelMode()
    {
        _expanded = true;
        _orb?.Hide();

        var panelPos = WindowHelper.PanelFromOrb(_orbPos);
        _appWindow.MoveAndResize(new RectInt32(
            panelPos.X, panelPos.Y, WindowHelper.PanelWidth, WindowHelper.PanelHeight));
        WindowHelper.ApplyPanelChrome(this, _appWindow);
        WindowHelper.ShowWindowTopmost(this);
        Activate();
        BringToFront();
        InputBox.Focus(FocusState.Programmatic);
        Save();
    }

    private void BringToFront()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        SetForegroundWindow(hwnd);
    }

    private void Expand()
    {
        if (_expanded) return;
        ShowPanelMode();
    }

    private void Collapse()
    {
        if (!_expanded) return;
        var panelPos = _appWindow.Position;
        _orbPos = WindowHelper.OrbFromPanel(panelPos);
        ShowOrbMode();
    }

    private void BeginDrag()
    {
        WindowHelper.GetCursorPos(out _dragScreenStart);
        _dragWindowStart = _appWindow.Position;
        _dragging = true;
    }

    private void Header_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        if (!e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed) return;
        BeginDrag();
        RootGrid.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || !_expanded) return;

        WindowHelper.GetCursorPos(out var cursor);
        var dx = cursor.X - _dragScreenStart.X;
        var dy = cursor.Y - _dragScreenStart.Y;

        var next = WindowHelper.Clamp(
            new PointInt32(_dragWindowStart.X + dx, _dragWindowStart.Y + dy),
            WindowHelper.PanelWidth, WindowHelper.PanelHeight);
        _appWindow.Move(next);
        e.Handled = true;
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        RootGrid.ReleasePointerCapture(e.Pointer);
        if (_expanded)
        {
            _orbPos = WindowHelper.OrbFromPanel(_appWindow.Position);
            Save();
        }
        e.Handled = true;
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void Input_Paste(object sender, TextControlPasteEventArgs e)
    {
        var files = await ScreenshotHelper.SaveClipboardFilesAsync();
        if (files.Count > 0)
        {
            e.Handled = true;
            foreach (var f in files)
                AddAttachment(f);
            return;
        }

        var image = await ScreenshotHelper.SaveClipboardImageAsync();
        if (image is not null)
        {
            e.Handled = true;
            AddAttachment(image, "image");
            return;
        }

        var text = await ScreenshotHelper.GetClipboardTextAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.Trim().Trim('"');
            if ((File.Exists(trimmed) || Directory.Exists(trimmed)) && trimmed.Length < 512)
            {
                e.Handled = true;
                AddAttachment(trimmed);
            }
        }
    }

    private void Input_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.Handled = true;
    }

    private async void Input_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var item in items)
            AddAttachment(item.Path);
    }

    private async void AttachFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        var files = await picker.PickMultipleFilesAsync();
        if (files is null) return;
        foreach (var file in files)
            AddAttachment(file.Path);
    }

    private void Screenshot_Click(object sender, RoutedEventArgs e) => CaptureScreenshot();

    private void CaptureScreenshot()
    {
        try
        {
            var wasExpanded = _expanded;
            if (wasExpanded)
                WindowHelper.HideWindow(this);

            var path = ScreenshotHelper.CapturePrimaryScreen();

            if (wasExpanded)
            {
                WindowHelper.ShowWindowTopmost(this);
                Activate();
            }

            AddAttachment(path, "image");
            FooterStatus.Text = "Скрін додано";
        }
        catch (Exception ex)
        {
            FooterStatus.Text = $"Скрін не вдався: {ex.Message}";
        }
    }

    private void AddAttachment(string path, string? kind = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.Trim().Trim('"');
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if (_attachments.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)))
            return;

        kind ??= IsImagePath(path) ? "image" : Directory.Exists(path) ? "folder" : "file";
        _attachments.Add(new AttachmentItem { Path = path, Kind = kind });
        RefreshAttachmentChips();
    }

    private void RemoveAttachment(AttachmentItem item)
    {
        _attachments.Remove(item);
        RefreshAttachmentChips();
    }

    private void ClearAttachments()
    {
        _attachments.Clear();
        RefreshAttachmentChips();
    }

    private void RefreshAttachmentChips()
    {
        AttachPanel.Children.Clear();
        AttachScroll.Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in _attachments.ToList())
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xC8, 0xFF, 0x4D)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0xC8, 0xFF, 0x4D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 4, 4, 4),
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var icon = item.Kind switch
            {
                "image" => "🖼",
                "folder" => "📁",
                _ => "📄",
            };
            row.Children.Add(new TextBlock
            {
                Text = $"{icon} {Truncate(item.DisplayName, 22)}",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            var remove = new Button
            {
                Content = "×",
                FontSize = 12,
                Padding = new Thickness(4, 0, 4, 0),
                MinWidth = 0,
                MinHeight = 0,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                Tag = item,
            };
            remove.Click += (_, _) =>
            {
                if (remove.Tag is AttachmentItem a)
                    RemoveAttachment(a);
            };
            row.Children.Add(remove);
            chip.Child = row;
            AttachPanel.Children.Add(chip);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    private static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    private async Task SendAsync()
    {
        var text = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(text) && _attachments.Count == 0) return;

        var attachments = _attachments.Select(a => a.Path).ToList();
        var display = text;
        if (attachments.Count > 0)
        {
            var names = string.Join(", ", attachments.Select(Path.GetFileName));
            display = string.IsNullOrEmpty(text)
                ? $"📎 {names}"
                : $"{text}\n📎 {names}";
        }

        InputBox.Text = "";
        ClearAttachments();
        if (!_expanded) Expand();
        HideGreeting();
        AddBubble(display, true);
        SetBusy(true);
        FooterStatus.Text = "Думаю…";

        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var turn = AddAssistantTurn();
        var gotAnswer = false;
        var thinkingLen = 0;
        var answerMarkdown = "";

        void Ui(Action action) => DispatcherQueue.TryEnqueue(() =>
        {
            action();
            ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight + 80, null);
        });

        void RenderAnswer(string markdown, bool error = false)
        {
            var fg = new SolidColorBrush(error
                ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x8A, 0x8A)
                : _themes.Current.Text);
            var muted = new SolidColorBrush(_themes.Current.Muted);
            var accent = new SolidColorBrush(_themes.Current.Accent);
            MarkdownRenderer.SetMarkdown(turn.Answer, markdown, fg, muted, accent);
        }

        await _agent.RunPromptAsync(
            string.IsNullOrEmpty(text) ? "Analyze the attached context." : text,
            s => Ui(() =>
            {
                FooterStatus.Text = s;
                if (!gotAnswer)
                    turn.Status.Text = s;
            }),
            chunk => Ui(() =>
            {
                thinkingLen += chunk.Length;
                turn.Thinking.Visibility = Visibility.Visible;
                if (thinkingLen < 900)
                    turn.Thinking.Text += chunk;
                else if (!turn.Thinking.Text.EndsWith("…", StringComparison.Ordinal))
                    turn.Thinking.Text += "…";
                if (!gotAnswer)
                    turn.Status.Text = "Думаю…";
            }),
            chunk => Ui(() =>
            {
                if (!gotAnswer)
                {
                    gotAnswer = true;
                    turn.Status.Visibility = Visibility.Collapsed;
                    if (turn.Thinking.Visibility == Visibility.Visible)
                        turn.Thinking.Opacity = 0.55;
                }
                answerMarkdown += chunk;
                RenderAnswer(answerMarkdown);
            }),
            (final, code) =>
            {
                Ui(() =>
                {
                    SetBusy(false);
                    turn.Status.Visibility = Visibility.Collapsed;
                    if (code == 0)
                    {
                        FooterStatus.Text = "Готово";
                        SetOnline(true);
                        _config.ChatId = _agent.ChatId;
                        if (string.IsNullOrWhiteSpace(answerMarkdown))
                            answerMarkdown = string.IsNullOrEmpty(final) ? "(порожня відповідь)" : final;
                        RenderAnswer(answerMarkdown);
                    }
                    else if (code != -1)
                    {
                        FooterStatus.Text = "Помилка — перевір вхід";
                        SetOnline(false);
                        if (string.IsNullOrWhiteSpace(answerMarkdown))
                            answerMarkdown = string.IsNullOrEmpty(final)
                                ? "Не вдалося отримати відповідь"
                                : final;
                        RenderAnswer(answerMarkdown, error: true);
                    }
                    else
                    {
                        FooterStatus.Text = "Скасовано";
                        if (string.IsNullOrWhiteSpace(answerMarkdown))
                            answerMarkdown = "Скасовано";
                        RenderAnswer(answerMarkdown);
                    }
                    Save();
                });
            },
            attachments,
            _runCts.Token);
    }

    private sealed class AssistantTurn
    {
        public required TextBlock Status { get; init; }
        public required TextBlock Thinking { get; init; }
        public required RichTextBlock Answer { get; init; }
    }

    private AssistantTurn AddAssistantTurn()
    {
        var accent = _themes.Current.Accent;
        var stack = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 320,
        };

        var status = new TextBlock
        {
            Text = "Думаю…",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
        };

        var thinking = new TextBlock
        {
            Text = "",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            Foreground = new SolidColorBrush(_themes.Current.Muted),
            Visibility = Visibility.Collapsed,
            MaxHeight = 120,
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(
                0x55, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10, 12, 10),
            MinWidth = 120,
            MinHeight = 40,
        };

        var body = new StackPanel { Spacing = 6 };
        var answer = new RichTextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
        };
        answer.Blocks.Add(new Paragraph
        {
            Inlines =
            {
                new Run
                {
                    Text = "⏳ Чекаю відповідь агента…",
                    Foreground = new SolidColorBrush(_themes.Current.Muted),
                },
            },
        });
        body.Children.Add(status);
        body.Children.Add(thinking);
        body.Children.Add(answer);
        border.Child = body;
        stack.Children.Add(border);
        ChatPanel.Children.Add(stack);
        ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight + 80, null);
        return new AssistantTurn { Status = status, Thinking = thinking, Answer = answer };
    }

    private TextBlock AddBubble(string text, bool isUser)
    {
        var accent = _themes.Current.Accent;
        var border = new Border
        {
            Background = new SolidColorBrush(isUser
                ? Windows.UI.Color.FromArgb(0x26, accent.R, accent.G, accent.B)
                : Windows.UI.Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(isUser
                ? Windows.UI.Color.FromArgb(0x40, accent.R, accent.G, accent.B)
                : Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 300,
        };
        var tb = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(_themes.Current.Text),
        };
        border.Child = tb;
        ChatPanel.Children.Add(border);
        ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null);
        return tb;
    }

    private void HideGreeting()
    {
        if (_hasMessages) return;
        _hasMessages = true;
        GreetingCard.Visibility = Visibility.Collapsed;
        QuickActions.Visibility = Visibility.Collapsed;
    }

    private bool _busy;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InputBox.IsEnabled = !busy;
        if (busy)
            StatusText.Text = "Думає…";
    }

    private void SetOnline(bool online)
    {
        StatusDot.Fill = new SolidColorBrush(online
            ? _themes.Current.Accent
            : Windows.UI.Color.FromArgb(0xFF, 0x5C, 0x5C, 0x6E));
        if (!_busy)
            StatusText.Text = online ? "Online" : "Offline";
    }

    private async Task RefreshStatusAsync()
    {
        var status = await _agent.CheckStatusAsync();
        WorkspaceText.Text = _agent.Workspace;
        SetOnline(_agent.IsLoggedIn(status));
        if (status is "missing" or "logged_out")
        {
            FooterStatus.Text = status switch
            {
                "missing" => "Встанови Cursor Agent",
                "logged_out" => "Увійди в налаштування",
                _ => FooterStatus.Text,
            };
        }
        await UpdateSettingsAuthUiAsync(status);
    }

    private async Task UpdateSettingsAuthUiAsync(string? status = null)
    {
        status ??= await _agent.CheckStatusAsync();
        var loggedIn = _agent.IsLoggedIn(status);

        SettingsAuthStatus.Text = status switch
        {
            "ready" => "Увійшов у Cursor",
            "logged_out" => "Не увійшов",
            "missing" => "Cursor Agent не встановлено",
            _ => "Не вдалося перевірити статус",
        };

        SettingsLoginBtn.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        SettingsLogoutBtn.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        SettingsWorkspaceText.Text = _agent.Workspace;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Visible;
        RefreshThemeCards();
        _ = UpdateSettingsAuthUiAsync();
        _ = LoadModelsAsync();
    }

    private bool _loadingModels;

    private async Task LoadModelsAsync()
    {
        _loadingModels = true;
        try
        {
            var models = await _agent.ListModelsAsync();
            SettingsModelCombo.Items.Clear();
            var selectedIndex = 0;
            for (var i = 0; i < models.Count; i++)
            {
                var m = models[i];
                SettingsModelCombo.Items.Add(new ComboBoxItem
                {
                    Content = m.Name,
                    Tag = m.Id,
                });
                if (string.Equals(m.Id, _config.ModelId, StringComparison.OrdinalIgnoreCase))
                    selectedIndex = i;
            }
            if (SettingsModelCombo.Items.Count > 0)
                SettingsModelCombo.SelectedIndex = selectedIndex;
        }
        finally
        {
            _loadingModels = false;
        }
    }

    private void SettingsModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingModels) return;
        if (SettingsModelCombo.SelectedItem is not ComboBoxItem item) return;
        var id = item.Tag as string;
        if (string.IsNullOrWhiteSpace(id)) return;
        if (string.Equals(_config.ModelId, id, StringComparison.OrdinalIgnoreCase)) return;
        _config.ModelId = id;
        Save();
        FooterStatus.Text = $"Модель: {item.Content}";
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void SettingsLogin_Click(object sender, RoutedEventArgs e)
    {
        _agent.Login();
        FooterStatus.Text = "Заверши вхід у браузері";
    }

    private async void SettingsLogout_Click(object sender, RoutedEventArgs e)
    {
        await _agent.LogoutAsync();
        _config.ChatId = null;
        Save();
        await RefreshStatusAsync();
        FooterStatus.Text = "Вийшов з Cursor";
    }

    private async void NewChat_Click(object sender, RoutedEventArgs e)
    {
        await _agent.NewChatAsync();
        ChatPanel.Children.Clear();
        _hasMessages = false;
        GreetingCard.Visibility = Visibility.Visible;
        QuickActions.Visibility = Visibility.Visible;
        ClearAttachments();
        _config.ChatId = null;
        Save();
    }

    private async void Folder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            _agent.Workspace = folder.Path;
            _config.Workspace = folder.Path;
            WorkspaceText.Text = folder.Path;
            SettingsWorkspaceText.Text = folder.Path;
            Save();
        }
    }

    private void SetupTray()
    {
        var menu = new MenuFlyout();
        _tray.ContextFlyout = menu;
        var show = new MenuFlyoutItem { Text = "Показати (Ctrl+Shift+C)" };
        show.Click += (_, _) => Expand();
        var hide = new MenuFlyoutItem { Text = "Сховати" };
        hide.Click += (_, _) => Collapse();
        var shot = new MenuFlyoutItem { Text = "Скрін + відкрити" };
        shot.Click += (_, _) =>
        {
            Expand();
            CaptureScreenshot();
        };
        var reset = new MenuFlyoutItem { Text = "На екран" };
        reset.Click += (_, _) =>
        {
            _orbPos = WindowHelper.DefaultOrbPosition();
            if (_expanded)
            {
                var panelPos = WindowHelper.PanelFromOrb(_orbPos);
                _appWindow.MoveAndResize(new RectInt32(
                    panelPos.X, panelPos.Y, WindowHelper.PanelWidth, WindowHelper.PanelHeight));
            }
            else
            {
                _orb?.Move(_orbPos);
            }
            Save();
        };
        var quit = new MenuFlyoutItem { Text = "Вихід" };
        quit.Click += (_, _) =>
        {
            _hotkey.Dispose();
            _orb?.Dispose();
            _orb = null;
            Application.Current.Exit();
        };
        menu.Items.Add(show);
        menu.Items.Add(hide);
        menu.Items.Add(shot);
        menu.Items.Add(reset);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(quit);
        _tray.ForceCreate();
    }

    private void Save()
    {
        _config.WindowX = _orbPos.X;
        _config.WindowY = _orbPos.Y;
        _config.Expanded = _expanded;
        _config.Workspace = _agent.Workspace;
        _config.ChatId = _agent.ChatId;
        _config.ThemeId = _themes.Current.Id;
        _config.ModelId = string.IsNullOrWhiteSpace(_config.ModelId) ? "auto" : _config.ModelId;
        _configService.Save(_config);
    }

    private void SetupSignalWatcher()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipyAssistant");
        Directory.CreateDirectory(dir);

        _signalWatcher = new FileSystemWatcher(dir, "toggle.signal")
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };
        _signalWatcher.Changed += OnSignal;
        _signalWatcher.Created += OnSignal;

        var signal = Path.Combine(dir, "toggle.signal");
        if (File.Exists(signal))
        {
            try { File.Delete(signal); } catch { /* ignore */ }
        }
    }

    private void OnSignal(object sender, FileSystemEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_expanded) Expand();
            BringToFront();
            Activate();
            try { File.Delete(e.FullPath); } catch { /* ignore */ }
        });
    }

    private static void SignalExisting()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipyAssistant", "toggle.signal");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "1");
    }
}
