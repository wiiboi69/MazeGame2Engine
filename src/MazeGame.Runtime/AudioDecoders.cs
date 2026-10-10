using NLayer;
using NVorbis;

namespace MazeGame.Runtime;

/// <summary>A decoded-on-demand PCM source (float samples, interleaved).</summary>
public interface IPcmStream : IDisposable
{
    int Channels { get; }
    int SampleRate { get; }
    int Read(float[] buffer, int count);
}

/// <summary>Opens .ogg (Vorbis), .mp3 and .wav files as <see cref="IPcmStream"/>.</summary>
public static class AudioDecoders
{
    public static readonly string[] Extensions = { ".ogg", ".mp3", ".wav" };

    /// <summary>Finds assets/audio/.../name.(ogg|mp3|wav); a name that already has an extension is used as is.</summary>
    public static string? Find(string dir, string name)
    {
        string direct = Path.Combine(dir, name);
        if (Extensions.Contains(Path.GetExtension(name).ToLowerInvariant()) && File.Exists(direct)) return direct;
        foreach (var ext in Extensions)
            if (File.Exists(direct + ext)) return direct + ext;
        return null;
    }

    public static IPcmStream? Open(string path)
    {
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg": return new OggStream(path);
                case ".mp3": return new Mp3Stream(path);
                case ".wav": return WavStream.Open(path);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[audio] cannot open {path}: {ex.Message}"); }
        return null;
    }

    /// <summary>Decodes a whole file to 16-bit PCM (used for sound effects).</summary>
    public static (short[] pcm, int channels, int rate)? DecodeAll(string path)
    {
        using var s = Open(path);
        if (s == null || s.Channels < 1 || s.Channels > 2) return null;
        var all = new List<short>();
        var buf = new float[16384];
        int n;
        while ((n = s.Read(buf, buf.Length)) > 0)
            for (int i = 0; i < n; i++) all.Add((short)(Math.Clamp(buf[i], -1f, 1f) * 32767f));
        return all.Count == 0 ? null : (all.ToArray(), s.Channels, s.SampleRate);
    }

    private sealed class OggStream : IPcmStream
    {
        private readonly VorbisReader _r;
        public OggStream(string path) { _r = new VorbisReader(path); }
        public int Channels => _r.Channels;
        public int SampleRate => _r.SampleRate;
        public int Read(float[] buffer, int count) => _r.ReadSamples(buffer, 0, count);
        public void Dispose() => _r.Dispose();
    }

    private sealed class Mp3Stream : IPcmStream
    {
        private readonly MpegFile _f;
        public Mp3Stream(string path) { _f = new MpegFile(path); }
        public int Channels => _f.Channels;
        public int SampleRate => _f.SampleRate;
        public int Read(float[] buffer, int count) => _f.ReadSamples(buffer, 0, count);
        public void Dispose() => _f.Dispose();
    }

    /// <summary>PCM 8/16/24/32-bit and 32-bit float WAV, held in memory.</summary>
    private sealed class WavStream : IPcmStream
    {
        private readonly float[] _data;
        private int _pos;
        public int Channels { get; }
        public int SampleRate { get; }
        private WavStream(float[] d, int ch, int rate) { _data = d; Channels = ch; SampleRate = rate; }

        public static WavStream? Open(string path)
        {
            byte[] d = File.ReadAllBytes(path);
            if (d.Length < 44 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F') return null;
            int pos = 12, channels = 1, rate = 44100, bits = 16, fmtTag = 1, start = -1, len = 0;
            while (pos + 8 <= d.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(d, pos, 4);
                int l = BitConverter.ToInt32(d, pos + 4), body = pos + 8;
                if (id == "fmt ")
                {
                    fmtTag = BitConverter.ToUInt16(d, body);
                    channels = BitConverter.ToInt16(d, body + 2);
                    rate = BitConverter.ToInt32(d, body + 4);
                    bits = BitConverter.ToInt16(d, body + 14);
                    if (fmtTag == 0xFFFE && l >= 26) fmtTag = BitConverter.ToUInt16(d, body + 24);   // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data") { start = body; len = Math.Min(l < 0 ? int.MaxValue : l, d.Length - body); break; }
                pos = body + l + (l & 1);
            }
            if (start < 0 || channels < 1) return null;
            int bytes = bits / 8;
            if (bytes < 1 || bytes > 4) return null;
            int n = len / bytes;
            var f = new float[n];
            for (int i = 0; i < n; i++)
            {
                int o = start + i * bytes;
                f[i] = (fmtTag, bits) switch
                {
                    (3, 32) => BitConverter.ToSingle(d, o),
                    (_, 8) => (d[o] - 128) / 128f,
                    (_, 16) => BitConverter.ToInt16(d, o) / 32768f,
                    (_, 24) => ((d[o] << 8 | d[o + 1] << 16 | d[o + 2] << 24) >> 8) / 8388608f,
                    (_, 32) => BitConverter.ToInt32(d, o) / 2147483648f,
                    _ => 0f,
                };
            }
            return new WavStream(f, channels, rate);
        }

        public int Read(float[] buffer, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, 0, n);
            _pos += n;
            return n;
        }

        public void Dispose() { }
    }
}
