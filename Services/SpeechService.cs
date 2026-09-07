using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechRecognition;
using Windows.Media.SpeechSynthesis;

namespace Windtranslator.Services;

public sealed class SpeechInputService
{
    private SpeechRecognizer? _recognizer;
    private bool _isStopping;

    public bool IsListening => _recognizer is not null;

    public async Task StartAsync(
        string? languageTag,
        Action<string> onResult,
        Action<string> onStateChanged,
        Action<string?> onCompleted)
    {
        if (_recognizer is not null)
        {
            return;
        }

        var language = ResolveRecognitionLanguage(languageTag);
        var recognizer = new SpeechRecognizer(language);
        recognizer.Constraints.Add(
            new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));

        var compilation = await recognizer.CompileConstraintsAsync();
        if (compilation.Status != SpeechRecognitionResultStatus.Success)
        {
            recognizer.Dispose();
            throw new InvalidOperationException($"语音识别器无法初始化（{compilation.Status}）。");
        }

        recognizer.ContinuousRecognitionSession.ResultGenerated += (_, args) =>
        {
            var text = args.Result.Text;
            if (args.Result.Confidence != SpeechRecognitionConfidence.Rejected
                && !string.IsNullOrWhiteSpace(text))
            {
                onResult(text);
            }
        };

        recognizer.StateChanged += (_, args) =>
        {
            if (args.State == SpeechRecognizerState.Capturing)
            {
                onStateChanged($"正在使用 {language.DisplayName} 进行语音识别。");
            }
        };
        recognizer.ContinuousRecognitionSession.Completed += (_, args) =>
        {
            if (_isStopping || !ReferenceEquals(_recognizer, recognizer))
            {
                return;
            }

            _recognizer = null;
            recognizer.Dispose();
            onCompleted(GetCompletionError(args.Status));
        };

        _isStopping = false;
        _recognizer = recognizer;
        try
        {
            await recognizer.ContinuousRecognitionSession.StartAsync();
        }
        catch
        {
            _recognizer = null;
            recognizer.Dispose();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_recognizer is null)
        {
            return;
        }

        var recognizer = _recognizer;
        _recognizer = null;
        _isStopping = true;
        try
        {
            await recognizer.ContinuousRecognitionSession.StopAsync();
        }
        catch
        {
            // Ignore stop failures; disposal still releases the recognizer.
        }

        recognizer.Dispose();
        _isStopping = false;
    }

    private static Language ResolveRecognitionLanguage(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return SpeechRecognizer.SystemSpeechLanguage
                ?? throw new InvalidOperationException(
                    "Windows 未安装语音识别语言包，请先在系统语言设置中安装。");
        }

        var exactMatch = SpeechRecognizer.SupportedTopicLanguages.FirstOrDefault(
            language => string.Equals(language.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        var languagePrefix = languageTag.Split('-')[0];
        var languageMatch = SpeechRecognizer.SupportedTopicLanguages.FirstOrDefault(
            language => language.LanguageTag.StartsWith(languagePrefix + "-", StringComparison.OrdinalIgnoreCase));
        return languageMatch
            ?? throw new InvalidOperationException(
                $"Windows 未安装 {languageTag} 的语音识别语言包，请在系统语言设置中安装后重试。");
    }

    private static string? GetCompletionError(SpeechRecognitionResultStatus status)
    {
        return status switch
        {
            SpeechRecognitionResultStatus.Success => null,
            SpeechRecognitionResultStatus.UserCanceled => null,
            SpeechRecognitionResultStatus.MicrophoneUnavailable => "麦克风不可用，请检查设备连接以及 Windows 麦克风权限。",
            SpeechRecognitionResultStatus.NetworkFailure => "语音识别网络连接失败，请检查网络后重试。",
            SpeechRecognitionResultStatus.TopicLanguageNotSupported => "当前语言不支持 Windows 在线语音识别。",
            SpeechRecognitionResultStatus.TimeoutExceeded => "长时间未检测到语音，语音识别已自动停止。",
            SpeechRecognitionResultStatus.PauseLimitExceeded => "停顿时间过长，语音识别已自动停止。",
            _ => $"语音识别已停止（{status}）。",
        };
    }
}

public sealed class SpeechOutputService
{
    private SpeechSynthesizer? _synthesizer;
    private MediaPlayer? _player;

    public bool IsSpeaking { get; private set; }

    public event EventHandler? PlaybackEnded;

    private SpeechSynthesizer Synthesizer => _synthesizer ??= new SpeechSynthesizer();

    public async Task SpeakAsync(string text, string? languageTag)
    {
        Stop();

        if (languageTag is not null)
        {
            var languagePrefix = languageTag.Split('-')[0];
            var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(
                    voice => string.Equals(voice.Language, languageTag, StringComparison.OrdinalIgnoreCase))
                ?? SpeechSynthesizer.AllVoices.FirstOrDefault(
                    voice => voice.Language.StartsWith(languagePrefix + "-", StringComparison.OrdinalIgnoreCase));
            if (voice is not null)
            {
                Synthesizer.Voice = voice;
            }
        }

        var stream = await Synthesizer.SynthesizeTextToStreamAsync(text);
        var player = new MediaPlayer();
        player.MediaEnded += OnMediaEnded;
        player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
        _player = player;
        IsSpeaking = true;
        player.Play();
    }

    public void Stop()
    {
        IsSpeaking = false;
        _player?.Pause();
        if (_player is not null)
        {
            _player.Source = null;
        }

        _player = null;
    }

    private void OnMediaEnded(MediaPlayer sender, object args)
    {
        IsSpeaking = false;
        if (ReferenceEquals(_player, sender))
        {
            _player = null;
        }

        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }
}
