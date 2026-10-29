using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace DeadlockMeta.Core;

/// <summary>What the HUD mod shows in a match; baked into its script when it is built.</summary>
public sealed record HudOptions(bool Timers, bool Reload, bool Zipline, bool CleanMap, int MapOpacity, int MapScale);

/// <summary>
/// The in-game HUD mod: event timers, a reload countdown, the zipline boost cooldown and a frameless minimap.
/// It is built on this computer from the game's own files, so it follows game patches: the game's base_hud layout
/// gets one more script, and the script is packed the way the game packs its own. The result goes to
/// game\citadel\addons as a VPK, and gameinfo.gi gets the addons search path (like every Deadlock mod needs).
/// Game files are only written while the game is closed, and gameinfo.gi is backed up to data\backups first.
/// </summary>
public static class HudMod
{
    const string ScriptPath = "panorama/scripts/dlmeta_hud.vts";
    const string LayoutPath = "panorama/layout/base_hud.vxml_c";
    // The game's own compiled script, whose container (edit info, header version) the mod's script copies.
    const string TemplateScript = "panorama/scripts/async.vts_c";
    static readonly string StatePath = Path.Combine(Settings.DataDir, "hudmod.json");
    static readonly string BackupDir = Path.Combine(Settings.DataDir, "backups");

    public sealed class State
    {
        /// <summary>File name in citadel\addons, e.g. pak77_dir.vpk; null when the mod is not installed.</summary>
        public string? Vpk { get; set; }
        public string? Sha256 { get; set; }
        /// <summary>The game's ClientVersion the mod was built from.</summary>
        public string? GameBuild { get; set; }
        public HudOptions? Options { get; set; }
        /// <summary>Hash of the script that went in, so a new version of this program rebuilds the mod.</summary>
        public string? Script { get; set; }
    }

    public static State Load()
    {
        try
        {
            if (File.Exists(StatePath)) return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State();
        }
        catch (Exception e) { Log.Write("hud mod state: " + e.Message); }
        return new State();
    }

    static void Save(State s) => Files.WriteAtomic(StatePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));

    static string Citadel(string gameDir) => Path.Combine(gameDir, "game", "citadel");

    /// <summary>The game's build number from steam.inf, which changes with every patch.</summary>
    public static string? GameBuild(string gameDir)
    {
        try
        {
            var m = Regex.Match(File.ReadAllText(Path.Combine(Citadel(gameDir), "steam.inf")), @"ClientVersion=(\d+)");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    /// <summary>True when the mod's VPK is in place and gameinfo.gi loads the addons folder.</summary>
    public static bool IsActive(string gameDir, State s) =>
        s.Vpk != null && File.Exists(Path.Combine(Citadel(gameDir), "addons", s.Vpk)) && HasAddonsPath(GameInfoText(gameDir));

    /// <summary>True when the installed mod was built from another game version, with other options or another script.</summary>
    public static bool IsOutdated(string gameDir, State s, HudOptions options) =>
        s.Vpk != null && (s.GameBuild != GameBuild(gameDir) || s.Options != options || s.Script != ScriptHash(options));

    static string ScriptHash(HudOptions options) => Sha(Encoding.UTF8.GetBytes(ScriptSource(options)));

    /// <summary>Builds the mod from the game's files and puts it into the game. The game must be closed.</summary>
    public static void Install(string gameDir, HudOptions options)
    {
        var state = Load();
        byte[] vpk = Vpk.Build(BuildFiles(gameDir, options));
        string addons = Path.Combine(Citadel(gameDir), "addons");
        Directory.CreateDirectory(addons);
        string name = state.Vpk ?? FreeVpkName(addons);
        string path = Path.Combine(addons, name);
        // Never overwrite a pack another mod put under the same name.
        if (File.Exists(path) && Sha(File.ReadAllBytes(path)) != state.Sha256)
        {
            name = FreeVpkName(addons);
            path = Path.Combine(addons, name);
        }
        File.WriteAllBytes(path + ".tmp", vpk);
        File.Move(path + ".tmp", path, true);
        EnableAddonsPath(gameDir);
        Save(new State { Vpk = name, Sha256 = Sha(vpk), GameBuild = GameBuild(gameDir), Options = options, Script = ScriptHash(options) });
        Log.Write($"hud mod installed: {name}, {vpk.Length} bytes, game build {GameBuild(gameDir)}");
    }

    /// <summary>
    /// Takes the mod out of the game. The addons search path stays when other mods are in that folder, since they
    /// need it too.
    /// </summary>
    public static void Remove(string gameDir)
    {
        var state = Load();
        string addons = Path.Combine(Citadel(gameDir), "addons");
        if (state.Vpk != null)
        {
            string path = Path.Combine(addons, state.Vpk);
            if (File.Exists(path) && Sha(File.ReadAllBytes(path)) == state.Sha256) File.Delete(path);
        }
        bool otherMods = Directory.Exists(addons) && Directory.EnumerateFiles(addons, "*.vpk").Any();
        if (!otherMods) DisableAddonsPath(gameDir);
        Save(new State());
        Log.Write("hud mod removed" + (otherMods ? ", addons path kept for other mods" : ""));
    }

    /// <summary>Steam puts the original gameinfo.gi back with patches; this puts the addons path in again.</summary>
    public static bool RepairGameInfo(string gameDir)
    {
        if (HasAddonsPath(GameInfoText(gameDir))) return false;
        EnableAddonsPath(gameDir);
        Log.Write("gameinfo.gi was reset by the game, addons path put back");
        return true;
    }

    static string FreeVpkName(string addons)
    {
        for (int n = 77; n < 99; n++)
        {
            string name = $"pak{n:00}_dir.vpk";
            if (!File.Exists(Path.Combine(addons, name))) return name;
        }
        throw new IOException("в папке addons нет свободного имени для мода");
    }

    static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    // ---- gameinfo.gi ----

    static string GameInfoPath(string gameDir) => Path.Combine(Citadel(gameDir), "gameinfo.gi");
    static string GameInfoText(string gameDir) => File.Exists(GameInfoPath(gameDir)) ? File.ReadAllText(GameInfoPath(gameDir)) : "";
    static bool HasAddonsPath(string text) => Regex.IsMatch(text, @"(?m)^\s*Game\s+citadel/addons\s*$");

    // The addons folder goes in front of the game's own files. Mod and Write keep pointing at citadel: without them
    // the first Game path (now addons) would become the place the game saves its config to.
    static readonly Regex GamePaths = new(@"(?m)^([ \t]+)Game([ \t]+)citadel\r?\n\1Game\2core", RegexOptions.Compiled);
    static readonly Regex AddedLines = new(@"(?m)^[ \t]+Mod[ \t]+citadel\r?\n[ \t]+Write[ \t]+citadel\r?\n[ \t]+Game[ \t]+citadel/addons\r?\n", RegexOptions.Compiled);

    static void EnableAddonsPath(string gameDir)
    {
        string path = GameInfoPath(gameDir);
        string text = File.ReadAllText(path);
        if (HasAddonsPath(text)) return;
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";
        string patched = GamePaths.Replace(text, m =>
        {
            string i = m.Groups[1].Value, gap = m.Groups[2].Value;
            return $"{i}Mod{gap}citadel{nl}{i}Write{gap}citadel{nl}{i}Game{gap}citadel/addons{nl}{m.Value}";
        }, 1);
        if (patched == text) throw new InvalidDataException("не нашёл в gameinfo.gi строки Game citadel / Game core");
        BackupGameInfo(path);
        Files.WriteAtomic(path, patched);
    }

    static void DisableAddonsPath(string gameDir)
    {
        string path = GameInfoPath(gameDir);
        if (!File.Exists(path)) return;
        string text = File.ReadAllText(path);
        string restored = AddedLines.Replace(text, "", 1);
        if (restored == text) return;
        BackupGameInfo(path);
        Files.WriteAtomic(path, restored);
    }

    static void BackupGameInfo(string path)
    {
        try
        {
            Directory.CreateDirectory(BackupDir);
            File.Copy(path, Path.Combine(BackupDir, $"gameinfo_{DateTime.Now:yyyyMMdd_HHmmss}.gi"), true);
            foreach (var old in new DirectoryInfo(BackupDir).GetFiles("gameinfo_*.gi").OrderByDescending(f => f.Name).Skip(10))
                old.Delete();
        }
        catch (Exception e) { Log.Write("gameinfo backup: " + e.Message); }
    }

    // ---- building the pack ----

    /// <summary>The mod's files: the game's base_hud layout with the mod's script added, and the script itself.</summary>
    public static Dictionary<string, byte[]> BuildFiles(string gameDir, HudOptions options)
    {
        using var pak = new Package();
        pak.Read(Path.Combine(Citadel(gameDir), "pak01_dir.vpk"));
        return new Dictionary<string, byte[]>
        {
            [LayoutPath] = PatchLayout(ReadEntry(pak, LayoutPath)),
            [ScriptPath + "_c"] = BuildScript(ReadEntry(pak, TemplateScript), ScriptSource(options)),
        };
    }

    static byte[] ReadEntry(Package pak, string path)
    {
        var entry = pak.FindEntry(path) ?? throw new FileNotFoundException("в файлах игры нет " + path);
        pak.ReadEntry(entry, out byte[] bytes);
        return bytes;
    }

    /// <summary>The embedded script with the options and the event schedule filled in.</summary>
    public static string ScriptSource(HudOptions options)
    {
        string script = ReadEmbedded("dlmeta_hud.js");
        using var timers = JsonDocument.Parse(ReadEmbedded("timers.json"));
        var config = new Dictionary<string, object>
        {
            ["timers"] = options.Timers,
            ["reload"] = options.Reload,
            ["zipline"] = options.Zipline,
            ["cleanMap"] = options.CleanMap,
            ["mapOpacity"] = Math.Clamp(options.MapOpacity, 20, 100) / 100.0,
            // Larger only: below the game's own size the map icons get hard to read.
            ["mapScale"] = Math.Clamp(options.MapScale, 100, 130) / 100.0,
            ["schedule"] = timers.RootElement,
        };
        const string slot = "/*CONFIG*/{}";
        if (!script.Contains(slot)) throw new InvalidDataException("в скрипте HUD нет места для настроек");
        return script.Replace(slot, JsonSerializer.Serialize(config));
    }

    static string ReadEmbedded(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException("embedded " + name);
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>Adds the mod's script to the layout's scripts section (made if the layout has none).</summary>
    static byte[] PatchLayout(byte[] original)
    {
        using var res = new Resource();
        res.Read(new MemoryStream(original));
        var laco = (BinaryKV3)(res.GetBlockByType(BlockType.LaCo) ?? throw new InvalidDataException("в base_hud нет LaCo"));
        var doc = laco.Data;
        var root = doc.Root["m_AST"]["m_pRoot"];
        var children = root["vecChildren"];

        var include = Node("INCLUDE");
        include.Add("child", Node("REFERENCE_COMPILED", ScriptPath));
        include.Add("sourceLineColumn", KVObject.Array());

        var rebuilt = KVObject.Array();
        bool added = false;
        foreach (var child in children.AsArraySpan())
        {
            if ((string)child["eType"] == "SCRIPTS")
            {
                AddToSection(child, include);
                added = true;
            }
            rebuilt.Add(child);
        }
        if (!added)
        {
            // Scripts go right after the styles, where the layout compiler puts them.
            var scripts = Node("SCRIPTS");
            scripts.Add("vecChildren", KVObject.Array([include]));
            scripts.Add("sourceLineColumn", KVObject.Array());
            rebuilt = KVObject.Array();
            int i = 0;
            foreach (var child in children.AsArraySpan())
            {
                rebuilt.Add(child);
                if (i++ == 0) rebuilt.Add(scripts);
            }
        }
        root.Remove("vecChildren");
        root.Add("vecChildren", rebuilt);

        var blocks = new List<(string, byte[])>();
        foreach (var block in res.Blocks)
            blocks.Add((block.Type.ToString(), block.Type == BlockType.LaCo ? SerializeKV3(doc, res) : Slice(original, block)));
        return ResourceFile(res.Version, blocks);
    }

    // A section with one entry keeps it under "child"; more go into "vecChildren".
    static void AddToSection(KVObject section, KVObject include)
    {
        if (section.ContainsKey("vecChildren"))
        {
            var list = KVObject.Array();
            foreach (var c in section["vecChildren"].AsArraySpan()) list.Add(c);
            list.Add(include);
            section.Remove("vecChildren");
            section.Add("vecChildren", list);
        }
        else if (section.ContainsKey("child"))
        {
            var only = section["child"];
            section.Remove("child");
            section.Add("vecChildren", KVObject.Array([only, include]));
        }
        else section.Add("vecChildren", KVObject.Array([include]));
    }

    static KVObject Node(string type, string? name = null)
    {
        var o = KVObject.Collection();
        o.Add("eType", new KVObject(type));
        if (name != null) o.Add("name", new KVObject(name));
        return o;
    }

    /// <summary>
    /// A compiled script in the game's own format: its edit info, the JavaScript as plain text, and the stats block
    /// with no exported methods.
    /// </summary>
    static byte[] BuildScript(byte[] template, string source)
    {
        using var res = new Resource();
        res.Read(new MemoryStream(template));
        var blocks = new List<(string, byte[])>();
        foreach (var block in res.Blocks)
        {
            if (block.Type == BlockType.DATA) blocks.Add(("DATA", Encoding.UTF8.GetBytes(source)));
            else if (block.Type == BlockType.RED2) blocks.Add(("RED2", Slice(template, block)));
            else if (block is BinaryKV3 stat && block.Type.ToString() == "STAT")
            {
                var root = KVObject.Collection();
                root.Add("publicMethods", KVObject.Array());
                blocks.Add(("STAT", SerializeKV3(new KVDocument(stat.Data.Header, null, root), res)));
            }
        }
        return ResourceFile(res.Version, blocks);
    }

    static byte[] SerializeKV3(KVDocument doc, Resource owner)
    {
        var kv3 = new BinaryKV3(doc.Root, doc.Header!.Format, BlockType.LaCo) { Resource = owner };
        var ms = new MemoryStream();
        kv3.Serialize(ms);
        return ms.ToArray();
    }

    static byte[] Slice(byte[] file, Block block) => file.AsSpan((int)block.Offset, (int)block.Size).ToArray();

    /// <summary>
    /// A Source 2 resource container: size, header version 12, the resource version, then a table of blocks whose
    /// offsets count from the offset field itself; blocks start on 16-byte boundaries.
    /// </summary>
    static byte[] ResourceFile(ushort version, List<(string Type, byte[] Data)> blocks)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(0u);
        w.Write((ushort)12);
        w.Write(version);
        w.Write(8u);
        w.Write((uint)blocks.Count);
        long table = ms.Position;
        foreach (var (type, _) in blocks)
        {
            w.Write(Encoding.ASCII.GetBytes(type.PadRight(4)[..4]));
            w.Write(0u);
            w.Write(0u);
        }
        for (int i = 0; i < blocks.Count; i++)
        {
            while (ms.Position % 16 != 0) w.Write((byte)0);
            long start = ms.Position;
            w.Write(blocks[i].Data);
            long end = ms.Position;
            long field = table + i * 12 + 4;
            ms.Position = field;
            w.Write((uint)(start - field));
            w.Write((uint)blocks[i].Data.Length);
            ms.Position = end;
        }
        ms.Position = 0;
        w.Write((uint)ms.Length);
        w.Flush();
        return ms.ToArray();
    }
}
