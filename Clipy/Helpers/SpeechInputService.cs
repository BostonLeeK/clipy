using System.Globalization;
using System.Speech.Recognition;
using Clipy.Localization;
using Windows.Globalization;
using Windows.Media.Capture;
using Windows.Media.SpeechRecognition;
using GlobalizationLanguage = Windows.Globalization.Language;
using WinRtRecognizer = Windows.Media.SpeechRecognition.SpeechRecognizer;

namespace Clipy.Helpers;

public sealed record SpeechRecognitionOutcome(string? Text, string? Error, bool Cancelled = false);

public sealed class SpeechInputService
{
    public async Task<SpeechRecognitionOutcome> RecognizeAsync()
    {
        try
        {
            if (!await EnsureMicrophoneAccessAsync())
            {
                return new SpeechRecognitionOutcome(
                    null,
                    Loc.Get("speech.mic_permission"));
            }

            var winRt = await TryWinRtAsync();
            if (winRt is not null)
                return winRt;

            var sapi = await Task.Run(TrySystemSpeech);
            if (sapi is not null)
                return sapi;

            return new SpeechRecognitionOutcome(
                null,
                Loc.Get("speech.start_failed"));
        }
        catch (Exception ex)
        {
            return new SpeechRecognitionOutcome(null, ShortError(ex));
        }
    }

    private static async Task<SpeechRecognitionOutcome?> TryWinRtAsync()
    {
        WinRtRecognizer? recognizer = null;
        SpeechRecognitionResultStatus? lastCompile = null;

        foreach (var attempt in BuildAttempts())
        {
            recognizer?.Dispose();
            recognizer = CreateWinRtRecognizer(attempt.Tag, attempt.Mode);
            if (recognizer is null) continue;

            var compile = await recognizer.CompileConstraintsAsync();
            lastCompile = compile.Status;
            if (compile.Status == SpeechRecognitionResultStatus.Success)
            {
                using (recognizer)
                {
                    var result = await recognizer.RecognizeWithUIAsync();
                    if (result.Status == SpeechRecognitionResultStatus.Success
                        && !string.IsNullOrWhiteSpace(result.Text))
                    {
                        return new SpeechRecognitionOutcome(result.Text.Trim(), null);
                    }

                    if (result.Status == SpeechRecognitionResultStatus.UserCanceled)
                        return new SpeechRecognitionOutcome(null, null, Cancelled: true);

                    var fallback = await recognizer.RecognizeAsync();
                    if (fallback.Status == SpeechRecognitionResultStatus.Success
                        && !string.IsNullOrWhiteSpace(fallback.Text))
                    {
                        return new SpeechRecognitionOutcome(fallback.Text.Trim(), null);
                    }
                }

                recognizer = null;
                continue;
            }

            recognizer.Dispose();
            recognizer = null;
        }

        if (lastCompile is SpeechRecognitionResultStatus.NetworkFailure)
        {
            return new SpeechRecognitionOutcome(
                null,
                Loc.Get("speech.online_required"));
        }

        return null;
    }

    private static SpeechRecognitionOutcome? TrySystemSpeech()
    {
        var cultures = CollectCultureNames();
        foreach (var cultureName in cultures)
        {
            try
            {
                var culture = new CultureInfo(cultureName);
                if (!SpeechRecognitionEngine.InstalledRecognizers()
                        .Any(r => string.Equals(r.Culture.Name, culture.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                using var engine = new SpeechRecognitionEngine(culture);
                engine.SetInputToDefaultAudioDevice();
                engine.LoadGrammar(new DictationGrammar());
                var result = engine.Recognize(TimeSpan.FromSeconds(15));
                if (result is null) continue;

                var text = result.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    return new SpeechRecognitionOutcome(text, null);
            }
            catch
            {
                // try next culture
            }
        }

        return null;
    }

    private static IEnumerable<(string? Tag, RecognizerMode Mode)> BuildAttempts()
    {
        foreach (var tag in CollectLanguageTags())
        {
            yield return (tag, RecognizerMode.Dictation);
            yield return (tag, RecognizerMode.WebSearch);
            yield return (tag, RecognizerMode.Default);
        }

        yield return (null, RecognizerMode.Default);
    }

    private static WinRtRecognizer? CreateWinRtRecognizer(string? languageTag, RecognizerMode mode)
    {
        try
        {
            var recognizer = string.IsNullOrWhiteSpace(languageTag)
                ? new WinRtRecognizer()
                : new WinRtRecognizer(new GlobalizationLanguage(languageTag));

            switch (mode)
            {
                case RecognizerMode.Dictation:
                    recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                        SpeechRecognitionScenario.Dictation,
                        "dictation"));
                    break;
                case RecognizerMode.WebSearch:
                    recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                        SpeechRecognitionScenario.WebSearch,
                        "general"));
                    break;
            }

            return recognizer;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> CollectLanguageTags()
    {
        var tags = new List<string>();
        void Add(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                tags.Add(tag);
        }

        Add("uk-UA");
        Add("uk");
        Add(WinRtRecognizer.SystemSpeechLanguage?.LanguageTag);
        foreach (var lang in WinRtRecognizer.SupportedTopicLanguages)
            Add(lang.LanguageTag);
        foreach (var lang in WinRtRecognizer.SupportedGrammarLanguages)
            Add(lang.LanguageTag);
        foreach (var lang in ApplicationLanguages.Languages)
            Add(lang);
        Add("en-US");
        return tags;
    }

    private static List<string> CollectCultureNames()
    {
        var names = new List<string>();
        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }

        Add("uk-UA");
        Add("uk");
        Add(CultureInfo.CurrentUICulture.Name);
        Add(CultureInfo.CurrentCulture.Name);
        foreach (var rec in SpeechRecognitionEngine.InstalledRecognizers())
            Add(rec.Culture.Name);
        Add("en-US");
        return names;
    }

    private static async Task<bool> EnsureMicrophoneAccessAsync()
    {
        try
        {
            var capture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio,
                MediaCategory = MediaCategory.Speech,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            };
            await capture.InitializeAsync(settings);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            return false;
        }
    }

    private enum RecognizerMode
    {
        Dictation,
        WebSearch,
        Default,
    }

    private static string DescribeStatus(SpeechRecognitionResultStatus status) =>
        status switch
        {
            SpeechRecognitionResultStatus.TimeoutExceeded => Loc.Get("speech.timeout"),
            SpeechRecognitionResultStatus.MicrophoneUnavailable => Loc.Get("speech.mic_unavailable"),
            SpeechRecognitionResultStatus.NetworkFailure => Loc.Get("speech.online_required"),
            SpeechRecognitionResultStatus.PauseLimitExceeded => Loc.Get("speech.pause_limit"),
            _ => Loc.Get("speech.not_recognized"),
        };

    private static string ShortError(Exception ex) =>
        ex.HResult switch
        {
            unchecked((int)0x80045509) => Loc.Get("speech.online_required"),
            unchecked((int)0x80070005) => Loc.Get("speech.mic_denied"),
            _ => ex.Message,
        };
}
