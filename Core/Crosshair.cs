using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DeadlockMeta.Core;

/// <summary>The reticle settings, in the order of the 19-number codes the community sites show.</summary>
public enum CrosshairField
{
    Gap, Width, Height, StaticGap, PipOpacity, PipBorder, PipBorderGap, PipBorderOpacity,
    DotSize, DotOpacity, DotBorder, DotBorderGap, DotBorderOpacity, R, G, B, OutlineR, OutlineG, OutlineB,
}

/// <summary>
/// How one setting is called in each place it shows up: the game's settings file (crosshair_pip_gap), the old
/// console variable (citadel_crosshair_pip_gap) and the key hash in the game's share code ("DL.…").
/// </summary>
public sealed record CrosshairSetting(CrosshairField Field, string Name, string CodeKey, double Min, double Max, double Step)
{
    public string FileKey => "crosshair_" + Name;
    public string Convar => "citadel_crosshair_" + Name;
    public bool IsBool => Field == CrosshairField.StaticGap;
}

/// <summary>One reticle. Kept as plain numbers so the editor, the codes and the comparisons share one table.</summary>
public sealed class Crosshair
{
    public static readonly CrosshairSetting[] Settings =
    [
        new(CrosshairField.Gap, "pip_gap", "cba4d1e907", -20, 50, 1),
        new(CrosshairField.Width, "pip_width", "ed9feaa606", 0, 12, 0.5),
        new(CrosshairField.Height, "pip_height", "b4c4d6f30c", 0, 40, 0.5),
        new(CrosshairField.StaticGap, "pip_gap_static", "c6f8f7fa06", 0, 1, 1),
        new(CrosshairField.PipOpacity, "pip_opacity", "e5dd99d204", 0, 1, 0.05),
        new(CrosshairField.PipBorder, "pip_outline_border", "f387a7b603", 0, 6, 1),
        new(CrosshairField.PipBorderGap, "pip_outline_gap", "f7f1d4a005", 0, 10, 1),
        new(CrosshairField.PipBorderOpacity, "pip_outline_opacity", "d6c1ddbe0c", 0, 1, 0.05),
        new(CrosshairField.DotSize, "dot_size", "92c2c9f009", 0, 30, 0.5),
        new(CrosshairField.DotOpacity, "dot_opacity", "d5e58779", 0, 1, 0.05),
        new(CrosshairField.DotBorder, "dot_outline_border", "9dbafaf70f", 0, 6, 1),
        new(CrosshairField.DotBorderGap, "dot_outline_gap", "dce0a6d00f", 0, 40, 1),
        new(CrosshairField.DotBorderOpacity, "dot_outline_opacity", "d9b285a908", 0, 1, 0.05),
        new(CrosshairField.R, "color_r", "9cc1fd8601", 0, 255, 1),
        new(CrosshairField.G, "color_g", "8188809907", 0, 255, 1),
        new(CrosshairField.B, "color_b", "b39af2e304", 0, 255, 1),
        new(CrosshairField.OutlineR, "outline_color_r", "99a8d3f707", 0, 255, 1),
        new(CrosshairField.OutlineG, "outline_color_g", "dbd0e0a103", 0, 255, 1),
        new(CrosshairField.OutlineB, "outline_color_b", "eccf8be805", 0, 255, 1),
    ];

    public const int Count = 19;

    // The game's own reticle as a fresh settings file has it.
    static readonly double[] Defaults = [4, 2, 16, 0, 0.5, 1, 0, 0.7, 4, 0.7, 2, 0, 0.7, 255, 255, 255, 0, 0, 0];

    readonly double[] v;

    public Crosshair() => v = (double[])Defaults.Clone();

    Crosshair(double[] values) => v = values;

    public static Crosshair Default => new();

    public double this[CrosshairField f]
    {
        get => v[(int)f];
        set => v[(int)f] = value;
    }

    public bool StaticGap
    {
        get => v[(int)CrosshairField.StaticGap] != 0;
        set => v[(int)CrosshairField.StaticGap] = value ? 1 : 0;
    }

    public (byte R, byte G, byte B) Color => (Byte(CrosshairField.R), Byte(CrosshairField.G), Byte(CrosshairField.B));
    public (byte R, byte G, byte B) Outline => (Byte(CrosshairField.OutlineR), Byte(CrosshairField.OutlineG), Byte(CrosshairField.OutlineB));

    byte Byte(CrosshairField f) => (byte)Math.Clamp(Math.Round(v[(int)f]), 0, 255);

    public Crosshair Clone() => new((double[])v.Clone());

    /// <summary>Same reticle, ignoring rounding noise from codes and the settings file.</summary>
    public bool SameAs(Crosshair o)
    {
        for (int i = 0; i < Count; i++)
            if (Math.Abs(v[i] - o.v[i]) > 0.005) return false;
        return true;
    }

    static string Num(double x) => Math.Round(x, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The 19 numbers the community sites show, e.g. "-20 1 1 true 0 …"; also how crosshairs are stored.</summary>
    public string ToLegacyCode()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(i == (int)CrosshairField.StaticGap ? (StaticGap ? "true" : "false") : Num(v[i]));
        }
        return sb.ToString();
    }

    /// <summary>Console commands for the F7 console (older game versions read the reticle from these).</summary>
    public string ToConsole() => string.Join("; ", Settings.Select(s =>
        s.Convar + " " + (s.IsBool ? (StaticGap ? "true" : "false") : Num(v[(int)s.Field]))));

    /// <summary>The value as the game writes it into its settings file.</summary>
    public string FileValue(CrosshairSetting s) => s.IsBool ? (StaticGap ? "true" : "false") : Num(v[(int)s.Field]);

    public void SetFromText(CrosshairSetting s, string text)
    {
        text = text.Trim().Trim('"');
        if (s.IsBool) StaticGap = text is "true" or "1";
        else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && double.IsFinite(x))
            v[(int)s.Field] = x;
    }

    // ---- The game's share code ("Поделиться настройками" → "Применить код") ----

    const int CodeVersion = 6731;
    const string DisableHeroReticlesKey = "a2939bc602";

    /// <summary>
    /// The code the game's "share settings" window accepts: "DL." + base64 of a checksum and a zstd frame that
    /// holds key-hash/value pairs. The frame is written uncompressed (a raw block), which every zstd reader accepts.
    /// </summary>
    public string ToGameCode()
    {
        var payload = new List<byte>();
        void Entry(string keyHex, string value)
        {
            byte[] kb = Convert.FromHexString(keyHex), vb = Encoding.UTF8.GetBytes(value);
            var e = new List<byte> { 1, (byte)kb.Length };
            e.AddRange(kb);
            e.Add(2);
            e.Add((byte)vb.Length);
            e.AddRange(vb);
            payload.Add(2);
            payload.Add((byte)e.Count);
            payload.AddRange(e);
        }
        // Same order as the game: hero reticles off, then the settings.
        Entry(DisableHeroReticlesKey, "true");
        foreach (var s in CodeOrder) Entry(s.CodeKey, FileValue(s));

        var body = new List<byte> { 1, 2 };
        body.AddRange(Varint(CodeVersion));
        body.Add(2);
        body.AddRange(Varint(payload.Count));
        body.AddRange(payload);

        int n = body.Count, blockHeader = (n << 3) | 1;
        var frame = new List<byte> { 0x28, 0xB5, 0x2F, 0xFD, 0xA0 };
        frame.AddRange(BitConverter.GetBytes(n));
        frame.AddRange([(byte)blockHeader, (byte)(blockHeader >> 8), (byte)(blockHeader >> 16)]);
        frame.AddRange(body);

        var output = new List<byte> { 1, 1 };
        output.AddRange(BitConverter.GetBytes(Crc32(body.ToArray())));
        output.AddRange(frame);
        return "DL." + Convert.ToBase64String(output.ToArray()).Replace('+', '.').Replace('/', '_');
    }

    static readonly CrosshairSetting[] CodeOrder =
    [
        .. Settings.Where(s => s.Field == CrosshairField.StaticGap),
        .. Settings.Where(s => s.Field is CrosshairField.Width or CrosshairField.Height),
        .. Settings.Where(s => s.Field == CrosshairField.Gap),
        .. Settings.Where(s => s.Field >= CrosshairField.PipOpacity),
    ];

    static IEnumerable<byte> Varint(int n)
    {
        while (n >= 0x80)
        {
            yield return (byte)(n & 0x7F | 0x80);
            n >>= 7;
        }
        yield return (byte)n;
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc32(byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    static Crosshair? FromGameCode(string code)
    {
        string s = code.Trim()[3..].TrimEnd('=').Replace('.', '+').Replace('_', '/').Replace('-', '+');
        s += new string('=', (4 - s.Length % 4) % 4);
        byte[] raw = Convert.FromBase64String(s);
        if (raw.Length < 10 || raw[0] != 1 || raw[1] != 1) return null;
        uint crc = BitConverter.ToUInt32(raw, 2);
        byte[] body;
        using (var z = new ZstdSharp.Decompressor()) body = z.Unwrap(raw.AsSpan(6)).ToArray();
        if (Crc32(body) != crc) return null;

        // Codes the game makes can also carry key binds and per-hero settings; the reticle entries are found by
        // their key hashes, the first one of each (the base settings) wins.
        var byKey = Settings.ToDictionary(x => x.CodeKey);
        var c = new Crosshair();
        var seen = new HashSet<string>();
        for (int i = 0; i + 4 < body.Length; i++)
        {
            if (body[i] != 1) continue;
            int kl = body[i + 1];
            if (kl is < 3 or > 8 || i + 2 + kl + 2 > body.Length || body[i + 2 + kl] != 2) continue;
            string key = Convert.ToHexString(body, i + 2, kl).ToLowerInvariant();
            if (!byKey.TryGetValue(key, out var setting) || !seen.Add(key)) continue;
            int vl = body[i + 3 + kl];
            if (i + 4 + kl + vl > body.Length) continue;
            c.SetFromText(setting, Encoding.UTF8.GetString(body, i + 4 + kl, vl));
        }
        return seen.Count > 0 ? c : null;
    }

    // ---- Reading whatever the player pasted ----

    static readonly Regex GameCodeRx = new(@"DL\.[A-Za-z0-9._\-=]+", RegexOptions.Compiled);
    static readonly Regex SettingRx = new(@"(?:citadel_)?crosshair_([a-z_]+)[\s=""]+(-?[0-9.]+|true|false)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Reads a game code (DL.…), console commands or settings-file lines, or the 19 numbers from a community site.
    /// Returns null with a reason a person can act on.
    /// </summary>
    public static Crosshair? Parse(string text, out string error)
    {
        error = "";
        text = text.Trim();
        if (text.Length == 0)
        {
            error = "Вставь код прицела.";
            return null;
        }

        if (GameCodeRx.Match(text) is { Success: true } m)
        {
            try
            {
                if (FromGameCode(m.Value) is { } c) return c;
            }
            catch (Exception e) { Log.Write("crosshair code: " + e.Message); }
            error = "Код похож на код из игры, но он повреждён. Скопируй его ещё раз целиком.";
            return null;
        }

        var named = SettingRx.Matches(text);
        if (named.Count >= 3)
        {
            var c = new Crosshair();
            int found = 0;
            foreach (Match x in named)
                if (Settings.FirstOrDefault(s => s.Name.Equals(x.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) is { } s)
                {
                    c.SetFromText(s, x.Groups[2].Value);
                    found++;
                }
            if (found >= 3) return c;
        }

        var parts = text.Split([' ', '\t', '\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= Count)
        {
            var c = new Crosshair();
            for (int i = 0; i < Count; i++)
            {
                string p = parts[i];
                if (i == (int)CrosshairField.StaticGap)
                {
                    if (p is not ("true" or "false" or "1" or "0")) return Fail(out error);
                    c.StaticGap = p is "true" or "1";
                }
                else if (double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && double.IsFinite(x)) c.v[i] = x;
                else return Fail(out error);
            }
            return c;
        }
        return Fail(out error);
    }

    static Crosshair? Fail(out string error)
    {
        error = "Не получилось узнать прицел. Подходит код из игры (начинается с DL.), команды для консоли или 19 чисел с сайта.";
        return null;
    }

    public static Crosshair? FromLegacyCode(string code) => Parse(code, out _);
}
