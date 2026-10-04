using System.Globalization;
using System.Text;

namespace MazeGame.Core;

/// <summary>
/// Reads and writes the original "mg2ex" level string, e.g.
/// <c>1_70_40_#f0f000_1_1_1_1_1_5_0_nul_x9_mg2ex_1.0v_0_0_0_test_0_0_0_0_0_0_69_420_10a7z..._count_idx_type_spedi_...</c>
/// <para>
/// Header = 34 '_' separated tokens. Then tiles as run-length pairs "{tile}{letter}" (letter a-z = run 1..26,
/// empty tile = id 2) walking row by row (y outer loop, x inner loop). Then the object list.
/// </para>
/// </summary>
public static class LevelCodec
{
    private const int HeaderTokens = 34;

    public static LevelData? Decode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var r = new Reader(text);

        if (r.Token() != "1") return null;       // validity marker

        var level = new LevelData();
        level.Width = ParseInt(r.Token(), 0);
        level.Height = ParseInt(r.Token(), 0);
        if (level.Width <= 0 || level.Height <= 0 || (long)level.Width * level.Height > 4_000_000) return null;
        level.BackgroundColor = r.Token();

        r.Token(); r.Token();                    // two reserved "1"s
        level.Backdrop = ParseInt(r.Token(), 1);
        r.Token(); r.Token();                    // two reserved "1"s
        level.Flags = ParseInt(r.Token(), 0);
        level.CameraMode = ParseInt(r.Token(), 0) & 0xFF;
        for (int i = 0; i < 9; i++) r.Token();   // "nul" x9
        level.ForkVersion = r.Token();
        level.Version = r.Token();
        r.Token(); r.Token(); r.Token();
        level.Name = r.Token();
        level.Difficulty = ParseInt(r.Token(), 0);
        level.Song = r.Token();
        for (int i = 0; i < 4; i++) r.Token();
        level.Custom = ParseInt(r.Token(), 0);
        level.ExtraFlags = ParseInt(r.Token(), 0);

        // ---- tiles
        int w = level.Width, h = level.Height;
        level.Tiles = new int[w * h];
        int filled = 0;
        int cx = 0, cy = 0;                      // walk: y outer, x inner
        while (filled < w * h && !r.End)
        {
            string digits = r.Digits();
            int tile = digits.Length == 0 ? TileInfo.Air : ParseInt(digits, TileInfo.Air);
            char letter = r.Next();
            int run = letter >= 'a' && letter <= 'z' ? letter - 'a' + 1 : 1;
            for (int k = 0; k < run && filled < w * h; k++)
            {
                level.Tiles[cx * h + cy] = tile;
                filled++;
                cx++;
                if (cx >= w) { cx = 0; cy++; }
            }
        }
        for (int i = 0; i < level.Tiles.Length; i++)
            if (level.Tiles[i] == 0) level.Tiles[i] = TileInfo.Air;

        // ---- objects
        int count = ParseInt(r.Token(), 0);
        for (int i = 0; i < count && !r.End; i++)
        {
            int idx = ParseInt(r.Token(), 0);
            int type = ParseInt(r.Token(), 0);
            string param = r.Token();
            if (idx < 1 || type <= 0) continue;
            level.Entities.Add(new EntityDef { X = (idx - 1) / h, Y = (idx - 1) % h, Type = type, Param = param });
        }
        return level;
    }

    public static string Encode(LevelData l)
    {
        var sb = new StringBuilder(l.Width * l.Height / 4 + 256);
        void W(string s) { sb.Append(s).Append('_'); }

        W("1");
        W(l.Width.ToString(CultureInfo.InvariantCulture));
        W(l.Height.ToString(CultureInfo.InvariantCulture));
        W(l.BackgroundColor);
        W("1"); W("1");
        W(l.Backdrop.ToString(CultureInfo.InvariantCulture));
        W("1"); W("1");
        W(l.Flags.ToString(CultureInfo.InvariantCulture));
        W(l.CameraMode.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < 9; i++) W("nul");
        W(l.ForkVersion);
        W(l.Version);
        W("0"); W("0"); W("0");
        W(l.Name);
        W(l.Difficulty.ToString(CultureInfo.InvariantCulture));
        W(l.Song);
        for (int i = 0; i < 4; i++) W("0");
        W(l.Custom.ToString(CultureInfo.InvariantCulture));
        W(l.ExtraFlags.ToString(CultureInfo.InvariantCulture));

        // tiles, row by row
        int w = l.Width, h = l.Height;
        int cur = -1, len = 0;
        void Flush()
        {
            if (len == 0) return;
            if (cur != TileInfo.Air) sb.Append(cur.ToString(CultureInfo.InvariantCulture));
            sb.Append((char)('a' + len - 1));
        }
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int t = l.Tiles[x * h + y];
                if (len > 0 && len < 26 && t == cur) { len++; continue; }
                Flush();
                cur = t;
                len = 1;
            }
        }
        Flush();

        // objects
        W(l.Entities.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var e in l.Entities)
        {
            W((1 + e.Y + e.X * h).ToString(CultureInfo.InvariantCulture));
            W(e.Type.ToString(CultureInfo.InvariantCulture));
            W(e.Param ?? "");
        }
        return sb.ToString();
    }

    /// <summary>Parses the "gs_N_" settings slot: returns the startup level number or null.</summary>
    public static int? DecodeSettings(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith("gs_", StringComparison.Ordinal)) return null;
        var parts = text.Split('_');
        return parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : null;
    }

    private static int ParseInt(string s, int fallback) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private sealed class Reader
    {
        private readonly string _s;
        private int _i;
        public Reader(string s) { _s = s; }
        public bool End => _i >= _s.Length;

        public char Next() => _i < _s.Length ? _s[_i++] : '\0';

        /// <summary>Reads up to the next '_' (consumed). Empty string if there is nothing.</summary>
        public string Token()
        {
            int start = _i;
            while (_i < _s.Length && _s[_i] != '_') _i++;
            string t = _s.Substring(start, _i - start);
            if (_i < _s.Length) _i++;
            return t;
        }

        public string Digits()
        {
            int start = _i;
            while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
            return _s.Substring(start, _i - start);
        }
    }
}
