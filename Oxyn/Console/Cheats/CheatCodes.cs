using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Oxyn.Infrastructure;

namespace Oxyn.ConsoleMode.Cheats;

/// <summary>Code de triche au format Atmosphère (« [Nom] » puis lignes hexadécimales). Désactivé par défaut.</summary>
public sealed class CheatCode
{
    public string Name { get; set; } = "";
    /// <summary>Texte complet du code, ligne « [Nom] » comprise.</summary>
    public string Body { get; set; } = "";
    public bool Enabled { get; set; }
    /// <summary>« fichier » (ajouté par l'utilisateur) ou « en ligne » (base switch-cheats-db).</summary>
    public string Source { get; set; } = "fichier";
}

/// <summary>Codes d'un jeu Switch (par identifiant) et version du jeu (build ID) à laquelle ils s'appliquent.</summary>
public sealed class GameCheatCodes
{
    public string? BuildId { get; set; }
    public List<CheatCode> Codes { get; set; } = new();
}

/// <summary>
/// Codes de triche des émulateurs Switch de la famille yuzu (Lichii, Eden, Citron...) :
/// - lus dans des fichiers .txt (format Atmosphère) ou téléchargés depuis la base publique switch-cheats-db ;
/// - enregistrés dans cheat-codes.json (désactivés par défaut) ;
/// - les codes cochés sont installés comme add-ons de l'émulateur : load\&lt;TitleID&gt;\Oxyn - &lt;nom&gt;\cheats\&lt;BuildID&gt;.txt
///   (un dossier par code) ; l'émulateur les applique au lancement suivant du jeu.
/// </summary>
public static class CheatCodes
{
    public const string FolderPrefix = "OXYN - ";
    /// <summary>Préfixe d'avant le renommage (SysDeck) : ces dossiers sont aussi remplacés à la réinstallation des codes.</summary>
    public const string LegacyFolderPrefix = "SysDeck - ";
    private const string DbUrl = "https://raw.githubusercontent.com/HamletDuFromage/switch-cheats-db/master/cheats/{0}.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Regex Header = new(@"^\s*[\[{](.+)[\]}]\s*$", RegexOptions.Compiled);
    private static readonly Regex BuildIdRx = new(@"name=main.*?build_id=([0-9A-Fa-f]{16,64})|build_id=([0-9A-Fa-f]{16,64}).*?name=main", RegexOptions.Compiled);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static string DefaultFile => Path.Combine(AppPaths.DataDir, "cheat-codes.json");

    // ---------- Lecture des fichiers ----------

    /// <summary>
    /// Codes d'un texte Atmosphère : chaque « [Nom] » (ou « {Nom} », code maître) ouvre un code, les lignes suivantes
    /// en sont le contenu. Les codes sans contenu (titres, séparateurs) sont ignorés.
    /// </summary>
    public static List<CheatCode> Parse(string text, string source = "fichier")
    {
        var list = new List<CheatCode>();
        string? name = null;
        var body = new StringBuilder();
        void Flush()
        {
            if (name == null) return;
            var lines = body.ToString().Trim();
            if (lines.Length > 0) list.Add(new CheatCode { Name = name.Trim(), Body = $"[{name.Trim()}]\n{lines}\n", Source = source });
            body.Clear();
        }
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            var m = Header.Match(line);
            if (m.Success) { Flush(); name = m.Groups[1].Value; continue; }
            if (name != null && line.Length > 0) body.Append(line).Append('\n');
        }
        Flush();
        return list;
    }

    /// <summary>Lit un fichier .txt (UTF-8, sinon page de code Windows pour les accents).</summary>
    public static List<CheatCode> ReadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        return Parse(text);
    }

    // ---------- Enregistrement ----------

    public static Dictionary<string, GameCheatCodes> Load(string? file = null)
    {
        file ??= DefaultFile;
        try
        {
            if (File.Exists(file))
                return JsonSerializer.Deserialize<Dictionary<string, GameCheatCodes>>(File.ReadAllText(file)) is { } d
                    ? new Dictionary<string, GameCheatCodes>(d, StringComparer.OrdinalIgnoreCase)
                    : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { Log.Warn("cheat-codes.json illisible", ex); }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    public static void Save(Dictionary<string, GameCheatCodes> all, string? file = null)
    {
        file ??= DefaultFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(all.Where(kv => kv.Value.Codes.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value), Json));
            File.Move(tmp, file, true);
        }
        catch (Exception ex) { Log.Warn("Enregistrement des codes de triche impossible", ex); }
    }

    /// <summary>
    /// Ajoute les codes : un nouveau code est désactivé ; un code de même nom est remplacé mais garde son état
    /// (une nouvelle recherche ne décoche pas ce que le joueur a activé). Renvoie le nombre de codes nouveaux.
    /// </summary>
    public static int Merge(GameCheatCodes game, IEnumerable<CheatCode> codes)
    {
        var n = 0;
        foreach (var c in codes)
        {
            var i = game.Codes.FindIndex(x => string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) { c.Enabled = game.Codes[i].Enabled; game.Codes[i] = c; }
            else { c.Enabled = false; game.Codes.Add(c); n++; }
        }
        return n;
    }

    // ---------- Tri des codes trouvés en ligne ----------

    // Écritures non latines : grec, cyrillique, hébreu, arabe, thaï, kana, chinois, coréen, pleine chasse.
    private static readonly Regex NonLatin = new(@"[Ͱ-ϿЀ-ӿ֐-ۿ฀-๿぀-ヿ㄀-ㄯ㐀-鿿가-힯＀-￯]", RegexOptions.Compiled);
    private static readonly Regex TitleLine = new(@"\b(TID|BID)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Numbering = new(@"^\s*(?:[#№]\s*\d+|\d+)\s*[.):\-]?\s*", RegexOptions.Compiled);

    /// <summary>Nom en français ou en anglais (écriture latine), et pas une ligne de titre « TID: ... BID: ... ».</summary>
    public static bool IsFrenchOrEnglish(string name) => !NonLatin.IsMatch(name) && !TitleLine.IsMatch(name);

    /// <summary>Nom comparable : sans numérotation (« #03. », « №3. »), sans ponctuation ni casse.</summary>
    public static string NameKey(string name) =>
        new string(Numbering.Replace(name, "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Lignes de code seules (sans le titre), sans espaces ni casse : deux codes identiques ont la même clé.</summary>
    public static string BodyKey(string body) =>
        string.Concat(body.Replace("\r", "").Split('\n').Skip(1).Select(l => new string(l.Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant()));

    /// <summary>
    /// Codes trouvés en ligne à garder : en français ou anglais, sans doublon (même nom ou mêmes lignes de code),
    /// ni entre eux ni avec les codes déjà dans la liste du jeu.
    /// </summary>
    public static List<CheatCode> FilterOnline(IEnumerable<CheatCode> found, GameCheatCodes existing)
    {
        var names = existing.Codes.Select(c => NameKey(c.Name)).ToHashSet();
        var bodies = existing.Codes.Select(c => BodyKey(c.Body)).ToHashSet();
        var keep = new List<CheatCode>();
        foreach (var c in found)
        {
            if (!IsFrenchOrEnglish(c.Name)) continue;
            var nk = NameKey(c.Name);
            var bk = BodyKey(c.Body);
            if (nk.Length == 0 || bk.Length == 0 || names.Contains(nk) || bodies.Contains(bk)) continue;
            names.Add(nk);
            bodies.Add(bk);
            keep.Add(c);
        }
        return keep;
    }

    /// <summary>
    /// Recherche en ligne dans les deux bases (switch-cheats-db puis CheatSlips si un jeton est fourni), ajoute les
    /// codes trouvés (désactivés) et renvoie un message pour le joueur.
    /// </summary>
    public static async Task<(int Added, string Message)> SearchOnlineAsync(GameCheatCodes game, string titleId, string? cheatSlipsToken, CancellationToken ct = default)
    {
        var added = 0;
        var notes = new List<string>();
        var failures = 0;
        try
        {
            var db = await FetchOnlineAsync(titleId, game.BuildId, ct);
            added += Merge(game, FilterOnline(db.Codes, game));
            if (db.GameKnown && db.Codes.Count == 0 && game.BuildId != null)
                notes.Add($"switch-cheats-db : rien pour ta version ({game.BuildId}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { failures++; Log.Warn("Codes en ligne (switch-cheats-db)", ex); }
        try
        {
            var cs = await FetchCheatSlipsAsync(titleId, game.BuildId, cheatSlipsToken, ct);
            added += Merge(game, FilterOnline(cs.Codes, game));
            if (cs.Quota && cs.Codes.Count == 0)
                notes.Add("CheatSlips : limite de téléchargements du jour atteinte avec ton compte gratuit, réessaie demain (les codes déjà récupérés restent dans ta liste).");
            else if (cs.Unreadable && cs.Codes.Count == 0)
                notes.Add("CheatSlips a répondu, mais sans codes lisibles (format inattendu).");
            else if (cs.Locked && cs.Codes.Count == 0)
                notes.Add(string.IsNullOrWhiteSpace(cheatSlipsToken)
                    ? "CheatSlips a des codes pour ce jeu : ajoute ton jeton CheatSlips dans les Paramètres (Mode console) pour les récupérer."
                    : "CheatSlips refuse le jeton : vérifie-le dans les Paramètres (Mode console).");
            else if (cs.Codes.Count > 0 && !cs.ExactVersion)
                notes.Add("Codes CheatSlips d'une autre version du jeu : ils peuvent ne pas marcher sur la tienne.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { failures++; Log.Warn("Codes en ligne (CheatSlips)", ex); }

        var head = added > 0 ? $"{added} nouveau{(added > 1 ? "x" : "")} code{(added > 1 ? "s" : "")} trouvé{(added > 1 ? "s" : "")}, désactivé{(added > 1 ? "s" : "")} : coche ceux que tu veux."
            : failures == 2 ? "Recherche impossible (pas de connexion ?)."
            : "Aucun nouveau code trouvé en ligne.";
        return (added, string.Join(" ", new[] { head }.Concat(notes)));
    }

    // ---------- Version du jeu (build ID) ----------

    /// <summary>
    /// Build ID (16 premiers chiffres hexadécimaux) du programme principal du jeu, lu dans les journaux de l'émulateur :
    /// après « title_id=&lt;id&gt; », la ligne du NSO « main ». Null si le jeu n'a jamais été lancé dans l'émulateur.
    /// </summary>
    public static string? FindBuildId(string titleId)
    {
        var logs = PlayTimeSources.YuzuDirs()
            .Select(d => Path.Combine(d, "log"))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.txt"))
            .OrderByDescending(File.GetLastWriteTimeUtc);
        foreach (var log in logs)
        {
            try
            {
                using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                var found = FindBuildIdInLog(sr.ReadToEnd(), titleId);
                if (found != null) return found;
            }
            catch (Exception ex) { Log.WarnOnce("cheat-log-" + log, "Journal de l'émulateur illisible", ex); }
        }
        return null;
    }

    public static string? FindBuildIdInLog(string log, string titleId)
    {
        var at = log.IndexOf("title_id=" + titleId, StringComparison.OrdinalIgnoreCase);
        if (at < 0) at = log.IndexOf("program_id=" + titleId, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        foreach (var line in log[at..].Split('\n'))
        {
            var m = BuildIdRx.Match(line);
            if (!m.Success) continue;
            var id = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return id[..16].ToUpperInvariant();
        }
        return null;
    }

    /// <summary>
    /// Après une partie : note la version du jeu lue dans le journal de l'émulateur (il n'en garde que quelques-uns) et,
    /// si elle a changé, réinstalle les codes cochés pour cette version.
    /// </summary>
    public static void Remember(string titleId)
    {
        try
        {
            if (FindBuildId(titleId) is not { } bid) return;
            var all = Load();
            if (!all.TryGetValue(titleId, out var game) || game.Codes.Count == 0 || game.BuildId == bid) return;
            game.BuildId = bid;
            Save(all);
            if (game.Codes.Any(c => c.Enabled)) Apply(titleId, game);
            Log.Info($"Codes de triche : version {bid} notée pour le jeu {titleId}.");
        }
        catch (Exception ex) { Log.Warn("Codes de triche : version du jeu", ex); }
    }

    // ---------- Installation dans l'émulateur ----------

    /// <summary>
    /// Installe les codes cochés dans chaque émulateur (un dossier d'add-on par code) et retire ceux d'OXYN
    /// qui ne sont plus cochés. Ne touche pas aux autres add-ons (mods de l'utilisateur).
    /// </summary>
    public static bool Apply(string titleId, GameCheatCodes game)
    {
        if (string.IsNullOrWhiteSpace(game.BuildId)) return false;
        var ok = true;
        foreach (var dir in PlayTimeSources.YuzuDirs())
        {
            try
            {
                var load = Path.Combine(dir, "load", titleId.ToUpperInvariant());
                if (Directory.Exists(load))
                    foreach (var old in Directory.GetDirectories(load, FolderPrefix + "*").Concat(Directory.GetDirectories(load, LegacyFolderPrefix + "*")))
                        Directory.Delete(old, true);
                foreach (var c in game.Codes.Where(c => c.Enabled))
                {
                    var cheats = Path.Combine(load, FolderPrefix + SafeName(c.Name), "cheats");
                    Directory.CreateDirectory(cheats);
                    File.WriteAllText(Path.Combine(cheats, game.BuildId.ToUpperInvariant() + ".txt"), c.Body, new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                ok = false;
                Log.Warn($"Codes de triche : installation impossible dans {dir}", ex);
            }
        }
        return ok;
    }

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
        return s.Length > 60 ? s[..60] : s.Length == 0 ? "code" : s;
    }

    // ---------- Recherche en ligne ----------

    /// <summary>Résultat de la base en ligne : codes de la version du jeu, ou versions connues si la sienne n'y est pas.</summary>
    public sealed record OnlineResult(List<CheatCode> Codes, List<string> KnownVersions, bool GameKnown);

    /// <summary>Codes de switch-cheats-db pour ce jeu et cette version (seul l'identifiant du jeu est envoyé).</summary>
    public static async Task<OnlineResult> FetchOnlineAsync(string titleId, string? buildId, CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync(string.Format(DbUrl, titleId.ToUpperInvariant()), ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return new(new(), new(), false);
        resp.EnsureSuccessStatusCode();
        return ParseOnline(await resp.Content.ReadAsStringAsync(ct), buildId);
    }

    // ---------- CheatSlips (compte gratuit + jeton API) ----------

    private const string CheatSlipsUrl = "https://www.cheatslips.com/api/v1/cheats/{0}";

    /// <summary>Le contenu des codes CheatSlips n'est envoyé qu'avec un jeton valide (sinon ce texte).</summary>
    private const string CheatSlipsLocked = "Please register and send APIToken";

    /// <summary>Réponse CheatSlips : codes de la version du joueur d'abord ; à défaut, ceux des autres versions.</summary>
    /// <paramref name="Quota"/> : limite de téléchargements du jour atteinte (« Quota exceeded ») ; <paramref name="Unreadable"/> :
    /// contenu reçu sans aucun code lisible.
    public sealed record CheatSlipsResult(List<CheatCode> Codes, bool ExactVersion, bool GameKnown, bool Locked, bool Quota = false, bool Unreadable = false);

    /// <summary>
    /// Jeton API CheatSlips à partir de l'e-mail et du mot de passe du compte (POST /api/v1/token, réponse { "token" }).
    /// Le mot de passe n'est pas conservé. Renvoie null si le compte est refusé.
    /// </summary>
    public static async Task<string?> GetCheatSlipsTokenAsync(string email, string password, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new { email = email.Trim(), password });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync("https://www.cheatslips.com/api/v1/token", content, ct);
        if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) return null;
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
    }

    /// <summary>Codes CheatSlips du jeu (seuls l'identifiant du jeu et le jeton de l'utilisateur sont envoyés).</summary>
    public static async Task<CheatSlipsResult> FetchCheatSlipsAsync(string titleId, string? buildId, string? token, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, string.Format(CheatSlipsUrl, titleId.ToUpperInvariant()));
        if (!string.IsNullOrWhiteSpace(token)) req.Headers.TryAddWithoutValidation("X-API-TOKEN", token.Trim());
        using var resp = await Http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return new(new(), false, false, false);
        resp.EnsureSuccessStatusCode();
        return ParseCheatSlips(await resp.Content.ReadAsStringAsync(ct), buildId);
    }

    /// <summary>JSON CheatSlips : { "cheats": [ { "buildid": "...", "content": "[Nom]\n...", "titles": [...] } ] }.</summary>
    public static CheatSlipsResult ParseCheatSlips(string json, string? buildId)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("cheats", out var cheats) || cheats.ValueKind != JsonValueKind.Array)
            return new(new(), false, false, false);
        var exact = new List<CheatCode>();
        var other = new List<CheatCode>();
        var locked = false;
        var quota = false;
        var unreadable = false;
        foreach (var c in cheats.EnumerateArray())
        {
            var content = c.TryGetProperty("content", out var ct) ? ct.GetString() ?? "" : "";
            if (content.StartsWith(CheatSlipsLocked, StringComparison.OrdinalIgnoreCase)) { locked = true; continue; }
            if (content.StartsWith("Quota exceeded", StringComparison.OrdinalIgnoreCase)) { quota = true; continue; }
            var bid = c.TryGetProperty("buildid", out var b) ? b.GetString() : null;
            var match = buildId != null && string.Equals(bid, buildId, StringComparison.OrdinalIgnoreCase);
            var parsed = Parse(content, match ? "CheatSlips" : $"CheatSlips (version {bid})");
            if (parsed.Count == 0 && content.Trim().Length > 0) unreadable = true;
            (match ? exact : other).AddRange(parsed);
        }
        return exact.Count > 0 ? new(exact, true, true, locked, quota, unreadable) : new(other, false, true, locked, quota, unreadable);
    }

    /// <summary>JSON de la base : { "&lt;buildID&gt;": { "[Nom]": "[Nom]\nlignes..." }, "attribution": {...} }.</summary>
    public static OnlineResult ParseOnline(string json, string? buildId)
    {
        using var doc = JsonDocument.Parse(json);
        var versions = new List<string>();
        var codes = new List<CheatCode>();
        foreach (var v in doc.RootElement.EnumerateObject())
        {
            if (v.Value.ValueKind != JsonValueKind.Object || !Regex.IsMatch(v.Name, "^[0-9A-Fa-f]{16}$")) continue;
            versions.Add(v.Name.ToUpperInvariant());
            if (buildId == null || !string.Equals(v.Name, buildId, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var c in v.Value.EnumerateObject())
                if (c.Value.ValueKind == JsonValueKind.String)
                    codes.AddRange(Parse(c.Value.GetString() ?? "", "en ligne"));
        }
        return new(codes, versions, true);
    }
}
