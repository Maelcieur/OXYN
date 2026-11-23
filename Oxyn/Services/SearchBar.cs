using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;
using Oxyn.Infrastructure;

namespace Oxyn.Services;

/// <summary>Moteur de recherche : page de résultats et, si le moteur en propose, adresse des suggestions ({0} = texte encodé).</summary>
public sealed record SearchEngine(string Id, string Name, string SearchUrl, string? SuggestUrl);

/// <summary>
/// Commande de la barre de recherche, choisie par l'utilisateur : « /nom texte » envoie le prompt (avec {texte} remplacé)
/// à l'IA de Google, ou corrige le texte hors ligne si <see cref="Correction"/>.
/// </summary>
public sealed class SearchCommand
{
    /// <summary>Nom tapé après « / » (renommable).</summary>
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    /// <summary>Correction orthographique avec le correcteur de Windows (le prompt est alors ignoré).</summary>
    public bool Correction { get; set; }
}

/// <summary>Moteur ajouté par l'utilisateur : nom et adresse de recherche avec {texte} à la place des mots cherchés.</summary>
public sealed class CustomSearchEngine
{
    public string Id { get; set; } = "custom-" + Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>Favori choisi pour la barre (copié depuis le navigateur ou saisi), avec un nom modifiable.</summary>
public sealed class SearchFavorite
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>Favori (Url renseignée) ou dossier de favoris (Children) du navigateur.</summary>
public sealed record Bookmark(string Name, string? Url, IReadOnlyList<Bookmark> Children)
{
    public bool IsFolder => Url is null;
}

/// <summary>Réglages du module Barre de recherche (bureau uniquement).</summary>
public sealed class SearchBarSettings : ObservableSettings
{
    private string _engine = "google";
    private bool _suggestions = true;
    private bool _bookmarks = true;
    private string _barColor = "#FF1B1D22";
    private double _barOpacity = 0.85;
    private string _borderColor = "#FFFFFFFF";
    private double _borderOpacity = 0.2;
    private double _barWidth = 420;
    private double _barHeight = 42;
    private double _popupOpacity = 0.96;

    /// <summary>Identifiant du moteur (<see cref="SearchBar.Engines"/> ou moteur personnalisé).</summary>
    public string Engine { get => _engine; set => Set(ref _engine, value); }

    /// <summary>Suggestions du moteur pendant la frappe (le texte tapé est alors envoyé au moteur).</summary>
    public bool Suggestions { get => _suggestions; set => Set(ref _suggestions, value); }

    /// <summary>Flèche des favoris choisis, et favoris proposés pendant la frappe.</summary>
    public bool Bookmarks { get => _bookmarks; set => Set(ref _bookmarks, value); }

    /// <summary>Couleur de fond de la barre (le module n'a pas de carte autour).</summary>
    public string BarColor { get => _barColor; set => Set(ref _barColor, value); }

    /// <summary>Opacité du fond de la barre (0 = transparent).</summary>
    public double BarOpacity { get => _barOpacity; set => Set(ref _barOpacity, Math.Clamp(value, 0, 1)); }

    public string BorderColor { get => _borderColor; set => Set(ref _borderColor, value); }

    /// <summary>Opacité du contour de la barre (0 = sans contour).</summary>
    public double BorderOpacity { get => _borderOpacity; set => Set(ref _borderOpacity, Math.Clamp(value, 0, 1)); }

    /// <summary>Largeur de la barre, en pixels.</summary>
    public double BarWidth { get => _barWidth; set => Set(ref _barWidth, Math.Clamp(double.IsFinite(value) ? value : 420, 240, 1200)); }

    /// <summary>Hauteur de la barre, en pixels (les textes suivent).</summary>
    public double BarHeight { get => _barHeight; set => Set(ref _barHeight, Math.Clamp(double.IsFinite(value) ? value : 42, 28, 90)); }

    /// <summary>Opacité du fond de la fenêtre de saisie (champ et liste des suggestions et commandes).</summary>
    public double PopupOpacity { get => _popupOpacity; set => Set(ref _popupOpacity, Math.Clamp(double.IsFinite(value) ? value : 0.96, 0, 1)); }

    /// <summary>Couleurs et taille d'origine de la barre.</summary>
    public void ResetLook()
    {
        var d = new SearchBarSettings();
        BarColor = d.BarColor; BarOpacity = d.BarOpacity; BorderColor = d.BorderColor; BorderOpacity = d.BorderOpacity;
        BarWidth = d.BarWidth; BarHeight = d.BarHeight; PopupOpacity = d.PopupOpacity;
    }

    /// <summary>Favoris affichés par la barre, dans l'ordre choisi ; aucun par défaut.</summary>
    public List<SearchFavorite> Favorites { get; set; } = new();

    /// <summary>À appeler après une modification de <see cref="Favorites"/> (enregistrement).</summary>
    public void FavoritesChanged() => Raise(nameof(Favorites));

    /// <summary>Moteurs ajoutés par l'utilisateur, proposés après les moteurs intégrés.</summary>
    public List<CustomSearchEngine> CustomEngines { get; set; } = new();

    /// <summary>À appeler après une modification de <see cref="CustomEngines"/> (enregistrement, pastille et liste à jour).</summary>
    public void EnginesChanged() => Raise(nameof(CustomEngines));

    /// <summary>Commandes choisies dans le catalogue (<see cref="SearchBar.Catalog"/>) ou créées ; aucune par défaut.</summary>
    public List<SearchCommand> Commands { get; set; } = new();

    /// <summary>À appeler après une modification de <see cref="Commands"/> (enregistrement).</summary>
    public void CommandsChanged() => Raise(nameof(Commands));
}

/// <summary>Logique de la barre de recherche : moteurs, commandes /xxx vers l'IA de Google, suggestions, favoris Chromium.</summary>
public static class SearchBar
{
    public static readonly IReadOnlyList<SearchEngine> Engines = new SearchEngine[]
    {
        new("google", "Google", "https://www.google.com/search?q={0}", "https://suggestqueries.google.com/complete/search?client=firefox&hl=fr&q={0}"),
        new("bing", "Bing", "https://www.bing.com/search?q={0}", "https://api.bing.com/osjson.aspx?market=fr-FR&query={0}"),
        new("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q={0}", "https://duckduckgo.com/ac/?type=list&kl=fr-fr&q={0}"),
        new("brave", "Brave Search", "https://search.brave.com/search?q={0}", "https://search.brave.com/api/suggest?q={0}"),
        new("ecosia", "Ecosia", "https://www.ecosia.org/search?q={0}", "https://ac.ecosia.org/autocomplete?type=list&q={0}"),
        new("qwant", "Qwant", "https://www.qwant.com/?q={0}", "https://api.qwant.com/v3/suggest?locale=fr_FR&q={0}"),
        new("youtube", "YouTube", "https://www.youtube.com/results?search_query={0}", "https://suggestqueries.google.com/complete/search?client=firefox&ds=yt&hl=fr&q={0}"),
    };

    public static SearchEngine Engine(string? id) => Engines.FirstOrDefault(e => e.Id == id) ?? Engines[0];

    /// <summary>Moteurs intégrés puis moteurs personnalisés valides (nom et adresse renseignés).</summary>
    public static IReadOnlyList<SearchEngine> AllEngines(SearchBarSettings settings)
        => Engines.Concat(settings.CustomEngines
                .Where(c => c.Name.Trim().Length > 0 && NormalizeTemplate(c.Url) is not null)
                .Select(c => new SearchEngine(c.Id, c.Name.Trim(), NormalizeTemplate(c.Url)!, null)))
            .ToList();

    /// <summary>Moteur choisi (Google si le moteur personnalisé a été supprimé).</summary>
    public static SearchEngine Engine(SearchBarSettings settings)
        => AllEngines(settings).FirstOrDefault(e => e.Id == settings.Engine) ?? Engines[0];

    /// <summary>
    /// Adresse d'un moteur personnalisé → modèle interne ({0}) : {texte}, %s ou {searchTerms} marquent les mots cherchés ;
    /// sans marqueur, les mots sont ajoutés à la fin. Null si ce n'est pas une adresse http(s).
    /// </summary>
    public static string? NormalizeTemplate(string? url)
    {
        var u = url?.Trim() ?? "";
        if (u.Length == 0) return null;
        if (!u.Contains("://")) u = "https://" + u;
        foreach (var marker in new[] { "{texte}", "%s", "{searchTerms}", "{query}" })
            u = u.Replace(marker, "{0}", StringComparison.OrdinalIgnoreCase);
        if (!u.Contains("{0}")) u += "{0}";
        var probe = u.Replace("{0}", "test");
        return Uri.TryCreate(probe, UriKind.Absolute, out var p) && (p.Scheme == Uri.UriSchemeHttp || p.Scheme == Uri.UriSchemeHttps) ? u : null;
    }

    /// <summary>Moteurs prêts à ajouter (nom, adresse avec {texte}).</summary>
    public static IReadOnlyList<(string Name, string Url)> EngineCatalog { get; } = new[]
    {
        ("Wikipédia", "https://fr.wikipedia.org/w/index.php?search={texte}"),
        ("Amazon", "https://www.amazon.fr/s?k={texte}"),
        ("GitHub", "https://github.com/search?q={texte}"),
        ("Reddit", "https://www.reddit.com/search/?q={texte}"),
        ("Twitch", "https://www.twitch.tv/search?term={texte}"),
        ("Steam", "https://store.steampowered.com/search/?term={texte}"),
        ("Google Images", "https://www.google.com/search?tbm=isch&q={texte}"),
        ("Google Maps", "https://www.google.com/maps/search/{texte}"),
        ("WordReference (anglais-français)", "https://www.wordreference.com/enfr/{texte}"),
        ("Perplexity", "https://www.perplexity.ai/search?q={texte}"),
        ("ChatGPT", "https://chatgpt.com/?q={texte}"),
    };

    /// <summary>Nom de commande tel qu'il se tape : minuscules, sans « / » ni espace.</summary>
    public static string CommandKey(string name)
        => new string(name.Trim().TrimStart('/').Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    /// <summary>Commandes proposées à l'ajout (libellé affiché, commande copiée dans les réglages).</summary>
    public static IReadOnlyList<(string Title, SearchCommand Command)> Catalog() => new (string, SearchCommand)[]
    {
        ("Correction orthographique (hors ligne)", new() { Name = "correction", Correction = true }),
        ("Résumer un texte", new() { Name = "resume", Prompt = "Résume ce texte en quelques points clés : {texte}" }),
        ("Traduire", new() { Name = "traduire", Prompt = "Traduis ce texte en français (ou en anglais s'il est déjà en français), sans commentaire : {texte}" }),
        ("Expliquer simplement", new() { Name = "explique", Prompt = "Explique simplement, comme à un débutant : {texte}" }),
        ("Définition d'un mot", new() { Name = "definition", Prompt = "Donne la définition de « {texte} » avec un exemple d'utilisation." }),
        ("Synonymes", new() { Name = "synonymes", Prompt = "Donne des synonymes de « {texte} », du plus courant au plus rare." }),
        ("Reformuler", new() { Name = "reformuler", Prompt = "Reformule ce texte de façon claire et naturelle : {texte}" }),
        ("Comparer", new() { Name = "compare", Prompt = "Compare de façon objective, avec un tableau des avantages et inconvénients : {texte}" }),
        ("Recette avec des ingrédients", new() { Name = "recette", Prompt = "Propose une recette simple avec : {texte}" }),
        ("Écrire du code", new() { Name = "code", Prompt = "Écris le code demandé, avec une courte explication : {texte}" }),
    };

    /// <summary>Nom libre dérivé de <paramref name="name"/> (« resume », « resume2 »…) parmi les commandes existantes.</summary>
    public static string UniqueName(IEnumerable<SearchCommand> existing, string name)
    {
        var used = existing.Select(c => CommandKey(c.Name)).ToHashSet();
        var key = CommandKey(name);
        if (!used.Contains(key)) return key;
        for (var i = 2; ; i++)
            if (!used.Contains(key + i)) return key + i;
    }

    /// <summary>
    /// Vrai si la liste est exactement celle mise d'office par la première version de la barre (toutes les commandes,
    /// jamais modifiées) : elle est alors vidée, il n'y a plus de commande par défaut.
    /// </summary>
    public static bool IsOldDefaultList(IReadOnlyList<SearchCommand> commands)
        => commands.Count == OldDefaults.Length
           && commands.Zip(OldDefaults).All(p => p.First.Name == p.Second.Name && p.First.Prompt == p.Second.Prompt && !p.First.Correction);

    private static readonly SearchCommand[] OldDefaults =
    {
        new() { Name = "resume", Prompt = "Résume ce texte en quelques points clés : {texte}" },
        new() { Name = "traduire", Prompt = "Traduis ce texte en français (ou en anglais s'il est déjà en français), sans commentaire : {texte}" },
        new() { Name = "explique", Prompt = "Explique simplement, comme à un débutant : {texte}" },
        new() { Name = "definition", Prompt = "Donne la définition de « {texte} » avec un exemple d'utilisation." },
        new() { Name = "synonymes", Prompt = "Donne des synonymes de « {texte} », du plus courant au plus rare." },
        new() { Name = "reformuler", Prompt = "Reformule ce texte de façon claire et naturelle : {texte}" },
        new() { Name = "compare", Prompt = "Compare de façon objective, avec un tableau des avantages et inconvénients : {texte}" },
        new() { Name = "recette", Prompt = "Propose une recette simple avec : {texte}" },
        new() { Name = "code", Prompt = "Écris le code demandé, avec une courte explication : {texte}" },
    };

    /// <summary>« /nom reste » → (nom en minuscules, reste) ; null si le texte n'est pas une commande.</summary>
    public static (string Name, string Argument)? ParseCommand(string text)
    {
        var t = text.TrimStart();
        if (t.Length < 1 || t[0] != '/') return null;
        var space = t.IndexOfAny(new[] { ' ', '\t' });
        var name = (space < 0 ? t[1..] : t[1..space]).ToLowerInvariant();
        var argument = space < 0 ? "" : t[(space + 1)..].Trim();
        return (name, argument);
    }

    /// <summary>Prompt final : {texte} remplacé, ou texte ajouté à la fin si le prompt ne le contient pas.</summary>
    public static string BuildPrompt(string prompt, string argument)
    {
        if (prompt.Contains("{texte}", StringComparison.OrdinalIgnoreCase))
            return prompt.Replace("{texte}", argument, StringComparison.OrdinalIgnoreCase).Trim();
        return argument.Length == 0 ? prompt.Trim() : $"{prompt.Trim().TrimEnd(':').TrimEnd()} : {argument}";
    }

    /// <summary>Mode IA de Google (udm=50) avec le prompt déjà rempli.</summary>
    public static string AiUrl(string prompt) => "https://www.google.com/search?udm=50&q=" + Uri.EscapeDataString(prompt);

    // Replace plutôt que string.Format : une adresse personnalisée peut contenir d'autres accolades.
    public static string SearchUrl(SearchEngine engine, string text) => engine.SearchUrl.Replace("{0}", Uri.EscapeDataString(text.Trim()));

    /// <summary>Adresse tapée directement (« exemple.fr », « https://… ») → adresse complète, sinon null.</summary>
    public static string? AsUrl(string text)
    {
        var t = text.Trim();
        if (t.Length == 0 || t.Contains(' ')) return null;
        if (Uri.TryCreate(t, UriKind.Absolute, out var abs) && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
            return abs.ToString();
        if (t.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) return "http://" + t;
        // nom.domaine (au moins un point, extension de 2 lettres ou plus, sans caractère interdit dans un nom d'hôte)
        var host = t.Split('/', '?', '#')[0];
        var dot = host.LastIndexOf('.');
        if (dot <= 0 || host.Length - dot - 1 < 2) return null;
        if (!host[(dot + 1)..].All(char.IsLetter) || host.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '-' or ':'))) return null;
        return Uri.TryCreate("https://" + t, UriKind.Absolute, out var u) ? u.ToString() : null;
    }

    /// <summary>Réponse de suggestions : format OpenSearch [texte, [suggestions…]] ou Qwant {data:{items:[{value}]}}.</summary>
    public static List<string> ParseSuggestions(string json)
    {
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 1 && root[1].ValueKind == JsonValueKind.Array)
            {
                foreach (var s in root[1].EnumerateArray())
                    if (s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } v) result.Add(v);
            }
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
                     && data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                    if (item.TryGetProperty("value", out var v) && v.GetString() is { Length: > 0 } s) result.Add(s);
            }
        }
        catch (JsonException) { /* réponse inattendue : pas de suggestions */ }
        return result;
    }

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) OXYN");
        return client;
    }

    /// <summary>Suggestions du moteur pour <paramref name="text"/> (liste vide si le moteur n'en propose pas ou en cas d'erreur).</summary>
    public static async Task<List<string>> FetchSuggestionsAsync(SearchEngine engine, string text, CancellationToken token)
    {
        if (engine.SuggestUrl is null || string.IsNullOrWhiteSpace(text)) return new();
        try
        {
            using var response = await Http.GetAsync(engine.SuggestUrl.Replace("{0}", Uri.EscapeDataString(text.Trim())), token);
            if (!response.IsSuccessStatusCode) return new();
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            // Google répond parfois en ISO-8859-1 sans le dire clairement : UTF-8 strict d'abord, sinon Latin-1.
            string json;
            try { json = new System.Text.UTF8Encoding(false, true).GetString(bytes); }
            catch (System.Text.DecoderFallbackException) { json = System.Text.Encoding.Latin1.GetString(bytes); }
            return ParseSuggestions(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (!token.IsCancellationRequested) Log.WarnOnce("search-suggest-" + engine.Id, $"Suggestions {engine.Name} indisponibles", ex);
            return new();
        }
    }

    // ---------- Favoris du navigateur par défaut (Chromium : Chrome, Edge, Brave, Opera) ----------

    /// <summary>Dossier de données de chaque navigateur Chromium, selon l'identifiant d'association https (ProgId).</summary>
    private static (string Name, string UserData, bool Profiles)? BrowserFor(string progId)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return progId switch
        {
            _ when progId.StartsWith("ChromeHTML", StringComparison.OrdinalIgnoreCase) => ("Chrome", Path.Combine(local, @"Google\Chrome\User Data"), true),
            _ when progId.StartsWith("MSEdgeHTM", StringComparison.OrdinalIgnoreCase) => ("Edge", Path.Combine(local, @"Microsoft\Edge\User Data"), true),
            _ when progId.StartsWith("BraveHTML", StringComparison.OrdinalIgnoreCase) => ("Brave", Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"), true),
            _ when progId.StartsWith("OperaGX", StringComparison.OrdinalIgnoreCase) => ("Opera GX", Path.Combine(roaming, @"Opera Software\Opera GX Stable"), false),
            _ when progId.StartsWith("Opera", StringComparison.OrdinalIgnoreCase) => ("Opera", Path.Combine(roaming, @"Opera Software\Opera Stable"), false),
            _ => null
        };
    }

    /// <summary>Nom du navigateur par défaut s'il est pris en charge (Chromium), sinon null.</summary>
    public static string? DefaultBrowserName() => DefaultBrowser()?.Name;

    private static (string Name, string UserData, bool Profiles)? DefaultBrowser()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            return key?.GetValue("ProgId") is string progId ? BrowserFor(progId) : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (string Path, DateTime Stamp, List<Bookmark> Items)? _bookmarkCache;

    /// <summary>
    /// Favoris du navigateur par défaut (barre de favoris, autres favoris, favoris mobiles), relus seulement si le fichier
    /// a changé. Liste vide si le navigateur n'est pas un Chromium connu ou si le fichier est introuvable.
    /// </summary>
    public static IReadOnlyList<Bookmark> LoadBookmarks()
    {
        if (DefaultBrowser() is not { } browser) return Array.Empty<Bookmark>();
        var file = BookmarksFile(browser.UserData, browser.Profiles);
        if (file is null || !File.Exists(file)) return Array.Empty<Bookmark>();
        try
        {
            var stamp = File.GetLastWriteTimeUtc(file);
            if (_bookmarkCache is { } cache && cache.Path == file && cache.Stamp == stamp) return cache.Items;
            var items = ParseBookmarks(File.ReadAllText(file));
            _bookmarkCache = (file, stamp, items);
            return items;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.WarnOnce("bookmarks-read", $"Favoris de {browser.Name} illisibles", ex);
            return Array.Empty<Bookmark>();
        }
    }

    /// <summary>Fichier Bookmarks du dernier profil utilisé (Local State › profile.last_used), sinon du profil « Default ».</summary>
    private static string? BookmarksFile(string userData, bool profiles)
    {
        if (!profiles) return Path.Combine(userData, "Bookmarks");
        var profile = "Default";
        try
        {
            var state = Path.Combine(userData, "Local State");
            if (File.Exists(state))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(state));
                if (doc.RootElement.TryGetProperty("profile", out var p) && p.TryGetProperty("last_used", out var last)
                    && last.GetString() is { Length: > 0 } name && Directory.Exists(Path.Combine(userData, name)))
                    profile = name;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { /* profil par défaut */ }
        return Path.Combine(userData, profile, "Bookmarks");
    }

    /// <summary>Lit le fichier Bookmarks (JSON Chromium) : un dossier par racine non vide, la barre de favoris en premier et à plat.</summary>
    public static List<Bookmark> ParseBookmarks(string json)
    {
        var result = new List<Bookmark>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("roots", out var roots)) return result;
            if (roots.TryGetProperty("bookmark_bar", out var bar)) result.AddRange(Children(bar));
            foreach (var name in new[] { "other", "synced" })
            {
                if (!roots.TryGetProperty(name, out var root)) continue;
                var children = Children(root);
                if (children.Count == 0) continue;
                var title = root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } s ? s
                    : name == "other" ? "Autres favoris" : "Favoris mobiles";
                result.Add(new Bookmark(title, null, children));
            }
        }
        catch (JsonException ex) { Log.WarnOnce("bookmarks-json", "Fichier de favoris illisible", ex); }
        return result;
    }

    private static List<Bookmark> Children(JsonElement folder)
    {
        var list = new List<Bookmark>();
        if (!folder.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) return list;
        foreach (var child in children.EnumerateArray())
        {
            var name = child.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var type = child.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "url" && child.TryGetProperty("url", out var u) && u.GetString() is { Length: > 0 } url)
                list.Add(new Bookmark(name.Length > 0 ? name : url, url, Array.Empty<Bookmark>()));
            else if (type == "folder")
                list.Add(new Bookmark(name, null, Children(child)));
        }
        return list;
    }

    /// <summary>Favoris (tous dossiers confondus) dont le nom ou l'adresse contient <paramref name="text"/>.</summary>
    public static IEnumerable<Bookmark> FindBookmarks(IEnumerable<Bookmark> items, string text)
    {
        var t = text.Trim();
        if (t.Length < 2) yield break;
        foreach (var b in items)
        {
            if (b.IsFolder)
            {
                foreach (var c in FindBookmarks(b.Children, t)) yield return c;
            }
            else if (b.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase) || b.Url!.Contains(t, StringComparison.OrdinalIgnoreCase))
                yield return b;
        }
    }

    /// <summary>Ouvre une adresse dans le navigateur par défaut.</summary>
    public static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Ouverture de {url} impossible", ex);
        }
    }
}
