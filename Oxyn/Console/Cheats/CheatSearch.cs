using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace Oxyn.ConsoleMode.Cheats;

/// <summary>Taille / format d'une valeur de jeu en mémoire (petit-boutiste, alignée).</summary>
public enum CheatType : byte { Byte = 1, Int16 = 2, Int32 = 4, Float = 5 }

/// <summary>Lecture, écriture et recherche des valeurs (pur, testé sans processus).</summary>
public static class CheatValue
{
    public static int Size(CheatType t) => t switch { CheatType.Byte => 1, CheatType.Int16 => 2, _ => 4 };

    public static double Read(ReadOnlySpan<byte> b, CheatType t) => t switch
    {
        // (double) partout : sinon le type commun de l'expression serait float et un grand entier perdrait sa précision.
        CheatType.Byte => (double)b[0],
        CheatType.Int16 => (double)BinaryPrimitives.ReadInt16LittleEndian(b),
        CheatType.Int32 => (double)BinaryPrimitives.ReadInt32LittleEndian(b),
        _ => (double)BinaryPrimitives.ReadSingleLittleEndian(b),
    };

    public static byte[] Encode(double v, CheatType t)
    {
        var b = new byte[Size(t)];
        switch (t)
        {
            case CheatType.Byte: b[0] = (byte)Math.Clamp(Math.Round(v), 0, 255); break;
            case CheatType.Int16: BinaryPrimitives.WriteInt16LittleEndian(b, (short)Math.Clamp(Math.Round(v), short.MinValue, short.MaxValue)); break;
            case CheatType.Int32: BinaryPrimitives.WriteInt32LittleEndian(b, (int)Math.Clamp(Math.Round(v), int.MinValue, int.MaxValue)); break;
            default: BinaryPrimitives.WriteSingleLittleEndian(b, (float)v); break;
        }
        return b;
    }

    /// <summary>Valeur entière ou décimale lisible (3, 2.5).</summary>
    public static string Format(double v) => Math.Abs(v - Math.Round(v)) < 0.0001 ? Math.Round(v).ToString("0") : v.ToString("0.##");

    /// <summary>
    /// Ajoute les positions de <paramref name="buf"/> (qui commence à l'adresse alignée <paramref name="baseAddr"/>)
    /// où l'on trouve la valeur entière <paramref name="value"/> : entier 16 / 32 bits, nombre décimal.
    /// Pas les octets seuls : un petit nombre se trouve des centaines de millions de fois en mémoire (recherche de
    /// plusieurs minutes), et les jeux Switch rangent leurs compteurs en 16 / 32 bits.
    /// </summary>
    public static void Scan(ReadOnlySpan<byte> buf, long baseAddr, long value, List<(long Addr, CheatType Type)> hits)
    {
        if (value is >= short.MinValue and <= ushort.MaxValue)
            Find(MemoryMarshal.Cast<byte, short>(buf[..(buf.Length & ~1)]), unchecked((short)value), baseAddr, 2, CheatType.Int16, hits);
        if (value is >= int.MinValue and <= int.MaxValue)
            Find(MemoryMarshal.Cast<byte, int>(buf[..(buf.Length & ~3)]), (int)value, baseAddr, 4, CheatType.Int32, hits);
        Find(MemoryMarshal.Cast<byte, float>(buf[..(buf.Length & ~3)]), (float)value, baseAddr, 4, CheatType.Float, hits);
    }

    private static void Find<T>(ReadOnlySpan<T> s, T v, long baseAddr, int size, CheatType type, List<(long, CheatType)> hits) where T : IEquatable<T>
    {
        var i = 0;
        while (i < s.Length)
        {
            var k = s[i..].IndexOf(v);
            if (k < 0) break;
            i += k;
            hits.Add((baseAddr + (long)i * size, type));
            i++;
        }
    }

    /// <summary>La valeur lue correspond-elle à l'entier recherché ?</summary>
    public static bool Matches(double read, long value, CheatType type) =>
        type == CheatType.Float ? (float)read == (float)value : read == value;
}

/// <summary>
/// Recherche de valeur façon Cheat Engine dans la mémoire de l'émulateur : 1re recherche sur toute la mémoire
/// inscriptible, puis on affine avec la nouvelle valeur après l'avoir fait changer dans le jeu.
/// Les résultats (adresse + type, triés par adresse) sont dans un fichier temporaire : il peut y en avoir des millions.
/// </summary>
public sealed class CheatSearch : IDisposable
{
    private const int Chunk = 4 << 20;
    private string _file;

    public long Count { get; private set; }
    public int Steps { get; private set; }

    /// <summary>Nombre de résultats avant le dernier affinage (égal à Count : l'affinage n'élimine plus rien).</summary>
    public long PreviousCount { get; private set; }

    /// <summary>Le dernier affinage n'a rien éliminé : les résultats restants suivent tous la valeur (copies).</summary>
    public bool Stalled => Steps > 1 && Count == PreviousCount;

    private CheatSearch(string file) => _file = file;

    public static CheatSearch First(ProcessMemory mem, long value, Action<double>? progress, CancellationToken ct)
    {
        var search = new CheatSearch(NewFile());
        var regions = mem.Spans();
        var total = Math.Max(1, regions.Sum(r => r.Size));
        long done = 0, count = 0;
        var buf = new byte[Chunk];
        var hits = new List<(long Addr, CheatType Type)>();
        using (var w = new BinaryWriter(new FileStream(search._file, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20)))
        {
            foreach (var r in regions)
            {
                for (long a = r.Base; a < r.End; a += Chunk)
                {
                    ct.ThrowIfCancellationRequested();
                    var len = (int)Math.Min(Chunk, r.End - a);
                    var span = buf.AsSpan(0, len);
                    if (mem.Read(a, buf, len))
                    {
                        hits.Clear();
                        CheatValue.Scan(span, a, value, hits);
                        hits.Sort((x, y) => x.Addr != y.Addr ? x.Addr.CompareTo(y.Addr) : x.Type.CompareTo(y.Type));
                        foreach (var (addr, type) in hits) { w.Write(addr); w.Write((byte)type); }
                        count += hits.Count;
                    }
                    done += len;
                    progress?.Invoke(done / (double)total);
                }
            }
        }
        search.Count = count;
        search.Steps = 1;
        return search;
    }

    /// <summary>Garde les résultats qui valent maintenant <paramref name="value"/>.</summary>
    public void Next(ProcessMemory mem, long value, Action<double>? progress, CancellationToken ct)
    {
        var next = NewFile();
        long kept = 0;
        var buf = new byte[Chunk];
        long winStart = 0, winEnd = 0;
        var winOk = false;
        // Carte des zones lue une seule fois (au lieu d'une question à Windows par fenêtre de lecture).
        var spans = mem.Spans();
        var starts = spans.Select(s => s.Base).ToArray();
        long SpanEnd(long addr)
        {
            var k = Array.BinarySearch(starts, addr);
            if (k < 0) k = ~k - 1;
            return k >= 0 && addr < spans[k].End ? spans[k].End : 0;
        }
        try
        {
            using (var r = new BinaryReader(new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20)))
            using (var w = new BinaryWriter(new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20)))
            {
                for (long i = 0; i < Count; i++)
                {
                    if ((i & 0xFFFF) == 0) { ct.ThrowIfCancellationRequested(); progress?.Invoke(i / (double)Math.Max(1, Count)); }
                    var addr = r.ReadInt64();
                    var type = (CheatType)r.ReadByte();
                    var size = CheatValue.Size(type);
                    if (addr < winStart || addr + size > winEnd)
                    {
                        // Nouvelle fenêtre de lecture à partir de cette adresse (sans déborder de la zone).
                        winStart = addr;
                        var end = SpanEnd(addr);
                        winEnd = end <= addr ? addr + size : Math.Min(addr + Chunk, end);
                        winOk = end > addr && mem.Read(winStart, buf, (int)(winEnd - winStart));
                    }
                    if (!winOk || addr + size > winEnd) continue;
                    var v = CheatValue.Read(buf.AsSpan((int)(addr - winStart), size), type);
                    if (!CheatValue.Matches(v, value, type)) continue;
                    w.Write(addr);
                    w.Write((byte)type);
                    kept++;
                }
            }
        }
        catch
        {
            TryDelete(next);
            throw;
        }
        TryDelete(_file);
        _file = next;
        PreviousCount = Count;
        Count = kept;
        Steps++;
        progress?.Invoke(1);
    }

    /// <summary>Les premiers résultats (adresse, type).</summary>
    public List<(long Addr, CheatType Type)> Results(int max)
    {
        var list = new List<(long, CheatType)>();
        using var r = new BinaryReader(new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read));
        for (long i = 0; i < Count && list.Count < max; i++) list.Add((r.ReadInt64(), (CheatType)r.ReadByte()));
        return list;
    }

    /// <summary>
    /// Une adresse peut correspondre à plusieurs types (5 = octet 5 = entier 5) : on garde le plus petit entier
    /// (écrire 4 octets écraserait la valeur voisine) ; un décimal reste à part.
    /// </summary>
    public static List<(long Addr, CheatType Type)> Simplify(IEnumerable<(long Addr, CheatType Type)> results) =>
        results.GroupBy(x => x.Addr)
            .Select(g => g.Any(x => x.Type != CheatType.Float) ? g.Where(x => x.Type != CheatType.Float).MinBy(x => (int)x.Type) : g.First())
            .ToList();

    public void Dispose() => TryDelete(_file);

    private static string NewFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "OXYN-cheats");
        Directory.CreateDirectory(dir);
        // Restes d'une recherche interrompue (OXYN fermé de force...).
        foreach (var old in Directory.GetFiles(dir, "*.bin"))
            if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddHours(-6)) TryDelete(old);
        return Path.Combine(dir, $"{Guid.NewGuid():N}.bin");
    }

    private static void TryDelete(string f)
    {
        try { File.Delete(f); } catch { }
    }
}
