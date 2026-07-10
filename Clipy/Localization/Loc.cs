namespace Clipy.Localization;

public static class Loc
{
    public const string Uk = "uk";
    public const string En = "en";

    private static readonly Dictionary<string, (string Uk, string En)> Strings = new(StringComparer.Ordinal)
    {
        ["app.already_running"] = ("Clipy вже працює.", "Clipy is already running."),
        ["hotkey.busy"] = ("Хоткей Ctrl+Shift+C зайнятий", "Hotkey Ctrl+Shift+C is already in use"),
        ["status.online"] = ("Online", "Online"),
        ["status.offline"] = ("Offline", "Offline"),
        ["status.thinking"] = ("Думає…", "Thinking…"),
        ["status.thinking_short"] = ("Думаю…", "Thinking…"),
        ["status.done"] = ("Готово", "Done"),
        ["status.cancelled"] = ("Скасовано", "Cancelled"),
        ["status.copied"] = ("Скопійовано", "Copied"),
        ["status.error_input"] = ("Помилка — перевір вхід", "Error — check your input"),
        ["status.timeout"] = ("Час очікування вичерпано", "Request timed out"),
        ["status.no_response"] = ("Агент не відповів (таймаут 10 хв)", "Agent did not respond (10 min timeout)"),
        ["status.agent_missing"] = ("Встанови {0}", "Install {0}"),
        ["status.agent_login"] = ("Увійди в налаштування", "Sign in via Settings"),
        ["status.screenshot_added"] = ("Скрін додано", "Screenshot added"),
        ["status.screenshot_failed"] = ("Скрін не вдався: {0}", "Screenshot failed: {0}"),
        ["status.theme"] = ("Тема: {0}", "Theme: {0}"),
        ["status.model"] = ("Модель: {0}", "Model: {0}"),
        ["status.mode"] = ("Режим: {0}", "Mode: {0}"),
        ["status.login_browser"] = ("Заверши вхід у браузері", "Finish sign-in in the browser"),
        ["status.logged_out"] = ("Вийшов з {0}", "Signed out of {0}"),
        ["status.listening"] = ("Слухаю… (може з'явитись вікно Windows)", "Listening… (Windows dialog may appear)"),
        ["status.voice_failed"] = ("Голос не розпізнано", "Voice not recognized"),
        ["status.voice_added"] = ("Голос додано", "Voice added"),

        ["home.tooltip"] = ("Головний екран", "Home screen"),
        ["home.greeting"] = ("Привіт 👋 Чим допомогти сьогодні?", "Hi 👋 How can I help today?"),
        ["home.new_chat"] = ("Новий чат", "New chat"),
        ["home.chat"] = ("Чат", "Chat"),
        ["home.folder"] = ("Папка", "Folder"),
        ["home.recent"] = ("Недавні чати", "Recent chats"),
        ["home.empty_title"] = ("Ще немає чатів", "No chats yet"),
        ["home.empty_hint"] = ("Напиши задачу внизу — з'явиться тут", "Write a task below — it will appear here"),

        ["input.placeholder"] = ("Напиши задачу…  Ctrl+Enter — надіслати", "Write a task…  Ctrl+Enter — send"),
        ["input.attach"] = ("Додати файл / папку", "Add file / folder"),
        ["input.screenshot"] = ("Скрін екрана (Ctrl+Shift+S)", "Screenshot (Ctrl+Shift+S)"),
        ["input.voice"] = ("Голосовий ввід", "Voice input"),
        ["footer.hint"] = ("Ctrl+Enter — надіслати · Ctrl+Shift+C — відкрити/сховати", "Ctrl+Enter — send · Ctrl+Shift+C — toggle"),
        ["resize.tooltip"] = ("Потягни для зміни розміру", "Drag to resize"),

        ["history.title"] = ("Історія чатів", "Chat history"),
        ["history.subtitle"] = ("Попередні сесії", "Previous sessions"),
        ["history.empty_title"] = ("Ще немає збережених чатів", "No saved chats yet"),
        ["history.empty_hint"] = ("Почніть новий чат — він з'явиться тут", "Start a new chat — it will appear here"),
        ["history.msg_count"] = ("{0} · {1} повід.", "{0} · {1} msgs"),
        ["history.delete"] = ("Видалити чат", "Delete chat"),
        ["history.delete_title"] = ("Видалити чат?", "Delete chat?"),
        ["history.delete_confirm"] = ("Видалити «{0}»? Цю дію не можна скасувати.", "Delete \"{0}\"? This cannot be undone."),
        ["history.delete_btn"] = ("Видалити", "Delete"),
        ["history.cancel"] = ("Скасувати", "Cancel"),
        ["history.deleted"] = ("Чат видалено", "Chat deleted"),

        ["settings.title"] = ("Налаштування", "Settings"),
        ["settings.subtitle"] = ("Провайдер, тема, workspace", "Provider, theme, workspace"),
        ["settings.provider.title"] = ("Провайдер агента", "Agent provider"),
        ["provider.cursor.name"] = ("Cursor Agent", "Cursor Agent"),
        ["provider.cursor.hint"] = ("Cursor Agent CLI — ask / plan / agent", "Cursor Agent CLI — ask / plan / agent"),
        ["provider.codex.name"] = ("OpenAI Codex", "OpenAI Codex"),
        ["provider.codex.hint"] = ("Codex CLI — codex exec --json", "Codex CLI — codex exec --json"),
        ["provider.claude.name"] = ("Claude Code", "Claude Code"),
        ["provider.claude.hint"] = ("Claude Code CLI — headless stream-json", "Claude Code CLI — headless stream-json"),
        ["settings.auth.title"] = ("{0}", "{0}"),
        ["settings.auth.ready"] = ("Увійшов у {0}", "Signed in to {0}"),
        ["settings.auth.logged_out"] = ("Не увійшов", "Not signed in"),
        ["settings.auth.missing"] = ("{0} не встановлено", "{0} is not installed"),
        ["settings.auth.unknown"] = ("Не вдалося перевірити статус", "Could not check status"),
        ["settings.auth.login"] = ("Увійти в {0}", "Sign in to {0}"),
        ["settings.auth.logout"] = ("Вийти", "Sign out"),
        ["settings.mode.title"] = ("Режим агента", "Agent mode"),
        ["settings.mode.agent"] = ("Agent", "Agent"),
        ["settings.mode.ask"] = ("Ask (лише Q&A)", "Ask (Q&A only)"),
        ["settings.mode.plan"] = ("Plan (без правок)", "Plan (no edits)"),
        ["settings.model.title"] = ("Модель", "Model"),
        ["settings.model.hint"] = ("Модель для поточного провайдера", "Model for the current provider"),
        ["settings.workspace.title"] = ("Робоча папка", "Workspace"),
        ["settings.workspace.change"] = ("Змінити папку", "Change folder"),
        ["settings.themes.title"] = ("Теми", "Themes"),
        ["settings.themes.hint"] = ("Маскот і кольори панелі", "Mascot and panel colors"),
        ["settings.language.title"] = ("Мова", "Language"),
        ["settings.language.uk"] = ("Українська", "Ukrainian"),
        ["settings.language.en"] = ("English", "English"),
        ["settings.update.title"] = ("Оновлення", "Updates"),
        ["settings.update.version"] = ("Поточна версія: {0}", "Current version: {0}"),
        ["settings.update.auto"] = ("Автоперевірка при запуску", "Auto-check on startup"),
        ["settings.update.available"] = ("Доступно оновлення {0}", "Update available: {0}"),
        ["settings.update.checking"] = ("Перевіряю оновлення…", "Checking for updates…"),
        ["settings.update.up_to_date"] = ("У тебе остання версія", "You're on the latest version"),
        ["settings.update.check_failed"] = ("Помилка перевірки: {0}", "Check failed: {0}"),
        ["settings.update.downloading"] = ("Завантажую оновлення…", "Downloading update…"),
        ["settings.update.installing"] = ("Запускаю встановлення…", "Starting installer…"),
        ["settings.update.install_failed"] = ("Помилка оновлення: {0}", "Update failed: {0}"),
        ["settings.update.check_btn"] = ("Перевірити оновлення", "Check for updates"),
        ["settings.update.install_btn"] = ("Оновити зараз", "Update now"),
        ["settings.update.dialog_title"] = ("Доступне оновлення {0}", "Update available {0}"),
        ["settings.update.dialog_primary"] = ("Оновити", "Update"),
        ["settings.update.dialog_secondary"] = ("Пізніше", "Later"),
        ["settings.update.dialog_close"] = ("Пропустити", "Skip"),

        ["chat.new"] = ("Новий чат", "New chat"),
        ["chat.copy"] = ("Копіювати", "Copy"),
        ["chat.waiting"] = ("⏳ Чекаю відповідь агента…", "⏳ Waiting for agent…"),
        ["chat.no_answer"] = ("Не вдалося отримати відповідь", "Could not get a response"),

        ["agent.sending_files"] = ("Надсилаю {0} файл(и)…", "Sending {0} file(s)…"),
        ["agent.not_found"] = ("Агент не знайдено", "Agent not found"),
        ["agent.start_failed"] = ("Не вдалося запустити агента", "Failed to start agent"),
        ["agent.still_working"] = ("Агент ще працює…", "Agent still working…"),
        ["agent.error_code"] = ("Помилка агента (код {0})", "Agent error (code {0})"),
        ["agent.trust_error"] = ("Workspace Trust: агент не довіряє папці. Перевір workspace у налаштуваннях.", "Workspace Trust: agent does not trust the folder. Check workspace in Settings."),
        ["agent.reading"] = ("Читаю {0}…", "Reading {0}…"),
        ["agent.writing"] = ("Пишу {0}…", "Writing {0}…"),
        ["agent.shell"] = ("Виконую команду…", "Running command…"),
        ["agent.tools"] = ("Працюю з інструментами…", "Working with tools…"),
        ["agent.answering"] = ("Пишу відповідь…", "Writing answer…"),
        ["agent.file"] = ("файл", "file"),

        ["speech.mic_permission"] = ("Дозволь мікрофон: Параметри → Конфіденційність → Мікрофон → Clipy", "Allow microphone: Settings → Privacy → Microphone → Clipy"),
        ["speech.start_failed"] = ("Не вдалося запустити розпізнавання. Перевір: Параметри → Час і мова → Мовлення → український пакет", "Could not start recognition. Check: Settings → Time & language → Speech → Ukrainian language pack"),
        ["speech.online_required"] = ("Увімкни онлайн-розпізнавання: Параметри → Конфіденційність → Мовлення", "Enable online recognition: Settings → Privacy → Speech"),
        ["speech.timeout"] = ("Не почув голос — спробуй ще раз", "Did not hear voice — try again"),
        ["speech.mic_unavailable"] = ("Мікрофон недоступний. Перевір підключення та дозволи", "Microphone unavailable. Check connection and permissions"),
        ["speech.pause_limit"] = ("Занадто довга пауза — спробуй ще раз", "Pause too long — try again"),
        ["speech.not_recognized"] = ("Голос не розпізнано. Перевір мікрофон і мовлення в Параметрах Windows", "Voice not recognized. Check microphone and speech in Windows Settings"),
        ["speech.mic_denied"] = ("Дозволь мікрофон у Параметрах Windows", "Allow microphone in Windows Settings"),

        ["theme.default.desc"] = ("Темна тема з лаймовим акцентом · by boston", "Dark theme with lime accent · by boston"),
        ["theme.kawaii.desc"] = ("Лісовий маскот у стилі Тоторо · by boston", "Totoro-style forest mascot · by boston"),
        ["theme.grain.desc"] = ("Монохромний grain-стиль · by boston", "Monochrome grain style · by boston"),

        ["tray.show"] = ("Показати (Ctrl+Shift+C)", "Show (Ctrl+Shift+C)"),
        ["tray.hide"] = ("Сховати", "Hide"),
        ["tray.screenshot"] = ("Скрін + відкрити", "Screenshot + open"),
        ["tray.reset_pos"] = ("На екран", "Move on screen"),
        ["tray.exit"] = ("Вихід", "Exit"),
    };

    public static string Current { get; private set; } = Uk;

    public static event Action? Changed;

    public static bool IsEn => Current == En;

    public static void Set(string? language)
    {
        Current = string.Equals(language, En, StringComparison.OrdinalIgnoreCase) ? En : Uk;
        Changed?.Invoke();
    }

    public static string Get(string key)
    {
        if (!Strings.TryGetValue(key, out var pair))
            return key;
        return Current == En ? pair.En : pair.Uk;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);

    public static string ThemeDescription(string themeId) =>
        themeId.ToLowerInvariant() switch
        {
            "kawaii" => Get("theme.kawaii.desc"),
            "grain" => Get("theme.grain.desc"),
            _ => Get("theme.default.desc"),
        };
}
