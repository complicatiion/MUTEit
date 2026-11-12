using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace MUTEit.Platform.Windows;

/// <summary>Decoded sound: interleaved 32-bit float samples (1 or 2 channels).</summary>
public sealed record SoundData(float[] Samples, int SampleRate, int Channels)
{
    public int Frames => Samples.Length / Channels;
}

/// <summary>
/// WAV decoder (PCM 8 / 16 / 24 / 32 bit, float 32 / 64 bit, WAVE_FORMAT_EXTENSIBLE). More than two channels are
/// reduced to the first two; sounds are cut after <c>maxSeconds</c>.
/// </summary>
public static class WavDecoder
{
    public static SoundData Decode(string path, int maxSeconds = 10)
    {
        using var stream = File.OpenRead(path);
        return Decode(stream, maxSeconds);
    }

    public static SoundData Decode(Stream stream, int maxSeconds = 10)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var data = memory.GetBuffer().AsSpan(0, (int)memory.Length);

        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Not a WAV file.");
        }

        int format = 0, channels = 0, rate = 0, bits = 0;
        ReadOnlySpan<byte> samples = default;
        var found = false;
        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            var id = data.Slice(pos, 4);
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 4, 4)), (uint)(data.Length - pos - 8));
            var body = data.Slice(pos + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == 0xFFFE && size >= 26)
                {
                    format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]); // first two bytes of the sub-format GUID
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                samples = body;
                found = true;
                break;
            }

            pos += 8 + size + (size & 1);
        }

        if (!found || channels < 1 || rate < 1000 || bits == 0)
        {
            throw new InvalidDataException("WAV file without usable audio data.");
        }

        var isFloat = format == 3;
        if (!isFloat && format != 1 || isFloat && bits is not (32 or 64) || !isFloat && bits is not (8 or 16 or 24 or 32))
        {
            throw new NotSupportedException("Only PCM or float WAV files are supported.");
        }

        var bytesPerSample = bits / 8;
        var frameBytes = bytesPerSample * channels;
        var frames = Math.Min(samples.Length / frameBytes, rate * maxSeconds);
        var outChannels = Math.Min(channels, 2);
        var result = new float[frames * outChannels];
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < outChannels; c++)
            {
                var s = samples.Slice((f * frameBytes) + (c * bytesPerSample), bytesPerSample);
                result[(f * outChannels) + c] = (isFloat, bits) switch
                {
                    (true, 32) => BinaryPrimitives.ReadSingleLittleEndian(s),
                    (true, _) => (float)BinaryPrimitives.ReadDoubleLittleEndian(s),
                    (false, 8) => (s[0] - 128) / 128f,
                    (false, 16) => BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f,
                    (false, 24) => ((s[2] << 24 | s[1] << 16 | s[0] << 8) >> 8) / 8388608f,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f,
                };
            }
        }

        return new SoundData(result, rate, outChannels);
    }
}

/// <summary>
/// Plays a short sound on a chosen output device (WASAPI shared mode with automatic format conversion, independent
/// of the Windows default device). Driven by a timer on the calling thread's synchronisation context (tray UI thread),
/// so all Core Audio objects stay on one thread.
/// </summary>
public sealed class SoundOutput : IDisposable
{
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
    private const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;
    private const long BufferDuration = 4_000_000; // 400 ms in 100-ns units

    private static Guid _audioClientIid = typeof(IAudioClient).GUID;
    private static Guid _renderClientIid = typeof(IAudioRenderClient).GUID;

    private SynchronizationContext? _context;
    private Timer? _timer;
    private IAudioClient? _client;
    private IAudioRenderClient? _render;
    private IntPtr _format;
    private SoundData? _sound;
    private float[] _chunk = [];
    private float _volume;
    private int _position;
    private uint _bufferFrames;

    /// <summary>
    /// Starts playing (a running sound is stopped first). <paramref name="deviceId"/> null = Windows default playback
    /// device; an unplugged device falls back to the default. Throws <see cref="COMException"/> on audio errors.
    /// </summary>
    public void Play(SoundData sound, string? deviceId, float volume)
    {
        Stop();
        var enumerator = AudioDevices.Enumerator ?? throw new COMException("Core Audio not available");
        var device = OpenDevice(enumerator, deviceId);
        try
        {
            device.Activate(ref _audioClientIid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var instance);
            _client = (IAudioClient)instance;
        }
        finally
        {
            AudioDevices.Release(device);
        }

        _format = CreateFloatFormat(sound.SampleRate, sound.Channels);
        _client.Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
            BufferDuration, 0, _format, IntPtr.Zero);
        _client.GetBufferSize(out _bufferFrames);
        _client.GetService(ref _renderClientIid, out var render);
        _render = (IAudioRenderClient)render;

        _sound = sound;
        _volume = Math.Clamp(volume, 0f, 1f);
        _position = 0;
        Fill();
        _client.Start();

        _context = SynchronizationContext.Current;
        _timer = new Timer(_ =>
        {
            if (_context is { } context)
            {
                context.Post(_ => Tick(), null);
            }
            else
            {
                Tick();
            }
        }, null, 20, 20);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        if (_client is not null)
        {
            try
            {
                _client.Stop();
            }
            catch (COMException)
            {
                // device gone
            }
        }

        AudioDevices.Release(_render);
        AudioDevices.Release(_client);
        _render = null;
        _client = null;
        _sound = null;
        if (_format != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_format);
            _format = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();

    private static IMMDevice OpenDevice(IMMDeviceEnumerator enumerator, string? deviceId)
    {
        if (deviceId is not null)
        {
            try
            {
                enumerator.GetDevice(deviceId, out var device);
                device.GetState(out var state);
                if (state == DeviceStates.Active)
                {
                    return device;
                }

                AudioDevices.Release(device);
            }
            catch (COMException)
            {
                // unplugged: default device
            }
        }

        enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var fallback);
        return fallback;
    }

    /// <summary>WAVEFORMATEX, IEEE float 32 bit.</summary>
    private static IntPtr CreateFloatFormat(int rate, int channels)
    {
        var block = (ushort)(channels * 4);
        var format = Marshal.AllocHGlobal(18);
        Marshal.WriteInt16(format, 0, 3); // WAVE_FORMAT_IEEE_FLOAT
        Marshal.WriteInt16(format, 2, (short)channels);
        Marshal.WriteInt32(format, 4, rate);
        Marshal.WriteInt32(format, 8, rate * block);
        Marshal.WriteInt16(format, 12, (short)block);
        Marshal.WriteInt16(format, 14, 32);
        Marshal.WriteInt16(format, 16, 0);
        return format;
    }

    private void Tick()
    {
        if (_client is null || _sound is null)
        {
            return;
        }

        try
        {
            if (_position < _sound.Frames)
            {
                Fill();
                return;
            }

            // Everything written: stop once the device has played the rest.
            _client.GetCurrentPadding(out var padding);
            if (padding == 0)
            {
                Stop();
            }
        }
        catch (COMException)
        {
            Stop();
        }
    }

    private void Fill()
    {
        if (_client is null || _render is null || _sound is null)
        {
            return;
        }

        _client.GetCurrentPadding(out var padding);
        var frames = (int)Math.Min(_bufferFrames - padding, (uint)(_sound.Frames - _position));
        if (frames <= 0)
        {
            return;
        }

        var count = frames * _sound.Channels;
        if (_chunk.Length < count)
        {
            _chunk = new float[count];
        }

        var start = _position * _sound.Channels;
        for (var i = 0; i < count; i++)
        {
            _chunk[i] = _sound.Samples[start + i] * _volume;
        }

        _render.GetBuffer((uint)frames, out var target);
        Marshal.Copy(_chunk, 0, target, count);
        _render.ReleaseBuffer((uint)frames, 0);
        _position += frames;
    }
}
