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
    private readonly ChatHistoryService _chatHistory = new();
    private readonly SpeechInputService _speech = new();
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
    private bool _homeMode = true;
    private bool _userCancelledRun;
    private PointInt32 _orbPos;
    private ChatSession? _currentSession;
    private DispatcherTimer? _mascotResetTimer;
    private bool _loadingMode;
    private bool _loadingRecentWorkspace;

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
        UpdateScreenLayout();
        SelectModeInSettings();
        RefreshRecentWorkspaces();
        RestoreSessionIfAny();
        UpdateScreenLayout();
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
            UpdateScreenLayout();
        });
    }

    private void ShowHomeScreen()
    {
        _homeMode = true;
        UpdateScreenLayout();
    }

    private void ShowChatScreen()
    {
        _homeMode = false;
        UpdateScreenLayout();
        ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null);
    }

    private void UpdateScreenLayout()
    {
        var onHome = (_homeMode || !_hasMessages) && !_busy;
        GreetingCard.Visibility = onHome ? Visibility.Visible : Visibility.Collapsed;
        QuickActions.Visibility = onHome ? Visibility.Visible : Visibility.Collapsed;
        ChatScroll.Visibility = onHome ? Visibility.Collapsed : Visibility.Visible;
        OpenChatBtn.Visibility = onHome && _hasMessages ? Visibility.Visible : Visibility.Collapsed;
        HomeBtn.Visibility = _hasMessages && !onHome ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Home_Click(object sender, RoutedEventArgs e) => ShowHomeScreen();

    private void OpenChat_Click(object sender, RoutedEventArgs e) => ShowChatScreen();

    private void ApplyPanelChromeColors()
    {
        var theme = _themes.Current;
        var accent = new SolidColorBrush(theme.Accent);
        var card = new SolidColorBrush(theme.Card);
        var text = new SolidColorBrush(theme.Text);
        var muted = new SolidColorBrush(theme.Muted);
        var bg = new SolidColorBrush(theme.Background);
        var border = new SolidColorBrush(theme.Border);
        var subtleBorder = ThemeSubtleBorder(theme);
        var inputBg = ThemeInputBackground(theme);
        var onAccent = ThemeOnAccent(theme);

        PanelView.Background = bg;
        PanelView.BorderBrush = subtleBorder;
        FrameBorder.Background = (Brush)Application.Current.Resources["PanelFrameGradient"];
        HistoryOverlay.Background = bg;
        SettingsOverlay.Background = bg;
        StatusDot.Fill = accent;
        StatusText.Foreground = muted;
        FooterStatus.Foreground = muted;
        WorkspaceText.Foreground = muted;
        InputBox.Background = inputBg;
        InputBox.Foreground = text;
        InputBox.BorderBrush = border;

        ApplyOverlayHeader(HistoryTitleText, HistorySubtitleText, HistoryBackBtn, text, muted, card, accent);
        ApplyOverlayHeader(SettingsTitleText, SettingsSubtitleText, SettingsBackBtn, text, muted, card, accent);

        StyleSettingsCard(SettingsAuthCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsModeCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsModelCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsWorkspaceCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsThemesCard, text, muted, card, subtleBorder);

        SettingsAuthStatus.Foreground = muted;
        SettingsWorkspaceText.Foreground = muted;
        SettingsLoginBtn.Background = accent;
        SettingsLoginBtn.Foreground = onAccent;
        SettingsLogoutBtn.Background = ThemeLogoutBackground(theme);
        SettingsLogoutBtn.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xFF, 0x8A, 0x8A));
        SettingsFolderBtn.Background = card;
        SettingsFolderBtn.Foreground = text;
        StyleComboBox(SettingsModeCombo, card, text, border);
        StyleComboBox(SettingsModelCombo, card, text, border);
        StyleComboBox(SettingsRecentWorkspaceCombo, card, text, border);

        HistoryEmptyIcon.Text = string.Equals(theme.Id, ThemeIds.Kawaii, StringComparison.OrdinalIgnoreCase)
            ? "🌿"
            : "💬";
        HistoryEmptyCard.Background = card;
        HistoryEmptyCard.BorderBrush = subtleBorder;
        HistoryEmptyTitle.Foreground = text;
        HistoryEmptyHint.Foreground = muted;

        if (HeaderBar.Children.OfType<Button>().ToList() is { Count: > 0 } headerButtons)
        {
            foreach (var btn in headerButtons)
            {
                btn.Background = card;
                if (btn.Content is FontIcon icon)
                    icon.Foreground = btn == HomeBtn ? accent : text;
            }
        }

        GreetingCard.Background = card;
        GreetingCard.BorderBrush = subtleBorder;
        if (GreetingCard.Child is TextBlock greet)
            greet.Foreground = text;

        foreach (var child in QuickActions.Children)
        {
            if (child is not Button btn) continue;
            var isPrimary = btn == QuickActions.Children[0];
            if (isPrimary)
            {
                btn.Background = accent;
                btn.Foreground = onAccent;
            }
            else
            {
                btn.Background = card;
                btn.Foreground = text;
            }
        }

        if (!_busy)
            SendButton.Background = accent;

        UpdateScreenLayout();
    }

    private static void ApplyOverlayHeader(
        TextBlock title,
        TextBlock subtitle,
        Button backBtn,
        SolidColorBrush text,
        SolidColorBrush muted,
        SolidColorBrush card,
        SolidColorBrush accent)
    {
        title.Foreground = text;
        subtitle.Foreground = muted;
        backBtn.Background = card;
        if (backBtn.Content is FontIcon icon)
            icon.Foreground = accent;
    }

    private static void StyleSettingsCard(
        Border cardBorder,
        SolidColorBrush text,
        SolidColorBrush muted,
        SolidColorBrush card,
        SolidColorBrush subtleBorder)
    {
        cardBorder.Background = card;
        cardBorder.BorderBrush = subtleBorder;
        if (cardBorder.Child is not StackPanel panel) return;
        foreach (var child in panel.Children)
        {
            if (child is TextBlock tb)
                tb.Foreground = tb.FontSize <= 11 && tb.FontWeight != Microsoft.UI.Text.FontWeights.SemiBold ? muted : text;
        }
    }

    private static void StyleComboBox(ComboBox combo, SolidColorBrush bg, SolidColorBrush fg, SolidColorBrush border)
    {
        combo.Background = bg;
        combo.Foreground = fg;
        combo.BorderBrush = border;
    }

    private static SolidColorBrush ThemeSubtleBorder(ThemePalette theme)
    {
        var c = theme.Accent;
        return new SolidColorBrush(Windows.UI.Color.FromArgb(0x44, c.R, c.G, c.B));
    }

    private static SolidColorBrush ThemeInputBackground(ThemePalette theme)
    {
        var bg = theme.Background;
        var card = theme.Card;
        return new SolidColorBrush(Windows.UI.Color.FromArgb(
            255,
            (byte)((bg.R + card.R) / 2),
            (byte)((bg.G + card.G) / 2),
            (byte)((bg.B + card.B) / 2)));
    }

    private static SolidColorBrush ThemeOnAccent(ThemePalette theme) =>
        string.Equals(theme.Id, ThemeIds.Kawaii, StringComparison.OrdinalIgnoreCase)
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x12, 0x18, 0x10))
            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x0B, 0x0B, 0x0F));

    private static SolidColorBrush ThemeLogoutBackground(ThemePalette theme)
    {
        var card = theme.Card;
        return new SolidColorBrush(Windows.UI.Color.FromArgb(
            255,
            (byte)Math.Min(255, card.R + 20),
            (byte)Math.Max(0, card.G - 30),
            (byte)Math.Max(0, card.B - 30)));
    }

    private Border MakeHistorySessionCard(ChatSession session)
    {
        var theme = _themes.Current;
        var accent = theme.Accent;
        var card = new Border
        {
            Background = new SolidColorBrush(theme.Card),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x55, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(3, 1, 1, 1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Tag = session,
        };
        var col = new StackPanel { Spacing = 4 };
        col.Children.Add(new TextBlock
        {
            Text = session.Title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(theme.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        col.Children.Add(new TextBlock
        {
            Text = $"{session.UpdatedAt.ToLocalTime():g} · {session.Messages.Count} повід.",
            FontSize = 11,
            Foreground = new SolidColorBrush(theme.Muted),
        });
        card.Child = col;
        card.Tapped += (_, _) =>
        {
            if (card.Tag is ChatSession s)
            {
                LoadSession(s);
                HistoryOverlay.Visibility = Visibility.Collapsed;
            }
        };
        return card;
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
            HistoryAvatarImage.Source = image;
            SettingsAvatarImage.Source = image;
        }
        catch
        {
            HeaderAvatarImage.Source = null;
            HistoryAvatarImage.Source = null;
            SettingsAvatarImage.Source = null;
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

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            CancelRun();
            return;
        }
        await SendAsync();
    }

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
        EnterChatMode();
        AddBubble(display, true);
        EnsureSession().Messages.Add(new ChatMessage { Role = "user", Text = display });
        PersistSession(display);
        SetBusy(true);
        FooterStatus.Text = "Думаю…";

        _userCancelledRun = false;
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        _runCts.CancelAfter(TimeSpan.FromMinutes(10));
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
            var card = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            turn.AnswerHost.Children.Clear();
            MarkdownRenderer.Populate(turn.AnswerHost, markdown, fg, muted, accent, card);
            turn.AnswerMarkdown = markdown;
            turn.CopyBtn.Visibility = string.IsNullOrWhiteSpace(markdown)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        try
        {
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
                        if (!string.IsNullOrWhiteSpace(final))
                            answerMarkdown = final;
                        else if (string.IsNullOrWhiteSpace(answerMarkdown))
                            answerMarkdown = "(порожня відповідь)";
                        RenderAnswer(answerMarkdown);
                        AppendAssistantHistory(answerMarkdown);
                        SetMascotState(MascotState.Success);
                    }
                    else if (code != -1)
                    {
                        FooterStatus.Text = "Помилка — перевір вхід";
                        SetOnline(false);
                        if (!string.IsNullOrWhiteSpace(final))
                            answerMarkdown = final;
                        else if (string.IsNullOrWhiteSpace(answerMarkdown))
                            answerMarkdown = "Не вдалося отримати відповідь";
                        RenderAnswer(answerMarkdown, error: true);
                        AppendAssistantHistory(answerMarkdown);
                        SetMascotState(MascotState.Error);
                    }
                    else
                    {
                        FooterStatus.Text = _userCancelledRun
                            ? "Скасовано"
                            : "Агент не відповів (таймаут 10 хв)";
                        if (string.IsNullOrWhiteSpace(answerMarkdown))
                            answerMarkdown = _userCancelledRun ? "Скасовано" : "Час очікування вичерпано";
                        RenderAnswer(answerMarkdown);
                        SetMascotState(MascotState.Idle);
                    }
                    Save();
                });
            },
            attachments,
            _runCts.Token);
        }
        catch (Exception ex)
        {
            Ui(() =>
            {
                SetBusy(false);
                FooterStatus.Text = ex.Message;
                SetMascotState(MascotState.Error);
            });
        }
    }

    private sealed class AssistantTurn
    {
        public required TextBlock Status { get; init; }
        public required TextBlock Thinking { get; init; }
        public required StackPanel AnswerHost { get; init; }
        public required Button CopyBtn { get; init; }
        public string AnswerMarkdown { get; set; } = "";
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
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copyBtn = new Button
        {
            Content = "Копіювати",
            FontSize = 10,
            Padding = new Thickness(8, 2, 8, 2),
            MinHeight = 0,
            MinWidth = 0,
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(accent),
            BorderThickness = new Thickness(0),
        };
        var answerHost = new StackPanel { Spacing = 4 };
        var turnRef = new AssistantTurn
        {
            Status = status,
            Thinking = thinking,
            AnswerHost = answerHost,
            CopyBtn = copyBtn,
        };
        copyBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(turnRef.AnswerMarkdown)) return;
            var package = new DataPackage();
            package.SetText(turnRef.AnswerMarkdown);
            Clipboard.SetContent(package);
            FooterStatus.Text = "Скопійовано";
        };

        answerHost.Children.Add(new TextBlock
        {
            Text = "⏳ Чекаю відповідь агента…",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(_themes.Current.Muted),
        });

        toolbar.Children.Add(copyBtn);
        body.Children.Add(status);
        body.Children.Add(thinking);
        body.Children.Add(toolbar);
        body.Children.Add(answerHost);
        border.Child = body;
        stack.Children.Add(border);
        ChatPanel.Children.Add(stack);
        ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight + 80, null);
        return turnRef;
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

    private void EnterChatMode()
    {
        _hasMessages = true;
        ShowChatScreen();
    }

    private bool _busy;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InputBox.IsEnabled = !busy;
        if (busy)
        {
            StatusText.Text = "Думає…";
            SendIcon.Glyph = "\uE711";
            SendButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B));
            SetMascotState(MascotState.Thinking);
        }
        else
        {
            SendIcon.Glyph = "\uE724";
            SendButton.Background = new SolidColorBrush(_themes.Current.Accent);
            if (!_expanded || StatusDot.Fill is not SolidColorBrush)
                StatusText.Text = "Online";
            else
                SetOnline(StatusText.Text != "Offline");
        }

        UpdateScreenLayout();
    }

    private void CancelRun()
    {
        _userCancelledRun = true;
        _runCts?.Cancel();
        _agent.Cancel();
        SetBusy(false);
        SetMascotState(MascotState.Idle);
        FooterStatus.Text = "Скасовано";
    }

    private void SetMascotState(MascotState state)
    {
        _orb?.SetMascotState(state);
        _mascotResetTimer?.Stop();
        if (state is MascotState.Success or MascotState.Error)
        {
            _mascotResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _mascotResetTimer.Tick += (_, _) =>
            {
                _mascotResetTimer?.Stop();
                _orb?.SetMascotState(MascotState.Idle);
            };
            _mascotResetTimer.Start();
        }
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
        HistoryOverlay.Visibility = Visibility.Collapsed;
        ApplyPanelChromeColors();
        RefreshThemeCards();
        SelectModeInSettings();
        RefreshRecentWorkspaces();
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
        _homeMode = true;
        UpdateScreenLayout();
        ClearAttachments();
        _config.ChatId = null;
        _currentSession = _chatHistory.CreateSession(_agent.Workspace);
        _config.LocalSessionId = _currentSession.Id;
        Save();
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        HistoryOverlay.Visibility = Visibility.Visible;
        ApplyPanelChromeColors();
        RefreshHistoryList();
    }

    private void HistoryBack_Click(object sender, RoutedEventArgs e)
    {
        HistoryOverlay.Visibility = Visibility.Collapsed;
    }

    private void RefreshHistoryList()
    {
        HistoryPanel.Children.Clear();
        foreach (var session in _chatHistory.List())
            HistoryPanel.Children.Add(MakeHistorySessionCard(session));

        var hasItems = HistoryPanel.Children.Count > 0;
        HistoryEmptyCard.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        HistoryPanel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadSession(ChatSession session)
    {
        _currentSession = session;
        _config.LocalSessionId = session.Id;
        _config.ChatId = session.AgentChatId;
        if (!string.IsNullOrWhiteSpace(session.Workspace) && Directory.Exists(session.Workspace))
        {
            _agent.Workspace = session.Workspace;
            _config.Workspace = session.Workspace;
            WorkspaceText.Text = session.Workspace;
            SettingsWorkspaceText.Text = session.Workspace;
        }
        ChatPanel.Children.Clear();
        _hasMessages = session.Messages.Count > 0;
        if (_hasMessages)
            ShowChatScreen();
        else
            ShowHomeScreen();
        foreach (var msg in session.Messages)
        {
            if (msg.Role == "user")
                AddBubble(msg.Text, true);
            else
            {
                var turn = AddAssistantTurn();
                turn.Status.Visibility = Visibility.Collapsed;
                var fg = new SolidColorBrush(_themes.Current.Text);
                var muted = new SolidColorBrush(_themes.Current.Muted);
                var accent = new SolidColorBrush(_themes.Current.Accent);
                var card = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                turn.AnswerHost.Children.Clear();
                MarkdownRenderer.Populate(turn.AnswerHost, msg.Text, fg, muted, accent, card);
                turn.AnswerMarkdown = msg.Text;
                turn.CopyBtn.Visibility = Visibility.Visible;
            }
        }
        Save();
    }

    private void RestoreSessionIfAny()
    {
        if (string.IsNullOrEmpty(_config.LocalSessionId)) return;
        var session = _chatHistory.Load(_config.LocalSessionId);
        if (session is null) return;
        _currentSession = session;
        if (session.Messages.Count == 0) return;
        LoadSession(session);
    }

    private ChatSession EnsureSession()
    {
        if (_currentSession is not null)
        {
            if (!string.IsNullOrEmpty(_currentSession.AgentChatId))
                _config.ChatId = _currentSession.AgentChatId;
            return _currentSession;
        }
        if (!string.IsNullOrEmpty(_config.LocalSessionId))
        {
            _currentSession = _chatHistory.Load(_config.LocalSessionId);
            if (_currentSession is not null)
            {
                if (!string.IsNullOrEmpty(_currentSession.AgentChatId))
                    _config.ChatId = _currentSession.AgentChatId;
                return _currentSession;
            }
        }
        _currentSession = _chatHistory.CreateSession(_agent.Workspace);
        _config.LocalSessionId = _currentSession.Id;
        Save();
        return _currentSession;
    }

    private void PersistSession(string? firstUserLine = null)
    {
        if (_currentSession is null) return;
        _currentSession.AgentChatId = _config.ChatId;
        _currentSession.Workspace = _agent.Workspace;
        if (!string.IsNullOrWhiteSpace(firstUserLine) && _currentSession.Messages.Count == 1)
            _currentSession.Title = ChatHistoryService.MakeTitle(firstUserLine);
        _chatHistory.Save(_currentSession);
    }

    private void AppendAssistantHistory(string text)
    {
        if (_currentSession is null || string.IsNullOrWhiteSpace(text)) return;
        _currentSession.Messages.Add(new ChatMessage { Role = "assistant", Text = text });
        _currentSession.AgentChatId = _config.ChatId;
        _chatHistory.Save(_currentSession);
    }

    private async void Voice_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        FooterStatus.Text = "Слухаю… (може з'явитись вікно Windows)";
        var outcome = await _speech.RecognizeAsync();
        if (outcome.Cancelled)
        {
            FooterStatus.Text = "Скасовано";
            return;
        }
        if (!string.IsNullOrWhiteSpace(outcome.Error))
        {
            FooterStatus.Text = outcome.Error;
            return;
        }
        if (string.IsNullOrWhiteSpace(outcome.Text))
        {
            FooterStatus.Text = "Голос не розпізнано";
            return;
        }
        InputBox.Text = string.IsNullOrWhiteSpace(InputBox.Text)
            ? outcome.Text
            : InputBox.Text.TrimEnd() + " " + outcome.Text;
        FooterStatus.Text = "Голос додано";
        InputBox.Focus(FocusState.Programmatic);
    }

    private void SelectModeInSettings()
    {
        _loadingMode = true;
        var mode = _config.AgentMode ?? "agent";
        for (var i = 0; i < SettingsModeCombo.Items.Count; i++)
        {
            if (SettingsModeCombo.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, mode, StringComparison.OrdinalIgnoreCase))
            {
                SettingsModeCombo.SelectedIndex = i;
                break;
            }
        }
        _loadingMode = false;
    }

    private void SettingsMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMode) return;
        if (SettingsModeCombo.SelectedItem is not ComboBoxItem item) return;
        var mode = item.Tag as string ?? "agent";
        if (string.Equals(_config.AgentMode, mode, StringComparison.OrdinalIgnoreCase)) return;
        _config.AgentMode = mode;
        Save();
        FooterStatus.Text = $"Режим: {item.Content}";
    }

    private void RefreshRecentWorkspaces()
    {
        _loadingRecentWorkspace = true;
        SettingsRecentWorkspaceCombo.Items.Clear();
        var selected = 0;
        for (var i = 0; i < _config.RecentWorkspaces.Count; i++)
        {
            var path = _config.RecentWorkspaces[i];
            SettingsRecentWorkspaceCombo.Items.Add(new ComboBoxItem { Content = path, Tag = path });
            if (string.Equals(path, _agent.Workspace, StringComparison.OrdinalIgnoreCase))
                selected = i;
        }
        if (SettingsRecentWorkspaceCombo.Items.Count > 0)
            SettingsRecentWorkspaceCombo.SelectedIndex = selected;
        _loadingRecentWorkspace = false;
    }

    private void SettingsRecentWorkspace_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingRecentWorkspace) return;
        if (SettingsRecentWorkspaceCombo.SelectedItem is not ComboBoxItem item) return;
        var path = item.Tag as string;
        if (string.IsNullOrWhiteSpace(path)) return;
        ApplyWorkspace(path);
    }

    private void ApplyWorkspace(string path)
    {
        if (!Directory.Exists(path)) return;
        _agent.Workspace = path;
        _config.Workspace = path;
        WorkspaceText.Text = path;
        SettingsWorkspaceText.Text = path;
        TrackRecentWorkspace(path);
        RefreshRecentWorkspaces();
        Save();
    }

    private void TrackRecentWorkspace(string path)
    {
        _config.RecentWorkspaces.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _config.RecentWorkspaces.Insert(0, path);
        if (_config.RecentWorkspaces.Count > 5)
            _config.RecentWorkspaces = _config.RecentWorkspaces.Take(5).ToList();
    }

    private async void Folder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            ApplyWorkspace(folder.Path);
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
        _config.LocalSessionId = _currentSession?.Id ?? _config.LocalSessionId;
        _config.ThemeId = _themes.Current.Id;
        _config.ModelId = string.IsNullOrWhiteSpace(_config.ModelId) ? "auto" : _config.ModelId;
        _config.AgentMode = string.IsNullOrWhiteSpace(_config.AgentMode) ? "agent" : _config.AgentMode;
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
