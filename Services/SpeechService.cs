using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechRecognition;
using Windows.Media.SpeechSynthesis;

namespace Windtranslator.Services;

public sealed class SpeechInputService
{
    private SpeechRecognizer? _recognizer;

    public bool IsListening => _recognizer is not null;

    public async Task StartAsync(Action<string> onResult, Action<string> onStateChanged)
    {
        if (_recognizer is not null)
        {
            return;
        }

        var recognizer = new SpeechRecognizer();
        var compilation = await recognizer.CompileConstraintsAsync();
        if (compilation.Status != SpeechRecognitionResultStatus.Success)
        {
            recognizer.Dispose();
            throw new InvalidOperationException("语音识别器无法初始化，请检查麦克风权限。");
        }

        recognizer.ContinuousRecognitionSession.ResultGenerated += (_, args) =>
        {
            var text = args.Result.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                onResult(text);
            }
        };

        recognizer.StateChanged += (_, args) => onStateChanged($"语音识别状态：{args.State}");

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
        try
        {
            await recognizer.ContinuousRecognitionSession.StopAsync();
        }
        catch
        {
            // Ignore stop failures; disposal still releases the recognizer.
        }

        recognizer.Dispose();
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
            var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(
                voice => voice.Language.StartsWith(languageTag, StringComparison.OrdinalIgnoreCase));
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
