namespace Oxyn.ConsoleMode.Cheats;

/// <summary>
/// Empreinte d'une valeur : les 16 octets avant et les 16 octets après elle en mémoire (pas la valeur elle-même,
/// qui change). Au lancement suivant, une adresse n'est réutilisée que si son empreinte ressemble assez ;
/// sinon on cherche l'empreinte dans toute la mémoire de l'émulateur (la valeur a pu changer de place).
/// </summary>
public static class CheatFingerprint
{
    public const int Side = 16, Length = Side * 2;

    /// <summary>Octets identiques sur 32 requis (les voisins peuvent être d'autres valeurs du jeu qui bougent).</summary>
    public const int MinSimilar = 26;

    /// <summary>Empreinte lue autour de <paramref name="addr"/>, null si illisible (bord de zone).</summary>
    public static byte[]? Read(ProcessMemory mem, long addr, int size)
    {
        var b = new byte[Length + size];
        return mem.Read(addr - Side, b) ? FromWindow(b, size) : null;
    }

    /// <summary>Empreinte extraite d'une fenêtre « 16 octets + valeur + 16 octets ».</summary>
    public static byte[] FromWindow(ReadOnlySpan<byte> window, int size)
    {
        var r = new byte[Length];
        window[..Side].CopyTo(r);
        window.Slice(Side + size, Side).CopyTo(r.AsSpan(Side));
        return r;
    }

    public static int Similarity(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var n = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] == b[i]) n++;
        return n;
    }

    public static bool Matches(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => a.Length == Length && b.Length == Length && Similarity(a, b) >= MinSimilar;

    /// <summary>Morceau assez varié pour être cherché (pas une suite de zéros, qu'on trouverait partout).</summary>
    public static bool Distinctive(ReadOnlySpan<byte> s)
    {
        Span<bool> seen = stackalloc bool[256];
        var distinct = 0;
        foreach (var x in s) if (!seen[x]) { seen[x] = true; distinct++; }
        return distinct >= 4;
    }

    /// <summary>
    /// Les 4 morceaux de 8 octets de l'empreinte qui servent d'ancre pour la retrouver, avec leur position par rapport
    /// à la valeur : -16, -8 (avant), +taille, +taille+8 (après).
    /// </summary>
    public static IEnumerable<(byte[] Bytes, int Delta)> Anchors(byte[] fp, int size)
    {
        for (var q = 0; q < 4; q++)
        {
            var part = fp.AsSpan(q * 8, 8);
            if (!Distinctive(part)) continue;
            var delta = q switch { 0 => -16, 1 => -8, 2 => size, _ => size + 8 };
            yield return (part.ToArray(), delta);
        }
    }

    public static string ToHex(byte[] b) => Convert.ToHexString(b);

    public static byte[]? FromHex(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        try { var b = Convert.FromHexString(s); return b.Length == Length ? b : null; }
        catch { return null; }
    }
}
