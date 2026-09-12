using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Windtranslator.Services;

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
