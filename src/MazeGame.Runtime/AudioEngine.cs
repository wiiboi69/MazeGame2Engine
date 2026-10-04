using NLayer;
using Silk.NET.OpenAL;

namespace MazeGame.Runtime;

/// <summary>
/// OpenAL audio: sound effects are decoded from 16-bit PCM WAV into buffers and played on a pool of sources;
/// music (MP3) is decoded with NLayer and streamed through a queue of buffers on a dedicated source.
/// Call <see cref="Update"/> every frame to keep the music queue filled.
/// </summary>
public sealed unsafe class AudioEngine : IDisposable
{
    private readonly AL? _al;
    private readonly ALContext? _alc;
    private Device* _device;
    private Context* _context;

    private readonly Dictionary<string, uint> _sfx = new();
    private readonly uint[] _sources = new uint[16];
    private int _nextSource;

    private const int MusicBuffers = 4;
    private const int ChunkSamples = 48000;           // ~0.5 s of stereo float samples per chunk
    private readonly uint[] _musicBuf = new uint[MusicBuffers];
    private uint _musicSource;
    private MpegFile? _mpeg;
    private string? _musicPath;
    private bool _musicLoop;
    private readonly float[] _floatChunk = new float[ChunkSamples];
    private readonly short[] _shortChunk = new short[ChunkSamples];
    private bool _musicEnded;

    public bool Available { get; }
    public string SoundDir { get; }
    public float SfxVolume { get; set; } = 0.8f;

    private float _musicVolume = 0.5f;
    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Math.Clamp(value, 0f, 1f);
            if (Available) _al!.SetSourceProperty(_musicSource, SourceFloat.Gain, _musicVolume);
        }
    }

    public AudioEngine(string audioDir)
    {
        SoundDir = audioDir;
        try
        {
            _alc = ALContext.GetApi();
            _al = AL.GetApi();
            _device = _alc.OpenDevice("");
            if (_device == null) return;
            _context = _alc.CreateContext(_device, null);
            _alc.MakeContextCurrent(_context);
            for (int i = 0; i < _sources.Length; i++) _sources[i] = _al.GenSource();
            _musicSource = _al.GenSource();
            for (int i = 0; i < MusicBuffers; i++) _musicBuf[i] = _al.GenBuffer();
            Available = true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[audio] OpenAL unavailable: " + ex.Message);
        }
    }

    // ================================================================ sound effects

    /// <summary>Plays assets/audio/sfx/{name}.wav. Unknown names are ignored.</summary>
    public void PlaySfx(string name, float volume = 1f)
    {
        if (!Available) return;
        if (!_sfx.TryGetValue(name, out uint buf))
        {
            buf = LoadWav(Path.Combine(SoundDir, "sfx", name + ".wav"));
            _sfx[name] = buf;
        }
        if (buf == 0) return;

        uint src = 0;
        for (int i = 0; i < _sources.Length; i++)
        {
            uint cand = _sources[(_nextSource + i) % _sources.Length];
            _al!.GetSourceProperty(cand, GetSourceInteger.SourceState, out int state);
            if ((SourceState)state != SourceState.Playing) { src = cand; _nextSource = (_nextSource + i + 1) % _sources.Length; break; }
        }
        if (src == 0)
        {
            src = _sources[_nextSource];
            _nextSource = (_nextSource + 1) % _sources.Length;
        }
        _al!.SourceStop(src);
        _al.SetSourceProperty(src, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(src, SourceFloat.Gain, Math.Clamp(SfxVolume * volume, 0f, 1f));
        _al.SourcePlay(src);
    }

    private uint LoadWav(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 44 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F') return 0;
            int pos = 12;
            int channels = 1, rate = 44100, bits = 16;
            int dataStart = -1, dataLen = 0;
            while (pos + 8 <= d.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(d, pos, 4);
                int len = BitConverter.ToInt32(d, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(d, body + 2);
                    rate = BitConverter.ToInt32(d, body + 4);
                    bits = BitConverter.ToInt16(d, body + 14);
                }
                else if (id == "data")
                {
                    dataStart = body;
                    dataLen = Math.Min(len, d.Length - body);
                    break;
                }
                pos = body + len + (len & 1);
            }
            if (dataStart < 0 || bits != 16 || channels < 1 || channels > 2) return 0;

            uint buffer = _al!.GenBuffer();
            var format = channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
            fixed (byte* p = &d[dataStart])
                _al.BufferData(buffer, format, p, dataLen, rate);
            return buffer;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[audio] cannot load {path}: {ex.Message}");
            return 0;
        }
    }

    // ================================================================ music

    public void PlayMusic(string name, bool loop = true)
    {
        if (!Available) return;
        string path = Path.Combine(SoundDir, "music", name + ".mp3");
        if (!File.Exists(path)) return;
        if (_musicPath == path && IsMusicPlaying) return;

        StopMusic();
        try
        {
            _mpeg = new MpegFile(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[audio] cannot open music {path}: {ex.Message}");
            return;
        }
        _musicPath = path;
        _musicLoop = loop;
        _musicEnded = false;

        int queued = 0;
        for (int i = 0; i < MusicBuffers; i++)
        {
            if (!FillBuffer(_musicBuf[i])) break;
            uint b = _musicBuf[i];
            _al!.SourceQueueBuffers(_musicSource, 1, &b);
            queued++;
        }
        _al!.SetSourceProperty(_musicSource, SourceFloat.Gain, _musicVolume);
        if (queued > 0) _al.SourcePlay(_musicSource);
    }

    public bool IsMusicPlaying
    {
        get
        {
            if (!Available) return false;
            _al!.GetSourceProperty(_musicSource, GetSourceInteger.SourceState, out int state);
            return (SourceState)state == SourceState.Playing;
        }
    }

    public void StopMusic()
    {
        if (!Available) return;
        _al!.SourceStop(_musicSource);
        _al.GetSourceProperty(_musicSource, GetSourceInteger.BuffersQueued, out int queued);
        while (queued-- > 0)
        {
            uint b;
            _al.SourceUnqueueBuffers(_musicSource, 1, &b);
        }
        _mpeg?.Dispose();
        _mpeg = null;
        _musicPath = null;
    }

    public void PauseMusic(bool paused)
    {
        if (!Available) return;
        if (paused) _al!.SourcePause(_musicSource);
        else if (_musicPath != null) _al!.SourcePlay(_musicSource);
    }

    /// <summary>Decodes the next chunk into <paramref name="buffer"/>. False when the stream is finished.</summary>
    private bool FillBuffer(uint buffer)
    {
        if (_mpeg == null || _musicEnded) return false;
        int n = _mpeg.ReadSamples(_floatChunk, 0, _floatChunk.Length);
        if (n <= 0 && _musicLoop && _musicPath != null)
        {
            _mpeg.Dispose();
            _mpeg = new MpegFile(_musicPath);
            n = _mpeg.ReadSamples(_floatChunk, 0, _floatChunk.Length);
        }
        if (n <= 0) { _musicEnded = true; return false; }
        for (int i = 0; i < n; i++)
        {
            float f = Math.Clamp(_floatChunk[i], -1f, 1f);
            _shortChunk[i] = (short)(f * 32767f);
        }
        var format = _mpeg.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
        fixed (short* p = _shortChunk)
            _al!.BufferData(buffer, format, p, n * sizeof(short), _mpeg.SampleRate);
        return true;
    }

    /// <summary>Keep the streaming queue topped up. Call once per frame.</summary>
    public void Update()
    {
        if (!Available || _mpeg == null) return;
        _al!.GetSourceProperty(_musicSource, GetSourceInteger.BuffersProcessed, out int processed);
        while (processed-- > 0)
        {
            uint b;
            _al.SourceUnqueueBuffers(_musicSource, 1, &b);
            if (FillBuffer(b)) _al.SourceQueueBuffers(_musicSource, 1, &b);
        }
        _al.GetSourceProperty(_musicSource, GetSourceInteger.BuffersQueued, out int stillQueued);
        _al.GetSourceProperty(_musicSource, GetSourceInteger.SourceState, out int state);
        if (stillQueued > 0 && (SourceState)state == SourceState.Stopped) _al.SourcePlay(_musicSource);   // underrun
    }

    public void Dispose()
    {
        if (!Available) return;
        StopMusic();
        foreach (var s in _sources) _al!.DeleteSource(s);
        _al!.DeleteSource(_musicSource);
        foreach (var b in _sfx.Values) if (b != 0) _al.DeleteBuffer(b);
        foreach (var b in _musicBuf) _al.DeleteBuffer(b);
        _alc!.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
        _al.Dispose();
        _alc.Dispose();
    }
}
