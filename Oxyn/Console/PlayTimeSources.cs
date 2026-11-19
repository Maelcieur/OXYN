using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Oxyn.Infrastructure;

namespace Oxyn.ConsoleMode;

/// <summary>
/// Vrais temps de jeu lus sur le PC, sans connexion :
/// - Steam : userdata\&lt;compte&gt;\config\localconfig.vdf (« Playtime » en minutes, par jeu ; comptes additionnés) ;
/// - émulateurs Switch de la famille yuzu (Lichii, Eden, Citron, Sudachi...) : play_time\playtime.bin
///   (paires identifiant 64 bits + secondes) ; Ryujinx : games\&lt;id&gt;\gui\metadata.json (timespan_played).
/// Les temps des différents émulateurs sont additionnés (un dossier relié à un autre n'est compté qu'une fois).
/// </summary>
public static class PlayTimeSources
{
    private static readonly string[] YuzuFamily = { "lichii", "eden", "citron", "yuzu", "sudachi", "torzu", "suyu" };
    private static readonly Regex TitleIdRx = new(@"(?<![0-9A-Fa-f])(0[14]00[0-9A-Fa-f]{12})(?![0-9A-Fa-f])", RegexOptions.Compiled);

    /// <summary>Remplit <see cref="ConsoleGame.ExternalPlayTime"/> pour chaque jeu dont la source est connue.</summary>
    public static void Apply(IEnumerable<ConsoleGame> games)
    {
        Dictionary<string, TimeSpan>? steam = null, switchTimes = null;
        foreach (var g in games)
        {
            g.ExternalPlayTime = null;
            try
            {
                if (g.UsesEmulator)
                {
                    if (TitleIdOf(g) is not { } id) continue;
                    switchTimes ??= SwitchPlayTimes();
                    if (switchTimes.TryGetValue(id, out var t)) g.ExternalPlayTime = t;
                }
                else if (g.EffectiveSteamAppId is { } app)
                {
                    steam ??= SteamPlayTimes();
                    if (steam.TryGetValue(app, out var t)) g.ExternalPlayTime = t;
                }
            }
            catch (Exception ex)
            {
                Log.WarnOnce("playtime-" + g.Id, $"Temps de jeu de « {g.Title} » illisible", ex);
            }
        }
    }

    /// <summary>Identifiant Switch du jeu : enregistré, sinon trouvé dans le chemin du fichier ([0100...] / [0400...]).</summary>
    public static string? TitleIdOf(ConsoleGame g)
    {
        if (!string.IsNullOrWhiteSpace(g.SwitchTitleId)) return g.SwitchTitleId.ToUpperInvariant();
        var m = TitleIdRx.Match(g.RomPath ?? "");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    /// <summary>
    /// Après une partie : l'émulateur note dans cache\launched.json l'heure de lancement de chaque jeu ;
    /// le jeu lancé depuis <paramref name="start"/> donne l'identifiant (fichiers sans identifiant dans le nom).
    /// </summary>
    public static string? LearnTitleId(DateTime start)
    {
        var since = new DateTimeOffset(start).ToUnixTimeSeconds() - 30;
        string? best = null;
        long bestTime = 0;
        foreach (var dir in YuzuDirs())
        {
            var file = Path.Combine(dir, "cache", "launched.json");
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var t) && t >= since && t > bestTime)
                    {
                        best = p.Name.ToUpperInvariant();
                        bestTime = t;
                    }
            }
            catch (Exception ex) { Log.WarnOnce("launched-" + dir, "launched.json illisible", ex); }
        }
        return best;
    }

    // ---------- Émulateurs ----------

    /// <summary>Temps par identifiant Switch (majuscules), tous émulateurs additionnés.</summary>
    public static Dictionary<string, TimeSpan> SwitchPlayTimes()
    {
        var total = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in YuzuDirs())
        {
            var file = Path.Combine(dir, "play_time", "playtime.bin");
            if (!File.Exists(file)) continue;
            foreach (var (id, t) in ParsePlaytimeBin(File.ReadAllBytes(file))) Add(total, id, t);
        }
        var ryu = Path.Combine(Roaming, "Ryujinx", "games");
        if (Directory.Exists(ryu))
            foreach (var game in Directory.GetDirectories(ryu))
            {
                var meta = Path.Combine(game, "gui", "metadata.json");
                if (!File.Exists(meta)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(meta));
                    if (doc.RootElement.TryGetProperty("timespan_played", out var tp) && TimeSpan.TryParse(tp.GetString(), out var t))
                        Add(total, Path.GetFileName(game), t);
                }
                catch { }
            }
        return total;
    }

    /// <summary>playtime.bin : paires (identifiant 64 bits, secondes 64 bits) en petit-boutiste.</summary>
    public static IEnumerable<(string Id, TimeSpan Time)> ParsePlaytimeBin(byte[] b)
    {
        for (var i = 0; i + 16 <= b.Length; i += 16)
        {
            var id = BitConverter.ToUInt64(b, i);
            var s = BitConverter.ToUInt64(b, i + 8);
            if (id == 0 || s > 100L * 365 * 24 * 3600) continue;
            yield return (id.ToString("X16"), TimeSpan.FromSeconds(s));
        }
    }

    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Dossiers de données des émulateurs de la famille yuzu, sans doublon (eden relié à citron...).</summary>
    internal static IEnumerable<string> YuzuDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in YuzuFamily)
        {
            var dir = Path.Combine(Roaming, name);
            if (!Directory.Exists(dir)) continue;
            string real;
            try { real = new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName ?? dir; }
            catch { real = dir; }
            if (seen.Add(Path.TrimEndingDirectorySeparator(real))) yield return real;   // vrai dossier : écrire à travers la jonction eden -> citron est refusé par Windows
        }
    }

    private static void Add(Dictionary<string, TimeSpan> d, string id, TimeSpan t) =>
        d[id] = d.TryGetValue(id, out var x) ? x + t : t;

    // ---------- Steam ----------

    /// <summary>Temps par identifiant de jeu Steam, tous les comptes du PC additionnés.</summary>
    public static Dictionary<string, TimeSpan> SteamPlayTimes()
    {
        var total = new Dictionary<string, TimeSpan>();
        var steam = SteamLocator.SteamRoot();
        var users = steam == null ? null : Path.Combine(steam, "userdata");
        if (users == null || !Directory.Exists(users)) return total;
        foreach (var user in Directory.GetDirectories(users))
        {
            var cfg = Path.Combine(user, "config", "localconfig.vdf");
            if (!File.Exists(cfg)) continue;
            try
            {
                foreach (var (app, t) in ParseSteamPlaytimes(File.ReadAllText(cfg))) Add(total, app, t);
            }
            catch (Exception ex) { Log.WarnOnce("steam-playtime-" + user, "localconfig.vdf illisible", ex); }
        }
        return total;
    }

    /// <summary>Dans localconfig.vdf : ...Software/Valve/Steam/apps/&lt;id&gt;/"Playtime" (minutes).</summary>
    public static IEnumerable<(string App, TimeSpan Time)> ParseSteamPlaytimes(string vdf)
    {
        var root = Vdf.Parse(vdf);
        var apps = Vdf.Find(root, "Software", "Valve", "Steam", "apps") ?? Vdf.Find(root, "UserLocalConfigStore", "Software", "Valve", "Steam", "apps");
        if (apps == null) yield break;
        foreach (var (key, value) in apps)
            if (value is Dictionary<string, object> app && app.TryGetValue("Playtime", out var p) && p is string s && long.TryParse(s, out var min) && min > 0)
                yield return (key, TimeSpan.FromMinutes(min));
    }

    /// <summary>Lecteur VDF (texte Valve) minimal : "clé" "valeur" et "clé" { ... }, clés insensibles à la casse.</summary>
    public static class Vdf
    {
        public static Dictionary<string, object> Parse(string text)
        {
            var i = 0;
            return Block(text, ref i);
        }

        public static Dictionary<string, object>? Find(Dictionary<string, object> node, params string[] path)
        {
            var cur = node;
            foreach (var key in path)
            {
                if (!cur.TryGetValue(key, out var next) || next is not Dictionary<string, object> d) return null;
                cur = d;
            }
            return cur;
        }

        private static Dictionary<string, object> Block(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var key = Token(s, ref i);
                if (key == null || key == "}") return d;
                var value = Token(s, ref i);
                if (value == null) return d;
                d[key] = value == "{" ? Block(s, ref i) : value;
            }
        }

        private static string? Token(string s, ref int i)
        {
            while (i < s.Length)
            {
                var c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c is '{' or '}') { i++; return c.ToString(); }
                if (c == '"')
                {
                    var sb = new System.Text.StringBuilder();
                    i++;
                    while (i < s.Length && s[i] != '"')
                    {
                        if (s[i] == '\\' && i + 1 < s.Length) { i++; sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] }); }
                        else sb.Append(s[i]);
                        i++;
                    }
                    i++;
                    return sb.ToString();
                }
                var start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not '{' and not '}') i++;
                return s[start..i];
            }
            return null;
        }
    }
}
