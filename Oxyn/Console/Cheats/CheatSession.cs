using System.IO;
using System.Text.Json;
using Oxyn.Infrastructure;

namespace Oxyn.ConsoleMode.Cheats;

/// <summary>
/// Position d'une valeur, retrouvable au lancement suivant : décalage dans l'allocation de l'émulateur qui a la même
/// taille (la mémoire de la console émulée), l'adresse brute changeant à chaque lancement.
/// </summary>
/// <paramref name="Fingerprint"/> : empreinte en hexadécimal (16 octets avant + 16 après), voir <see cref="CheatFingerprint"/>.
public sealed record CheatLocation(long AllocationSize, long Offset, CheatType Type, string? Fingerprint = null);

/// <summary>Valeur nommée d'un jeu (« Bombes »...) enregistrée dans console-cheats.json.</summary>
public sealed class SavedCheat
{
    public string Name { get; set; } = "";
    public List<CheatLocation> Locations { get; set; } = new();
}

/// <summary>console-cheats.json : valeurs trouvées, par identifiant de jeu.</summary>
public sealed class CheatStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _file;
    private Dictionary<string, List<SavedCheat>> _games = new();

    public CheatStore(string? file = null)
    {
        _file = file ?? Path.Combine(AppPaths.DataDir, "console-cheats.json");
        try
        {
            if (File.Exists(_file)) _games = JsonSerializer.Deserialize<Dictionary<string, List<SavedCheat>>>(File.ReadAllText(_file)) ?? new();
        }
        catch (Exception ex)
        {
            Log.Warn("Triches du Mode console illisibles", ex);
            JsonFile.KeepCorruptCopy(_file);
            _games = new();
        }
    }

    public List<SavedCheat> For(string gameId) => _games.TryGetValue(gameId, out var l) ? l : _games[gameId] = new();

    public void Save()
    {
        try
        {
            // Les jeux sans valeur ne sont pas écrits, mais restent dans _games : une session garde la référence de
            // sa liste (For) et y ajoutera les prochaines valeurs (sinon elles seraient perdues à l'enregistrement).
            var toWrite = _games.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(toWrite, Json));
            File.Move(tmp, _file, true);
        }
        catch (Exception ex)
        {
            Log.Warn("Enregistrement des triches impossible", ex);
        }
    }
}

/// <summary>Valeur enregistrée retrouvée dans l'émulateur en cours (adresses de ce lancement).</summary>
public sealed class LiveCheat
{
    public LiveCheat(SavedCheat saved, List<(long Addr, CheatType Type)> addresses) { Saved = saved; Addresses = addresses; }
    public SavedCheat Saved { get; }
    public List<(long Addr, CheatType Type)> Addresses { get; }
    public string Name => Saved.Name;
    public bool Found => Addresses.Count > 0;
    public bool Frozen { get; set; }
    public double FrozenValue { get; set; }
}

/// <summary>
/// Triches d'un jeu lancé dans un émulateur : valeurs enregistrées (lues / modifiées / bloquées en direct)
/// et recherche en cours. Une valeur bloquée est réécrite 10 fois par seconde tant que le jeu tourne.
/// </summary>
public sealed class CheatSession : IDisposable
{
    public static readonly string[] Names =
    {
        "Vies", "Cœurs / santé", "Bombes", "Flèches", "Rubis / argent", "Munitions", "Objets", "Clés",
        "Magie / énergie", "Score", "Niveau", "Expérience", "Temps", "Autre valeur",
    };

    private readonly CheatStore _store;
    private readonly ProcessMemory _mem;
    private readonly List<SavedCheat> _saved;
    private readonly System.Threading.Timer _freeze;
    private readonly object _gate = new();
    private Dictionary<long, List<long>> _allocBySize = new();

    public string GameId { get; }
    public int Pid => _mem.Pid;
    public List<LiveCheat> Cheats { get; } = new();
    public CheatSearch? Search { get; private set; }
    public long LastSearchValue { get; private set; }

    public CheatSession(int pid, string gameId, CheatStore? store = null)
    {
        GameId = gameId;
        _store = store ?? new CheatStore();
        _mem = new ProcessMemory(pid);
        _saved = _store.For(gameId);
        try { Resolve(); }
        catch { _mem.Dispose(); throw; }   // sinon le handle du processus resterait ouvert
        _freeze = new System.Threading.Timer(_ => WriteFrozen(), null, 100, 100);
    }

    /// <summary>Retrouve les valeurs enregistrées dans la mémoire de ce lancement.</summary>
    public void Resolve()
    {
        lock (_gate)
        {
            _allocBySize = ProcessMemory.AllocationSizes(_mem.Regions())
                .GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).OrderBy(b => b).ToList());
            Cheats.Clear();
            var dirty = false;
            foreach (var s in _saved)
            {
                var addrs = new List<(long, CheatType)>();
                var legacy = new List<(long Addr, int Index)>();
                for (var i = 0; i < s.Locations.Count; i++)
                {
                    var loc = s.Locations[i];
                    // Seulement une allocation de taille UNIQUE (la mémoire de la console émulée) : avec plusieurs
                    // candidates, on écrirait peut-être au mauvais endroit et le jeu planterait.
                    if (!_allocBySize.TryGetValue(loc.AllocationSize, out var bases) || bases.Count != 1) continue;
                    var addr = bases[0] + loc.Offset;
                    var saved = CheatFingerprint.FromHex(loc.Fingerprint);
                    if (saved == null) { legacy.Add((addr, i)); continue; }
                    // L'empreinte doit ressembler : sinon la valeur n'est plus là (le jeu l'a rangée ailleurs).
                    if (CheatFingerprint.Read(_mem, addr, CheatValue.Size(loc.Type)) is { } now && CheatFingerprint.Matches(saved, now))
                        addrs.Add((addr, loc.Type));
                }
                // Anciennes valeurs (sans empreinte) : gardées seulement si au moins 2 copies sont d'accord sur la
                // valeur ; on leur ajoute alors une empreinte.
                if (legacy.Count >= 2)
                {
                    var values = legacy.Select(l => ReadAt(l.Addr, s.Locations[l.Index].Type)).ToList();
                    if (values.All(v => v != null && v == values[0]))
                        foreach (var (addr, index) in legacy)
                        {
                            var loc = s.Locations[index];
                            addrs.Add((addr, loc.Type));
                            if (CheatFingerprint.Read(_mem, addr, CheatValue.Size(loc.Type)) is { } fp)
                            {
                                s.Locations[index] = loc with { Fingerprint = CheatFingerprint.ToHex(fp) };
                                dirty = true;
                            }
                        }
                }
                if (addrs.Count == 0 && s.Locations.Count > 0)
                    Log.Info($"Triches : « {s.Name} » n'est plus à sa place dans ce lancement.");
                Cheats.Add(new LiveCheat(s, addrs));
            }
            if (dirty) _store.Save();
        }
    }

    private double? ReadAt(long addr, CheatType type)
    {
        var b = new byte[CheatValue.Size(type)];
        return _mem.Read(addr, b) ? CheatValue.Read(b, type) : null;
    }

    // ---------- Valeurs déplacées : recherche par empreinte ----------

    private bool _relocated;

    /// <summary>Des valeurs enregistrées (avec empreinte) n'ont pas été retrouvées à leur place : à rechercher.</summary>
    public bool NeedsRelocation
    {
        get
        {
            lock (_gate)
                return !_relocated && Cheats.Any(c => !c.Found && c.Saved.Locations.Any(l => CheatFingerprint.FromHex(l.Fingerprint) != null));
        }
    }

    /// <summary>
    /// Cherche l'empreinte des valeurs introuvables dans toute la mémoire de l'émulateur (lent : quelques secondes)
    /// et met à jour leurs positions enregistrées.
    /// </summary>
    public void Relocate(Action<double>? progress, CancellationToken ct)
    {
        _relocated = true;
        var anchors = new List<(LiveCheat Cheat, CheatType Type, byte[] Fp, byte[] Anchor, int Delta)>();
        lock (_gate)
        {
            foreach (var c in Cheats.Where(c => !c.Found))
                foreach (var loc in c.Saved.Locations)
                    if (CheatFingerprint.FromHex(loc.Fingerprint) is { } fp)
                        foreach (var (bytes, delta) in CheatFingerprint.Anchors(fp, CheatValue.Size(loc.Type)))
                            anchors.Add((c, loc.Type, fp, bytes, delta));
        }
        if (anchors.Count == 0) return;
        var found = new Dictionary<LiveCheat, HashSet<(long, CheatType)>>();
        var regions = _mem.Regions();
        var total = Math.Max(1, regions.Sum(r => r.Size));
        long done = 0;
        const int chunk = 4 << 20, overlap = 64;
        var buf = new byte[chunk + overlap];
        foreach (var r in regions)
        {
            for (long a = r.Base; a < r.End; a += chunk)
            {
                ct.ThrowIfCancellationRequested();
                var len = (int)Math.Min(chunk + overlap, r.End - a);
                if (_mem.Read(a, buf, len))
                {
                    foreach (var (cheat, type, fp, anchor, delta) in anchors)
                        FindAnchor(buf.AsSpan(0, len), a, cheat, type, fp, anchor, delta, found);
                }
                done += Math.Min(chunk, r.End - a);
                progress?.Invoke(done / (double)total);
            }
        }
        lock (_gate)
        {
            foreach (var (cheat, set) in found)
            {
                if (set.Count == 0) continue;
                cheat.Addresses.AddRange(set.OrderBy(x => x.Item1));
                cheat.Saved.Locations = Locate(cheat.Addresses, regions);
                Log.Info($"Triches : « {cheat.Name} » retrouvé par son empreinte ({set.Count} adresse(s)).");
            }
        }
        if (found.Count > 0) _store.Save();
    }

    private void FindAnchor(ReadOnlySpan<byte> span, long baseAddr, LiveCheat cheat, CheatType type, byte[] fp, byte[] anchor, int delta,
        Dictionary<LiveCheat, HashSet<(long, CheatType)>> found)
    {
        var size = CheatValue.Size(type);
        var i = 0;
        while (i < span.Length)
        {
            var k = span[i..].IndexOf(anchor);
            if (k < 0) break;
            i += k;
            var addr = baseAddr + i - delta;
            i++;
            if (addr % size != 0) continue;
            if (!found.TryGetValue(cheat, out var set)) found[cheat] = set = new();
            if (set.Count >= MaxSaved || set.Contains((addr, type))) continue;
            if (CheatFingerprint.Read(_mem, addr, size) is { } now && CheatFingerprint.Matches(fp, now)) set.Add((addr, type));
        }
    }

    /// <summary>Positions enregistrables (allocation, décalage, empreinte) des adresses de ce lancement.</summary>
    private List<CheatLocation> Locate(IEnumerable<(long Addr, CheatType Type)> addresses, List<MemRegion> regions)
    {
        var list = new List<CheatLocation>();
        foreach (var (addr, type) in addresses)
        {
            var region = regions.FirstOrDefault(r => addr >= r.Base && addr < r.End);
            if (region.Size == 0) continue;
            var allocSize = regions.Where(r => r.AllocationBase == region.AllocationBase).Max(r => r.End) - region.AllocationBase;
            var fp = CheatFingerprint.Read(_mem, addr, CheatValue.Size(type));
            list.Add(new CheatLocation(allocSize, addr - region.AllocationBase, type, fp != null ? CheatFingerprint.ToHex(fp) : null));
        }
        return list;
    }

    /// <summary>Valeur actuelle (première adresse lisible), null si introuvable.</summary>
    public double? Value(LiveCheat c)
    {
        foreach (var (addr, type) in c.Addresses)
        {
            var b = new byte[CheatValue.Size(type)];
            if (_mem.Read(addr, b)) return CheatValue.Read(b, type);
        }
        return null;
    }

    public bool Set(LiveCheat c, double value)
    {
        var ok = false;
        foreach (var (addr, type) in c.Addresses) ok |= _mem.Write(addr, CheatValue.Encode(value, type));
        if (c.Frozen) c.FrozenValue = value;
        return ok;
    }

    public void ToggleFreeze(LiveCheat c)
    {
        if (!c.Frozen && Value(c) is { } v) { c.FrozenValue = v; c.Frozen = true; }
        else c.Frozen = false;
    }

    public void Remove(LiveCheat c)
    {
        lock (_gate)
        {
            Cheats.Remove(c);
            _saved.Remove(c.Saved);
        }
        _store.Save();
    }

    // ---------- Recherche ----------

    public void StartSearch(long value, Action<double>? progress, CancellationToken ct)
    {
        CancelSearch();
        Search = CheatSearch.First(_mem, value, progress, ct);
        LastSearchValue = value;
        Log.Info($"Triches : 1re recherche de {value}, {Search.Count} résultats.");
    }

    public void RefineSearch(long value, Action<double>? progress, CancellationToken ct)
    {
        if (Search == null) return;
        Search.Next(_mem, value, progress, ct);
        LastSearchValue = value;
        Log.Info($"Triches : recherche affinée ({value}), {Search.Count} résultats.");
    }

    public void CancelSearch()
    {
        Search?.Dispose();
        Search = null;
    }

    /// <summary>Adresses enregistrées au plus pour une valeur (l'émulateur et le jeu en gardent plusieurs copies).</summary>
    public const int MaxSaved = 64;

    /// <summary>
    /// Nombre d'adresses différentes parmi les résultats (une même case compte comme octet, 16 et 32 bits) ;
    /// int.MaxValue s'il y en a trop pour être enregistrées.
    /// </summary>
    public int DistinctResults()
    {
        if (Search == null || Search.Count == 0) return 0;
        if (Search.Count > MaxSaved * 4) return int.MaxValue;
        var n = CheatSearch.Simplify(Search.Results(MaxSaved * 4)).Count;
        return n <= MaxSaved ? n : int.MaxValue;
    }

    /// <summary>Enregistre les résultats restants (64 adresses au plus) sous ce nom et termine la recherche.</summary>
    public LiveCheat? SaveSearch(string name)
    {
        if (Search == null || Search.Count == 0) return null;
        var results = CheatSearch.Simplify(Search.Results(MaxSaved * 4)).Take(MaxSaved).ToList();
        var saved = new SavedCheat { Name = UniqueName(name), Locations = Locate(results, _mem.Regions()) };
        CancelSearch();
        if (saved.Locations.Count == 0) return null;
        LiveCheat live;
        lock (_gate)
        {
            _saved.Add(saved);
            live = new LiveCheat(saved, results);
            Cheats.Add(live);
        }
        _store.Save();
        Log.Info($"Triches : « {saved.Name} » enregistré ({results.Count} adresse(s)).");
        return live;
    }

    private string UniqueName(string name)
    {
        var n = name;
        for (var i = 2; _saved.Any(s => s.Name == n); i++) n = $"{name} {i}";
        return n;
    }

    private void WriteFrozen()
    {
        try
        {
            List<LiveCheat> frozen;
            lock (_gate) frozen = Cheats.Where(c => c.Frozen).ToList();
            foreach (var c in frozen)
                foreach (var (addr, type) in c.Addresses) _mem.Write(addr, CheatValue.Encode(c.FrozenValue, type));
        }
        catch { }
    }

    public void Dispose()
    {
        _freeze.Dispose();
        CancelSearch();
        _mem.Dispose();
    }
}
