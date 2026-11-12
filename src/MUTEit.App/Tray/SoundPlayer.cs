using MUTEit.Core.Localization;
using MUTEit.Core.Settings;
using MUTEit.Platform.Windows;

namespace MUTEit.App.Tray;

/// <summary>
/// Confirmation sounds on the chosen output device (own WASAPI output, see <see cref="SoundOutput"/>).
/// Sounds are decoded once and kept in memory, so push-to-talk plays without delay. Sources: built-in sounds,
/// Windows sounds (%WINDIR%\Media) or own WAV files.
/// </summary>
internal sealed class SoundPlayer : IDisposable
{
    private readonly Dictionary<string, SoundData> _cache = [];
    private readonly SoundOutput _output = new();

    /// <summary>Last problem (missing file, unknown format, device gone), null = fine.</summary>
    public string? Error { get; private set; }

    /// <summary>Plays the mute (<paramref name="muted"/> = true) or unmute sound.</summary>
    public void Play(bool muted, MuteitSettings settings)
    {
        _output.Stop();
        if (settings.SoundVolume <= 0)
        {
            return;
        }

        try
        {
            var sound = Load(muted ? settings.MuteSoundPath : settings.UnmuteSoundPath, muted);
            var device = DeviceSelection.IsSpecific(settings.OutputDevice) ? settings.OutputDevice : null;
            _output.Play(sound, device, settings.SoundVolume / 100f);
            Error = null;
        }
        catch (Exception ex)
        {
            // A sound must never take the tray down: report it on the microphone page instead.
            Error = ex is FileNotFoundException
                ? Loc.F("SoundMissingFmt", Path.GetFileName(ex.Message))
                : Loc.F("SoundFailedFmt", ex.Message);
            _output.Stop();
        }
    }

    /// <summary>Settings changed (sound files): decode again on the next play.</summary>
    public void ClearCache() => _cache.Clear();

    public void Dispose()
    {
        _output.Dispose();
        _cache.Clear();
    }

    private SoundData Load(string? path, bool muted)
    {
        var key = path ?? (muted ? "<mute>" : "<unmute>");
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        SoundData sound;
        if (path is null)
        {
            var name = muted ? "mute.wav" : "unmute.wav";
            using var stream = typeof(SoundPlayer).Assembly.GetManifestResourceStream("MUTEit.Assets.sounds." + name)
                               ?? throw new InvalidOperationException("Missing embedded sound: " + name);
            sound = WavDecoder.Decode(stream);
        }
        else
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(path);
            }

            sound = WavDecoder.Decode(path);
        }

        _cache[key] = sound;
        return sound;
    }
}
