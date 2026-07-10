using System.Drawing.Imaging;
using Bitmap = System.Drawing.Bitmap;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Clipy.Helpers;
using Clipy.Localization;
using Clipy.Models;
using Clipy.Services;
using Clipy.Services.Agents;
using Clipy.Themes;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
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
    private readonly UpdateService _updates = new();
    private readonly SpeechInputService _speech = new();
    private readonly TaskbarIcon _tray = new() { ToolTipText = "Clipy" };
    private NativeTrayMenuService? _trayMenu;
    private XamlUICommand? _trayOpenMenuCommand;
    private System.Drawing.Icon? _trayIcon;
    private readonly HotkeyService _hotkey = new();
    private readonly List<AttachmentItem> _attachments = new();
    private NativeOrbWindow? _orb;
    private OrbBubbleWindow? _orbBubble;
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
    private bool _loadingLanguage;
    private bool _loadingMode;
    private bool _loadingProvider;
    private bool _loadingRecentWorkspace;
    private int _panelWidth;
    private int _panelHeight;
    private bool _resizing;
    private NativePoint _resizeStartCursor;
    private int _resizeStartWidth;
    private int _resizeStartHeight;
    private string? _pendingOrbSummary;
    private UpdateInfo? _pendingUpdate;
    private bool _checkingUpdate;
    private bool _installingUpdate;
    private IHomeBackgroundPlayer? _homeBackgroundPlayer;
    private bool _homeBackgroundLoading;
    private int _homeBackgroundGeneration;

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
            MessageBox(IntPtr.Zero, Loc.Get("app.already_running"), "Clipy", 0x40);
            Environment.Exit(0);
            return;
        }

        InitializeComponent();
        Title = "";

        _config = _configService.Load();
        Loc.Set(_config.Language);
        Loc.Changed += () => DispatcherQueue.TryEnqueue(ApplyLocalization);
        _panelWidth = _config.PanelWidth ?? WindowHelper.DefaultPanelWidth;
        _panelHeight = _config.PanelHeight ?? WindowHelper.DefaultPanelHeight;
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
            _homeBackgroundPlayer?.Dispose();
            _homeBackgroundPlayer = null;
            _hotkey.Dispose();
            _orb?.Dispose();
            _orb = null;
            _orbBubble?.Dispose();
            _orbBubble = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
        };

        try
        {
            _hotkey.Register();
            _hotkey.Triggered += () => DispatcherQueue.TryEnqueue(ToggleFromHotkey);
        }
        catch
        {
            FooterStatus.Text = Loc.Get("hotkey.busy");
        }

        _ = RefreshStatusAsync();
        SetupTray();
        SetupSignalWatcher();
        RefreshThemeCards();
        ApplyLocalization();
        ApplyPanelChromeColors();
        RefreshHeaderAvatar();
        RefreshFrameDecor();
        UpdateScreenLayout();
        SelectModeInSettings();
        SelectLanguageInSettings();
        PopulateProviderCombo();
        SelectProviderInSettings();
        RefreshRecentWorkspaces();
        RestoreSessionIfAny();
        UpdateScreenLayout();
        RefreshUpdateSettingsUi();
        _ = CheckForUpdatesOnStartupAsync();
    }

    private void OnThemeChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _orb?.SetRenderer(_themes.CurrentRenderer);
                ApplyPanelChromeColors();
                RefreshHeaderAvatar();
                RefreshFrameDecor();
                RefreshHomeBackground();
                RefreshThemeCards();
                RefreshTrayIcon();
                UpdateScreenLayout();
            }
            catch
            {
            }
        });
    }

    private void ShowHomeScreen()
    {
        _homeMode = true;
        UpdateScreenLayout();
        RefreshHomeHistoryList();
    }

    private void ShowChatScreen()
    {
        _homeMode = false;
        UpdateScreenLayout();
        RefreshChatLayout();
        ScrollChatToBottom();
    }

    private void ScrollChatToBottom()
    {
        if (ChatScroll.Visibility != Visibility.Visible)
            return;

        void ScrollNow()
        {
            ChatPanel.UpdateLayout();
            ChatScroll.UpdateLayout();
            var offset = ChatScroll.ScrollableHeight + 80;
            if (offset > 0)
                ChatScroll.ChangeView(null, offset, null, true);
        }

        DispatcherQueue.TryEnqueue(ScrollNow);
        DispatcherQueue.TryEnqueue(ScrollNow);
    }

    private void UpdateScreenLayout()
    {
        var onHome = (_homeMode || !_hasMessages) && !_busy;
        HomePanel.Visibility = onHome ? Visibility.Visible : Visibility.Collapsed;
        ChatScroll.Visibility = onHome ? Visibility.Collapsed : Visibility.Visible;
        OpenChatBtn.Visibility = onHome && _hasMessages ? Visibility.Visible : Visibility.Collapsed;
        HomeBtn.Visibility = _hasMessages && !onHome ? Visibility.Visible : Visibility.Collapsed;
        if (onHome)
        {
            RefreshHomeHistoryList();
            EnsureHomeBackground();
            if (_homeBackgroundPlayer is not null)
                _homeBackgroundPlayer.Start();
        }
        else
            _homeBackgroundPlayer?.Stop();
    }

    private void ApplyLocalization()
    {
        ToolTipService.SetToolTip(HomeBtn, Loc.Get("home.tooltip"));
        GreetingText.Text = Loc.Get("home.greeting");
        NewChatBtn.Content = Loc.Get("home.new_chat");
        OpenChatBtn.Content = Loc.Get("home.chat");
        FolderBtn.Content = Loc.Get("home.folder");
        HomeHistoryTitle.Text = Loc.Get("home.recent");
        HomeHistoryEmptyTitle.Text = Loc.Get("home.empty_title");
        HomeHistoryEmptyHint.Text = Loc.Get("home.empty_hint");

        ToolTipService.SetToolTip(AttachFileBtn, Loc.Get("input.attach"));
        ToolTipService.SetToolTip(ScreenshotBtn, Loc.Get("input.screenshot"));
        ToolTipService.SetToolTip(VoiceBtn, Loc.Get("input.voice"));
        InputBox.PlaceholderText = Loc.Get("input.placeholder");
        FooterStatus.Text = Loc.Get("footer.hint");
        ToolTipService.SetToolTip(ResizeGrip, Loc.Get("resize.tooltip"));

        HistoryTitleText.Text = Loc.Get("history.title");
        HistorySubtitleText.Text = Loc.Get("history.subtitle");
        HistoryEmptyTitle.Text = Loc.Get("history.empty_title");
        HistoryEmptyHint.Text = Loc.Get("history.empty_hint");

        SettingsTitleText.Text = Loc.Get("settings.title");
        SettingsSubtitleText.Text = Loc.Get("settings.subtitle");
        SettingsProviderTitle.Text = Loc.Get("settings.provider.title");
        RefreshProviderComboLabels();
        ApplyProviderSettingsUi();
        SettingsLogoutBtn.Content = Loc.Get("settings.auth.logout");
        SettingsModeTitle.Text = Loc.Get("settings.mode.title");
        SettingsModelTitle.Text = Loc.Get("settings.model.title");
        SettingsModelHint.Text = Loc.Get("settings.model.hint");
        SettingsWorkspaceTitle.Text = Loc.Get("settings.workspace.title");
        SettingsFolderBtn.Content = Loc.Get("settings.workspace.change");
        SettingsLanguageTitle.Text = Loc.Get("settings.language.title");
        SettingsThemesTitle.Text = Loc.Get("settings.themes.title");
        SettingsThemesHint.Text = Loc.Get("settings.themes.hint");
        SettingsUpdateTitle.Text = Loc.Get("settings.update.title");
        SettingsCheckUpdateBtn.Content = Loc.Get("settings.update.check_btn");
        SettingsInstallUpdateBtn.Content = Loc.Get("settings.update.install_btn");

        if (SettingsLanguageCombo.Items.Count >= 2)
        {
            ((ComboBoxItem)SettingsLanguageCombo.Items[0]).Content = Loc.Get("settings.language.uk");
            ((ComboBoxItem)SettingsLanguageCombo.Items[1]).Content = Loc.Get("settings.language.en");
        }

        if (SettingsModeCombo.Items.Count >= 3)
        {
            ((ComboBoxItem)SettingsModeCombo.Items[0]).Content = Loc.Get("settings.mode.agent");
            ((ComboBoxItem)SettingsModeCombo.Items[1]).Content = Loc.Get("settings.mode.ask");
            ((ComboBoxItem)SettingsModeCombo.Items[2]).Content = Loc.Get("settings.mode.plan");
        }

        if (!_busy)
            StatusText.Text = Loc.Get("status.online");

        RefreshThemeCards();
        RefreshHomeHistoryList();
        RefreshTrayMenu();
        _ = UpdateSettingsAuthUiAsync();
        RefreshUpdateSettingsUi();
    }

    private void SelectLanguageInSettings()
    {
        _loadingLanguage = true;
        var lang = string.Equals(_config.Language, Loc.En, StringComparison.OrdinalIgnoreCase) ? Loc.En : Loc.Uk;
        for (var i = 0; i < SettingsLanguageCombo.Items.Count; i++)
        {
            if (SettingsLanguageCombo.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, lang, StringComparison.OrdinalIgnoreCase))
            {
                SettingsLanguageCombo.SelectedIndex = i;
                break;
            }
        }
        _loadingLanguage = false;
    }

    private void SettingsLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingLanguage) return;
        if (SettingsLanguageCombo.SelectedItem is not ComboBoxItem item) return;
        var lang = item.Tag as string;
        if (string.IsNullOrWhiteSpace(lang)) return;
        _config.Language = lang;
        Loc.Set(lang);
        Save();
    }

    private void Home_Click(object sender, RoutedEventArgs e) => ShowHomeScreen();

    private void OpenChat_Click(object sender, RoutedEventArgs e) => ShowChatScreen();

    private void ApplyPanelChromeColors()
    {
        var theme = _themes.Current;
        var pack = _themes.CurrentPack;
        var accent = new SolidColorBrush(theme.Accent);
        var card = new SolidColorBrush(theme.Card);
        var text = new SolidColorBrush(theme.Text);
        var muted = new SolidColorBrush(theme.Muted);
        var bg = new SolidColorBrush(theme.Background);
        var border = new SolidColorBrush(theme.Border);
        var subtleBorder = ThemeSubtleBorder(theme);
        var inputBg = ThemeInputBackground(theme);
        var onAccent = new SolidColorBrush(pack.OnAccentColor);

        PanelView.Background = bg;
        PanelView.BorderBrush = subtleBorder;
        FrameBorder.Background = (Brush)Application.Current.Resources["PanelFrameGradient"];
        HistoryOverlay.Background = bg;
        SettingsOverlay.Background = bg;
        StatusDot.Fill = accent;
        StatusText.Foreground = muted;
        FooterStatus.Foreground = muted;
        WorkspaceText.Foreground = muted;
        ResizeGripIcon.Foreground = accent;
        ResizeGrip.Background = card;
        InputBox.Background = inputBg;
        InputBox.Foreground = text;
        InputBox.BorderBrush = border;

        ApplyOverlayHeader(HistoryTitleText, HistorySubtitleText, HistoryBackBtn, text, muted, card, accent);
        ApplyOverlayHeader(SettingsTitleText, SettingsSubtitleText, SettingsBackBtn, text, muted, card, accent);

        StyleSettingsCard(SettingsProviderCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsAuthCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsModeCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsModelCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsWorkspaceCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsLanguageCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsThemesCard, text, muted, card, subtleBorder);
        StyleSettingsCard(SettingsUpdateCard, text, muted, card, subtleBorder);

        SettingsAuthStatus.Foreground = muted;
        SettingsWorkspaceText.Foreground = muted;
        SettingsLoginBtn.Background = accent;
        SettingsLoginBtn.Foreground = onAccent;
        SettingsLogoutBtn.Background = ThemeLogoutBackground(theme);
        SettingsLogoutBtn.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xFF, 0x8A, 0x8A));
        SettingsFolderBtn.Background = card;
        SettingsFolderBtn.Foreground = text;
        StyleComboBox(SettingsLanguageCombo, card, text, border);
        StyleComboBox(SettingsProviderCombo, card, text, border);
        StyleComboBox(SettingsModeCombo, card, text, border);
        StyleComboBox(SettingsModelCombo, card, text, border);
        StyleComboBox(SettingsRecentWorkspaceCombo, card, text, border);

        HistoryEmptyIcon.Text = pack.Manifest.EmptyIcon;
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

        GreetingCard.Background = ThemeGlassCard(theme);
        GreetingCard.BorderBrush = subtleBorder;
        if (GreetingCard.Child is TextBlock greet)
            greet.Foreground = text;

        HomeHistoryTitle.Foreground = text;
        HomeHistoryEmptyIcon.Text = pack.Manifest.EmptyIcon;
        HomeHistoryEmptyCard.Background = ThemeGlassCard(theme);
        HomeHistoryEmptyCard.BorderBrush = subtleBorder;
        HomeHistoryEmptyTitle.Foreground = text;
        HomeHistoryEmptyHint.Foreground = muted;
        HomeBgOverlay.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
            GradientStops =
            {
                new GradientStop
                {
                    Color = Windows.UI.Color.FromArgb(0xCC, theme.Background.R, theme.Background.G, theme.Background.B),
                    Offset = 0,
                },
                new GradientStop
                {
                    Color = Windows.UI.Color.FromArgb(0x55, theme.Background.R, theme.Background.G, theme.Background.B),
                    Offset = 0.5,
                },
                new GradientStop
                {
                    Color = Windows.UI.Color.FromArgb(0xEE, theme.Background.R, theme.Background.G, theme.Background.B),
                    Offset = 1,
                },
            },
        };

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
        {
            SendButton.Background = accent;
            SendIcon.Foreground = onAccent;
        }
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

    private static SolidColorBrush ThemeGlassCard(ThemePalette theme) =>
        new(Windows.UI.Color.FromArgb(0xD0, theme.Card.R, theme.Card.G, theme.Card.B));

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
            Background = ThemeGlassCard(theme),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x55, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(3, 1, 1, 1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 8, 10),
            Tag = session,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var col = new StackPanel { Spacing = 4 };
        col.Children.Add(new TextBlock
        {
            Text = session.Title,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(theme.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var preview = session.Messages.LastOrDefault(m => m.Role == "user")?.Text
            ?? session.Messages.LastOrDefault()?.Text
            ?? "";
        preview = preview.Replace('\n', ' ').Trim();
        if (preview.Length > 72) preview = preview[..69] + "…";
        if (!string.IsNullOrWhiteSpace(preview))
        {
            col.Children.Add(new TextBlock
            {
                Text = preview,
                FontSize = 11,
                Foreground = new SolidColorBrush(theme.Muted),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                TextWrapping = TextWrapping.Wrap,
            });
        }
        col.Children.Add(new TextBlock
        {
            Text = Loc.Format("history.msg_count", session.UpdatedAt.ToLocalTime().ToString("g"), session.Messages.Count),
            FontSize = 11,
            Foreground = new SolidColorBrush(theme.Muted),
        });
        Grid.SetColumn(col, 0);
        grid.Children.Add(col);

        var deleteBtn = new Button
        {
            Style = (Style)Application.Current.Resources["IconButtonStyle"],
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Top,
            Content = new FontIcon
            {
                Glyph = "\uE74D",
                FontSize = 12,
                Foreground = new SolidColorBrush(theme.Muted),
            },
        };
        ToolTipService.SetToolTip(deleteBtn, Loc.Get("history.delete"));
        deleteBtn.Click += async (_, _) => await ConfirmAndDeleteChatAsync(session);
        Grid.SetColumn(deleteBtn, 1);
        grid.Children.Add(deleteBtn);

        card.Child = grid;
        col.Tapped += (_, _) =>
        {
            LoadSession(session);
            HistoryOverlay.Visibility = Visibility.Collapsed;
            ShowChatScreen();
        };
        return card;
    }

    private async Task ConfirmAndDeleteChatAsync(ChatSession session)
    {
        var title = string.IsNullOrWhiteSpace(session.Title) ? Loc.Get("chat.new") : session.Title;
        var dialog = new ContentDialog
        {
            Title = Loc.Get("history.delete_title"),
            Content = new TextBlock
            {
                Text = Loc.Format("history.delete_confirm", title),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = Loc.Get("history.delete_btn"),
            CloseButtonText = Loc.Get("history.cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        DeleteChat(session);
    }

    private void DeleteChat(ChatSession session)
    {
        _chatHistory.Delete(session.Id);

        if (string.Equals(_currentSession?.Id, session.Id, StringComparison.Ordinal))
        {
            _currentSession = null;
            _config.LocalSessionId = null;
            _config.ChatId = null;
            ChatPanel.Children.Clear();
            _hasMessages = false;
            _homeMode = true;
            Save();
            UpdateScreenLayout();
        }

        RefreshHomeHistoryList();
        RefreshHistoryList();
        FooterStatus.Text = Loc.Get("history.deleted");
    }

    private void RefreshHomeBackground()
    {
        _homeBackgroundGeneration++;
        _homeBackgroundLoading = false;
        _homeBackgroundPlayer?.Dispose();
        _homeBackgroundPlayer = null;
        HomeBgGif.Visibility = Visibility.Collapsed;
        HomeBgGif.Source = null;
    }

    private void EnsureHomeBackground()
    {
        if (_homeBackgroundPlayer is not null || _homeBackgroundLoading) return;

        var pack = _themes.CurrentPack;
        var path = pack.HomeBackgroundPath;
        if (path is null) return;

        HomeBgGif.Opacity = pack.HomeBackgroundOpacity;

        if (GifBackgroundPlayer.CanPlay(path))
        {
            try
            {
                HomeBgGif.Visibility = Visibility.Visible;
                _homeBackgroundPlayer = new GifBackgroundPlayer(HomeBgGif, path, DispatcherQueue);
            }
            catch
            {
                HomeBgGif.Visibility = Visibility.Collapsed;
                HomeBgGif.Source = null;
            }

            return;
        }

        if (!HomeBackgroundFiles.IsWebp(path))
            return;

        var generation = _homeBackgroundGeneration;
        _homeBackgroundLoading = true;
        HomeBgGif.Visibility = Visibility.Visible;

        _ = Task.Run(() => WebpBackgroundPlayer.LoadImage(path)).ContinueWith(t =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _homeBackgroundLoading = false;
                if (generation != _homeBackgroundGeneration)
                {
                    t.Result?.Dispose();
                    return;
                }

                if (t.IsFaulted || t.Result is null)
                {
                    HomeBgGif.Visibility = Visibility.Collapsed;
                    HomeBgGif.Source = null;
                    return;
                }

                _homeBackgroundPlayer = WebpBackgroundPlayer.TryCreate(HomeBgGif, t.Result, DispatcherQueue);
                if (_homeBackgroundPlayer is null)
                {
                    t.Result.Dispose();
                    HomeBgGif.Visibility = Visibility.Collapsed;
                    HomeBgGif.Source = null;
                    return;
                }

                if (ShouldPlayHomeBackground())
                    _homeBackgroundPlayer.Start();
            });
        });
    }

    private bool ShouldPlayHomeBackground() =>
        (_homeMode || !_hasMessages) && !_busy;

    private void RefreshHomeHistoryList() => PopulateHistoryList(HomeHistoryPanel, HomeHistoryEmptyCard);

    private void RefreshHistoryList() => PopulateHistoryList(HistoryPanel, HistoryEmptyCard);

    private void PopulateHistoryList(StackPanel panel, Border emptyCard)
    {
        panel.Children.Clear();
        foreach (var session in _chatHistory.List())
            panel.Children.Add(MakeHistorySessionCard(session));

        var hasItems = panel.Children.Count > 0;
        emptyCard.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        panel.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshHeaderAvatar()
    {
        try
        {
            Bitmap bmp;
            lock (GdiRenderLock.Sync)
            {
                bmp = _themes.CurrentRenderer.Render(64, 1.15);
            }

            using (bmp)
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var image = new BitmapImage();
                image.SetSource(ms.AsRandomAccessStream());
                HeaderAvatarImage.Source = image;
                HistoryAvatarImage.Source = image;
                SettingsAvatarImage.Source = image;
            }
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
        var frame = _themes.CurrentPack.Frame;
        if (frame is null)
        {
            FrameDecorImage.Source = null;
            FrameDecorImage.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            Bitmap bmp;
            lock (GdiRenderLock.Sync)
            {
                bmp = frame.Render(_panelWidth, _panelHeight);
            }

            using (bmp)
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var image = new BitmapImage();
                image.SetSource(ms.AsRandomAccessStream());
                FrameDecorImage.Source = image;
                FrameDecorImage.Visibility = Visibility.Visible;
            }
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
        foreach (var pack in _themes.All)
        {
            var theme = pack.Palette;
            var selected = string.Equals(pack.Id, _themes.CurrentPack.Id, StringComparison.OrdinalIgnoreCase);
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
                Tag = pack.Id,
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
                Text = pack.Manifest.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(_themes.Current.Text),
            });
            text.Children.Add(new TextBlock
            {
                Text = pack.GetDescription(Loc.Current),
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
            card.Tapped += (_, _) => SelectTheme(pack.Id);
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
        FooterStatus.Text = Loc.Format("status.theme", _themes.Current.Name);
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
            _orbBubble?.MoveNearOrb(_orbPos, WindowHelper.OrbSize);
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
        _orbBubble?.Hide();
        _orb?.Hide();

        var panelPos = WindowHelper.PanelFromOrb(_orbPos, _panelWidth, _panelHeight);
        _appWindow.MoveAndResize(new RectInt32(
            panelPos.X, panelPos.Y, _panelWidth, _panelHeight));
        WindowHelper.ApplyPanelChrome(this, _appWindow);
        WindowHelper.ShowWindowTopmost(this);
        Activate();
        BringToFront();
        RefreshChatLayout();
        InputBox.Focus(FocusState.Programmatic);
        if (_hasMessages && !_homeMode)
            ScrollChatToBottom();
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
        _orbPos = WindowHelper.OrbFromPanel(panelPos, _panelWidth, _panelHeight);
        ShowOrbMode();
        TryShowOrbBubble();
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
            _panelWidth, _panelHeight);
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
            _orbPos = WindowHelper.OrbFromPanel(_appWindow.Position, _panelWidth, _panelHeight);
            Save();
        }
        e.Handled = true;
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => Collapse();

    private double ChatBubbleMaxWidth() => Math.Max(220, _panelWidth - 96);

    private void RefreshChatLayout()
    {
        var maxWidth = ChatBubbleMaxWidth();
        foreach (var child in ChatPanel.Children)
        {
            if (child is FrameworkElement fe)
                fe.MaxWidth = maxWidth;
        }
        ChatPanel.UpdateLayout();
        ChatScroll.UpdateLayout();
    }

    private void ResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_expanded || !e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
            return;
        WindowHelper.GetCursorPos(out _resizeStartCursor);
        _resizeStartWidth = _panelWidth;
        _resizeStartHeight = _panelHeight;
        _resizing = true;
        (sender as UIElement)?.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        WindowHelper.GetCursorPos(out var cursor);
        var newWidth = WindowHelper.ClampPanelWidth(_resizeStartWidth + (cursor.X - _resizeStartCursor.X));
        var newHeight = WindowHelper.ClampPanelHeight(_resizeStartHeight + (cursor.Y - _resizeStartCursor.Y));
        if (newWidth == _panelWidth && newHeight == _panelHeight) return;
        _panelWidth = newWidth;
        _panelHeight = newHeight;
        var pos = _appWindow.Position;
        _appWindow.MoveAndResize(new RectInt32(pos.X, pos.Y, _panelWidth, _panelHeight));
        WindowHelper.ApplyWindowRoundRegion(this, _panelWidth, _panelHeight);
        RefreshFrameDecor();
        RefreshChatLayout();
        e.Handled = true;
    }

    private void ResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        _config.PanelWidth = _panelWidth;
        _config.PanelHeight = _panelHeight;
        Save();
        RefreshChatLayout();
        e.Handled = true;
    }

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
            FooterStatus.Text = Loc.Get("status.screenshot_added");
        }
        catch (Exception ex)
        {
            FooterStatus.Text = Loc.Format("status.screenshot_failed", ex.Message);
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
        HideOrbBubble();
        _pendingOrbSummary = null;
        FooterStatus.Text = Loc.Get("status.thinking_short");

        _userCancelledRun = false;
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        _runCts.CancelAfter(TimeSpan.FromMinutes(10));
        var turn = AddAssistantTurn();
        var gotAnswer = false;
        var thinkingLen = 0;
        var answerMarkdown = "";
        var thinkingBuilder = new System.Text.StringBuilder();
        var ui = new UiCoalescer(DispatcherQueue);
        var textBrush = new SolidColorBrush(_themes.Current.Text);
        var mutedBrush = new SolidColorBrush(_themes.Current.Muted);
        var accentBrush = new SolidColorBrush(_themes.Current.Accent);
        var cardBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        var errorBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x8A, 0x8A));

        void ScrollChat() => ScrollChatToBottom();

        void RenderAnswer(string markdown, bool error = false)
        {
            turn.StreamPreview.Visibility = Visibility.Collapsed;
            turn.AnswerHost.Visibility = Visibility.Visible;
            var fg = error ? errorBrush : textBrush;
            turn.AnswerHost.Children.Clear();
            MarkdownRenderer.Populate(turn.AnswerHost, markdown, fg, mutedBrush, accentBrush, cardBrush);
            turn.AnswerMarkdown = markdown;
            turn.CopyBtn.Visibility = string.IsNullOrWhiteSpace(markdown)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ScrollChat();
        }

        void UpdateStreamPreview()
        {
            if (_userCancelledRun) return;
            turn.AnswerHost.Visibility = Visibility.Collapsed;
            turn.StreamPreview.Visibility = Visibility.Visible;
            turn.StreamPreview.Text = answerMarkdown.Length > 16000
                ? "…\n" + answerMarkdown[^16000..]
                : answerMarkdown;
            ScrollChat();
        }

        try
        {
        var agentPrompt = string.IsNullOrEmpty(text) ? "Analyze the attached context." : text;
        agentPrompt = OrbSummary.AugmentPrompt(agentPrompt);
        await _agent.RunPromptAsync(
            agentPrompt,
            s => ui.Enqueue(() =>
            {
                FooterStatus.Text = s;
                if (!gotAnswer)
                    turn.Status.Text = s;
            }),
            chunk =>
            {
                if (thinkingLen < 900)
                {
                    thinkingLen += chunk.Length;
                    thinkingBuilder.Append(chunk);
                }
                ui.Enqueue(() =>
                {
                    turn.Thinking.Visibility = Visibility.Visible;
                    turn.Thinking.Text = thinkingLen < 900
                        ? thinkingBuilder.ToString()
                        : thinkingBuilder.ToString()[..Math.Min(900, thinkingBuilder.Length)] + "…";
                    if (!gotAnswer)
                        turn.Status.Text = Loc.Get("status.thinking_short");
                });
            },
            chunk =>
            {
                answerMarkdown += chunk;
                ui.Enqueue(() =>
                {
                    if (!gotAnswer)
                    {
                        gotAnswer = true;
                        turn.Status.Visibility = Visibility.Collapsed;
                        if (turn.Thinking.Visibility == Visibility.Visible)
                            turn.Thinking.Opacity = 0.55;
                    }
                    UpdateStreamPreview();
                });
            },
            (final, code) =>
            {
                ui.EnqueueHigh(() =>
                {
                    SetBusy(false);
                    turn.Status.Visibility = Visibility.Collapsed;
                    if (code == 0)
                    {
                        FooterStatus.Text = Loc.Get("status.done");
                        SetOnline(true);
                        _config.ChatId = _agent.ChatId;
                        if (!string.IsNullOrWhiteSpace(final))
                            answerMarkdown = final;
                        else if (string.IsNullOrWhiteSpace(answerMarkdown))
                            answerMarkdown = "(порожня відповідь)";
                        var (body, summary) = OrbSummary.Split(answerMarkdown);
                        answerMarkdown = body;
                        RenderAnswer(answerMarkdown);
                        AppendAssistantHistory(answerMarkdown);
                        SetMascotState(MascotState.Success);
                        if (!string.IsNullOrWhiteSpace(summary))
                        {
                            _pendingOrbSummary = summary;
                            TryShowOrbBubble();
                        }
                    }
                    else if (code != -1)
                    {
                        FooterStatus.Text = Loc.Get("status.error_input");
                        SetOnline(false);
                        if (!string.IsNullOrWhiteSpace(final))
                            answerMarkdown = final;
                        else if (string.IsNullOrWhiteSpace(answerMarkdown))
                            answerMarkdown = Loc.Get("chat.no_answer");
                        RenderAnswer(answerMarkdown, error: true);
                        AppendAssistantHistory(answerMarkdown);
                        SetMascotState(MascotState.Error);
                    }
                    else
                    {
                        FooterStatus.Text = _userCancelledRun
                            ? Loc.Get("status.cancelled")
                            : Loc.Get("status.no_response");
                        if (string.IsNullOrWhiteSpace(answerMarkdown))
                            answerMarkdown = _userCancelledRun ? Loc.Get("status.cancelled") : Loc.Get("status.timeout");
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
            ui.EnqueueHigh(() =>
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
        public required TextBlock StreamPreview { get; init; }
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
            MaxWidth = ChatBubbleMaxWidth(),
        };

        var status = new TextBlock
        {
            Text = Loc.Get("status.thinking_short"),
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
            Content = Loc.Get("chat.copy"),
            FontSize = 10,
            Padding = new Thickness(8, 2, 8, 2),
            MinHeight = 0,
            MinWidth = 0,
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(accent),
            BorderThickness = new Thickness(0),
        };
        var streamPreview = new TextBlock
        {
            Text = Loc.Get("chat.waiting"),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = new SolidColorBrush(_themes.Current.Muted),
        };
        var answerHost = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        var turnRef = new AssistantTurn
        {
            Status = status,
            Thinking = thinking,
            StreamPreview = streamPreview,
            AnswerHost = answerHost,
            CopyBtn = copyBtn,
        };
        copyBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(turnRef.AnswerMarkdown)) return;
            var package = new DataPackage();
            package.SetText(turnRef.AnswerMarkdown);
            Clipboard.SetContent(package);
            FooterStatus.Text = Loc.Get("status.copied");
        };

        toolbar.Children.Add(copyBtn);
        body.Children.Add(status);
        body.Children.Add(thinking);
        body.Children.Add(toolbar);
        body.Children.Add(streamPreview);
        body.Children.Add(answerHost);
        border.Child = body;
        stack.Children.Add(border);
        ChatPanel.Children.Add(stack);
        ScrollChatToBottom();
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
            MaxWidth = ChatBubbleMaxWidth(),
        };
        var tb = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = new SolidColorBrush(_themes.Current.Text),
        };
        border.Child = tb;
        ChatPanel.Children.Add(border);
        ScrollChatToBottom();
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
            StatusText.Text = Loc.Get("status.thinking");
            SendIcon.Glyph = "\uE711";
            SendButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B));
            SetMascotState(MascotState.Thinking);
        }
        else
        {
            SendIcon.Glyph = "\uE724";
            SendButton.Background = new SolidColorBrush(_themes.Current.Accent);
            if (!_expanded || StatusDot.Fill is not SolidColorBrush)
                StatusText.Text = Loc.Get("status.online");
            else
                SetOnline(StatusText.Text != Loc.Get("status.offline"));
        }

        UpdateScreenLayout();
    }

    private void CancelRun()
    {
        if (!_busy) return;
        _userCancelledRun = true;
        _agent.Cancel();
        _runCts?.Cancel();
        SetBusy(false);
        SetMascotState(MascotState.Idle);
        FooterStatus.Text = Loc.Get("status.cancelled");
    }

    private void TryShowOrbBubble()
    {
        if (_expanded || string.IsNullOrWhiteSpace(_pendingOrbSummary) || _orb is null)
            return;
        ShowOrbBubble(_pendingOrbSummary);
    }

    private void ShowOrbBubble(string summary)
    {
        if (_orb is null || string.IsNullOrWhiteSpace(summary)) return;
        _orbBubble ??= new OrbBubbleWindow();
        _orbBubble.Dismissed -= OnOrbBubbleDismissed;
        _orbBubble.Dismissed += OnOrbBubbleDismissed;
        _orbBubble.Show(summary, _orbPos, WindowHelper.OrbSize, _themes.Current);
        _pendingOrbSummary = null;
    }

    private void OnOrbBubbleDismissed() => _pendingOrbSummary = null;

    private void HideOrbBubble() => _orbBubble?.Hide();

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
            StatusText.Text = online ? Loc.Get("status.online") : Loc.Get("status.offline");
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
                "missing" => Loc.Format("status.agent_missing", ProviderLabel()),
                "logged_out" => Loc.Get("status.agent_login"),
                _ => FooterStatus.Text,
            };
        }
        await UpdateSettingsAuthUiAsync(status);
    }

    private async Task UpdateSettingsAuthUiAsync(string? status = null)
    {
        status ??= await _agent.CheckStatusAsync();
        var loggedIn = _agent.IsLoggedIn(status);
        var providerName = ProviderLabel();

        SettingsAuthTitle.Text = Loc.Format("settings.auth.title", providerName);
        SettingsAuthStatus.Text = status switch
        {
            "ready" => Loc.Format("settings.auth.ready", providerName),
            "logged_out" => Loc.Get("settings.auth.logged_out"),
            "missing" => Loc.Format("settings.auth.missing", providerName),
            _ => Loc.Get("settings.auth.unknown"),
        };

        SettingsLoginBtn.Content = Loc.Format("settings.auth.login", providerName);
        SettingsLoginBtn.Visibility = _agent.SupportsLogin && !loggedIn ? Visibility.Visible : Visibility.Collapsed;
        SettingsLogoutBtn.Visibility = _agent.SupportsLogin && loggedIn ? Visibility.Visible : Visibility.Collapsed;
        SettingsWorkspaceText.Text = _agent.Workspace;
        RefreshUpdateSettingsUi();
    }

    private string ProviderLabel() => Loc.Get($"provider.{_agent.ProviderId}.name");

    private void PopulateProviderCombo()
    {
        if (SettingsProviderCombo.Items.Count > 0) return;
        foreach (var provider in _agent.Providers)
        {
            SettingsProviderCombo.Items.Add(new ComboBoxItem
            {
                Content = Loc.Get(provider.NameKey),
                Tag = provider.Id,
            });
        }
    }

    private void RefreshProviderComboLabels()
    {
        for (var i = 0; i < SettingsProviderCombo.Items.Count; i++)
        {
            if (SettingsProviderCombo.Items[i] is not ComboBoxItem item) continue;
            var id = item.Tag as string;
            if (string.IsNullOrWhiteSpace(id)) continue;
            item.Content = Loc.Get($"provider.{id}.name");
        }
    }

    private void SelectProviderInSettings()
    {
        _loadingProvider = true;
        var providerId = _agent.ProviderId;
        for (var i = 0; i < SettingsProviderCombo.Items.Count; i++)
        {
            if (SettingsProviderCombo.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag as string, providerId, StringComparison.OrdinalIgnoreCase))
            {
                SettingsProviderCombo.SelectedIndex = i;
                break;
            }
        }
        _loadingProvider = false;
        ApplyProviderSettingsUi();
    }

    private void ApplyProviderSettingsUi()
    {
        var descriptor = AgentProviderRegistry.GetDescriptor(_agent.ProviderId);
        SettingsProviderHint.Text = Loc.Get(descriptor.HintKey);
        SettingsAuthCard.Visibility = descriptor.SupportsLogin ? Visibility.Visible : Visibility.Collapsed;
        SettingsModeCard.Visibility = descriptor.SupportsModes ? Visibility.Visible : Visibility.Collapsed;
        SettingsModelCard.Visibility = descriptor.SupportsModels ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SettingsProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProvider) return;
        if (SettingsProviderCombo.SelectedItem is not ComboBoxItem item) return;
        var providerId = item.Tag as string;
        if (string.IsNullOrWhiteSpace(providerId)) return;
        if (string.Equals(_agent.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)) return;

        _agent.SetProvider(providerId);
        _config.ChatId = null;
        if (_currentSession is not null)
            _currentSession.AgentProvider = _agent.ProviderId;
        Save();
        ApplyProviderSettingsUi();
        await RefreshStatusAsync();
        await LoadModelsAsync();
        FooterStatus.Text = Loc.Format("status.model", ProviderLabel());
    }

    private void RefreshUpdateSettingsUi()
    {
        SettingsVersionText.Text = Loc.Format("settings.update.version", AppVersion.Display);
        SettingsInstallUpdateBtn.Visibility = _pendingUpdate is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (_installingUpdate)
            return;
        SettingsUpdateStatus.Text = _pendingUpdate is null
            ? Loc.Get("settings.update.auto")
            : Loc.Format("settings.update.available", _pendingUpdate.Version);
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        await Task.Delay(2500);
        try
        {
            var update = await _updates.CheckForUpdateAsync(_config);
            Save();
            if (update is null) return;
            if (string.Equals(_config.SkippedUpdateVersion, update.Version, StringComparison.OrdinalIgnoreCase))
                return;
            DispatcherQueue.TryEnqueue(() =>
            {
                _pendingUpdate = update;
                RefreshUpdateSettingsUi();
                if (!_expanded)
                    Expand();
                _ = ShowUpdateDialogAsync(update);
            });
        }
        catch { /* ignore */ }
    }

    private async Task CheckForUpdatesManualAsync()
    {
        if (_checkingUpdate || _installingUpdate) return;
        _checkingUpdate = true;
        SettingsCheckUpdateBtn.IsEnabled = false;
        SettingsUpdateStatus.Text = Loc.Get("settings.update.checking");
        SettingsUpdateProgress.Visibility = Visibility.Collapsed;
        try
        {
            var update = await _updates.CheckForUpdateAsync(_config, force: true);
            Save();
            _pendingUpdate = update;
            SettingsUpdateStatus.Text = update is null
                ? Loc.Get("settings.update.up_to_date")
                : Loc.Format("settings.update.available", update.Version);
            RefreshUpdateSettingsUi();
            if (update is not null)
                await ShowUpdateDialogAsync(update);
        }
        catch (Exception ex)
        {
            SettingsUpdateStatus.Text = Loc.Format("settings.update.check_failed", ex.Message);
        }
        finally
        {
            _checkingUpdate = false;
            SettingsCheckUpdateBtn.IsEnabled = true;
        }
    }

    private async Task ShowUpdateDialogAsync(UpdateInfo update)
    {
        var notes = update.ReleaseNotes;
        if (notes.Length > 700)
            notes = notes[..700] + "…";

        var dialog = new ContentDialog
        {
            Title = Loc.Format("settings.update.dialog_title", update.Version),
            Content = new ScrollViewer
            {
                MaxHeight = 220,
                Content = new TextBlock
                {
                    Text = notes,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
            PrimaryButtonText = Loc.Get("settings.update.dialog_primary"),
            SecondaryButtonText = Loc.Get("settings.update.dialog_secondary"),
            CloseButtonText = Loc.Get("settings.update.dialog_close"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await InstallPendingUpdateAsync();
        else if (result == ContentDialogResult.None)
        {
            _config.SkippedUpdateVersion = update.Version;
            Save();
        }
    }

    private async void SettingsCheckUpdate_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesManualAsync();

    private async void SettingsInstallUpdate_Click(object sender, RoutedEventArgs e) =>
        await InstallPendingUpdateAsync();

    private async Task InstallPendingUpdateAsync()
    {
        if (_pendingUpdate is null || _installingUpdate) return;
        _installingUpdate = true;
        SettingsInstallUpdateBtn.IsEnabled = false;
        SettingsCheckUpdateBtn.IsEnabled = false;
        SettingsUpdateProgress.Visibility = Visibility.Visible;
        SettingsUpdateProgress.Value = 0;
        SettingsUpdateStatus.Text = Loc.Get("settings.update.downloading");

        try
        {
            var portable = UpdateService.ShouldUsePortableUpdate();
            var path = await _updates.DownloadAsync(
                _pendingUpdate,
                portable,
                new Progress<double>(p => SettingsUpdateProgress.Value = p));

            SettingsUpdateStatus.Text = Loc.Get("settings.update.installing");
            if (portable)
                _updates.ApplyPortable(path);
            else
                _updates.ApplyInstaller(path);

            _hotkey.Dispose();
            _orb?.Dispose();
            _orb = null;
            _orbBubble?.Dispose();
            _orbBubble = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            SettingsUpdateStatus.Text = Loc.Format("settings.update.install_failed", ex.Message);
            SettingsUpdateProgress.Visibility = Visibility.Collapsed;
            SettingsInstallUpdateBtn.IsEnabled = true;
            SettingsCheckUpdateBtn.IsEnabled = true;
            _installingUpdate = false;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Visible;
        HistoryOverlay.Visibility = Visibility.Collapsed;
        ApplyPanelChromeColors();
        RefreshThemeCards();
        SelectModeInSettings();
        SelectProviderInSettings();
        RefreshRecentWorkspaces();
        RefreshUpdateSettingsUi();
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
        FooterStatus.Text = Loc.Format("status.model", item.Content);
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void SettingsLogin_Click(object sender, RoutedEventArgs e)
    {
        _agent.Login();
        FooterStatus.Text = Loc.Get("status.login_browser");
    }

    private async void SettingsLogout_Click(object sender, RoutedEventArgs e)
    {
        await _agent.LogoutAsync();
        _config.ChatId = null;
        Save();
        await RefreshStatusAsync();
        FooterStatus.Text = Loc.Format("status.logged_out", ProviderLabel());
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
        _pendingOrbSummary = null;
        HideOrbBubble();
        _currentSession = _chatHistory.CreateSession(_agent.Workspace);
        _currentSession.AgentProvider = _agent.ProviderId;
        _config.LocalSessionId = _currentSession.Id;
        Save();
    }

    private void History_Click(object sender, RoutedEventArgs e) => ShowHomeScreen();

    private void HistoryBack_Click(object sender, RoutedEventArgs e)
    {
        HistoryOverlay.Visibility = Visibility.Collapsed;
    }

    private void LoadSession(ChatSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.AgentProvider)
            && !string.Equals(session.AgentProvider, _agent.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            _agent.SetProvider(session.AgentProvider);
            ApplyProviderSettingsUi();
            SelectProviderInSettings();
        }

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
                turn.StreamPreview.Visibility = Visibility.Collapsed;
                turn.AnswerHost.Visibility = Visibility.Visible;
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
        RefreshChatLayout();
        ScrollChatToBottom();
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
        _currentSession.AgentProvider = _agent.ProviderId;
        _config.LocalSessionId = _currentSession.Id;
        Save();
        return _currentSession;
    }

    private void PersistSession(string? firstUserLine = null)
    {
        if (_currentSession is null) return;
        _currentSession.AgentChatId = _config.ChatId;
        _currentSession.Workspace = _agent.Workspace;
        _currentSession.AgentProvider = _agent.ProviderId;
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
        FooterStatus.Text = Loc.Get("status.listening");
        var outcome = await _speech.RecognizeAsync();
        if (outcome.Cancelled)
        {
            FooterStatus.Text = Loc.Get("status.cancelled");
            return;
        }
        if (!string.IsNullOrWhiteSpace(outcome.Error))
        {
            FooterStatus.Text = outcome.Error;
            return;
        }
        if (string.IsNullOrWhiteSpace(outcome.Text))
        {
            FooterStatus.Text = Loc.Get("status.voice_failed");
            return;
        }
        InputBox.Text = string.IsNullOrWhiteSpace(InputBox.Text)
            ? outcome.Text
            : InputBox.Text.TrimEnd() + " " + outcome.Text;
        FooterStatus.Text = Loc.Get("status.voice_added");
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
        FooterStatus.Text = Loc.Format("status.mode", item.Content);
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
        RefreshTrayIcon();
        _tray.ContextFlyout = null;
        _trayOpenMenuCommand = new XamlUICommand();
        _trayOpenMenuCommand.ExecuteRequested += (_, _) =>
            DispatcherQueue.TryEnqueue(ShowTrayMenu);
        _tray.RightClickCommand = _trayOpenMenuCommand;
        _tray.NoLeftClickDelay = true;
        _tray.LeftClickCommand = new XamlUICommand();
        ((XamlUICommand)_tray.LeftClickCommand).ExecuteRequested += (_, _) =>
            DispatcherQueue.TryEnqueue(ToggleFromHotkey);

        _trayMenu = new NativeTrayMenuService(
            DispatcherQueue,
            () => WindowNative.GetWindowHandle(this));
        RefreshTrayMenu();
        _tray.ForceCreate();
    }

    private void RefreshTrayMenu()
    {
        if (_trayMenu is null) return;
        _trayMenu.Clear();
        _trayMenu.AddItem(Loc.Get("tray.show"), Expand);
        _trayMenu.AddItem(Loc.Get("tray.hide"), Collapse);
        _trayMenu.AddItem(Loc.Get("tray.screenshot"), () =>
        {
            Expand();
            CaptureScreenshot();
        });
        _trayMenu.AddItem(Loc.Get("tray.reset_pos"), ResetTrayPosition);
        _trayMenu.AddSeparator();
        _trayMenu.AddItem(Loc.Get("tray.exit"), ExitApp);
    }

    private void ShowTrayMenu() => _trayMenu?.Show();

    private void ResetTrayPosition()
    {
        _orbPos = WindowHelper.DefaultOrbPosition();
        if (_expanded)
        {
            var panelPos = WindowHelper.PanelFromOrb(_orbPos, _panelWidth, _panelHeight);
            _appWindow.MoveAndResize(new RectInt32(
                panelPos.X, panelPos.Y, _panelWidth, _panelHeight));
        }
        else
        {
            _orb?.Move(_orbPos);
        }
        Save();
    }

    private void ExitApp()
    {
        _hotkey.Dispose();
        _orb?.Dispose();
        _orb = null;
        _orbBubble?.Dispose();
        _orbBubble = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        Application.Current.Exit();
    }

    private void RefreshTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = TrayIconHelper.Create(_themes.CurrentRenderer);
        _tray.Icon = _trayIcon;
    }

    private void Save()
    {
        _config.WindowX = _orbPos.X;
        _config.WindowY = _orbPos.Y;
        _config.Expanded = _expanded;
        _config.PanelWidth = _panelWidth;
        _config.PanelHeight = _panelHeight;
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
