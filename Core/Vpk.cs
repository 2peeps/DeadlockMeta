using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace DeadlockMeta.Core;

/// <summary>
/// Writes a version 2 VPK that holds all of its files itself (a single pakNN_dir.vpk, like the ones the game loads from
/// citadel\addons). The checksum section is filled in the way Valve's packer does, so the file verifies cleanly.
/// </summary>
public static class Vpk
{
    const uint Signature = 0x55AA1234;
    const ushort InDirFile = 0x7FFF;

    /// <param name="files">Paths inside the pack (forward slashes, e.g. panorama/layout/base_hud.vxml_c) and their bytes.</param>
    public static byte[] Build(IReadOnlyDictionary<string, byte[]> files)
    {
        // The tree is grouped by extension, then by folder, as the reader expects.
        var groups = files
            .Select(kv => (Path: kv.Key.Replace('\\', '/').ToLowerInvariant(), Data: kv.Value))
            .Select(f =>
            {
                int slash = f.Path.LastIndexOf('/');
                string dir = slash < 0 ? " " : f.Path[..slash];
                string file = slash < 0 ? f.Path : f.Path[(slash + 1)..];
                int dot = file.LastIndexOf('.');
                string ext = dot < 0 ? " " : file[(dot + 1)..];
                string name = dot < 0 ? file : file[..dot];
                return (Ext: ext, Dir: dir, Name: name, f.Data);
            })
            .GroupBy(f => f.Ext)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        var tree = new MemoryStream();
        var data = new MemoryStream();
        var tw = new BinaryWriter(tree);
        foreach (var ext in groups)
        {
            WriteString(tw, ext.Key);
            foreach (var dir in ext.GroupBy(f => f.Dir).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                WriteString(tw, dir.Key);
                foreach (var f in dir.OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    WriteString(tw, f.Name);
                    tw.Write(Crc32.HashToUInt32(f.Data));
                    tw.Write((ushort)0); // no preload bytes
                    tw.Write(InDirFile);
                    tw.Write((uint)data.Length);
                    tw.Write((uint)f.Data.Length);
                    tw.Write((ushort)0xFFFF);
                    data.Write(f.Data);
                }
                tw.Write((byte)0);
            }
            tw.Write((byte)0);
        }
        tw.Write((byte)0);
        tw.Flush();

        byte[] treeBytes = tree.ToArray();
        byte[] dataBytes = data.ToArray();
        var output = new MemoryStream();
        var w = new BinaryWriter(output);
        w.Write(Signature);
        w.Write(2u);
        w.Write((uint)treeBytes.Length);
        w.Write((uint)dataBytes.Length);
        w.Write(0u);  // archive MD5 section: no external archives
        w.Write(48u); // other MD5 section: tree, archive section and whole file checksums
        w.Write(0u);  // unsigned
        w.Write(treeBytes);
        w.Write(dataBytes);
        w.Write(MD5.HashData(treeBytes));
        w.Write(MD5.HashData(Array.Empty<byte>()));
        w.Flush();
        // The whole-file checksum covers everything before it, including the two checksums just written.
        w.Write(MD5.HashData(output.ToArray()));
        w.Flush();
        return output.ToArray();
    }

    static void WriteString(BinaryWriter w, string s)
    {
        w.Write(Encoding.UTF8.GetBytes(s));
        w.Write((byte)0);
    }
}
