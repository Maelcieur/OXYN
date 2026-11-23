using Oxyn.Services;
using Xunit;

namespace Oxyn.Tests;

public class SearchBarTests
{
    [Fact]
    public void ParseCommand_SplitsNameAndArgument()
    {
        Assert.Equal(("resume", "un long texte"), SearchBar.ParseCommand("/Resume   un long texte "));
        Assert.Equal(("tra", ""), SearchBar.ParseCommand("/tra"));
        Assert.Null(SearchBar.ParseCommand("météo rouen"));
        Assert.Null(SearchBar.ParseCommand(""));
    }

    [Fact]
    public void BuildPrompt_ReplacesOrAppendsText()
    {
        Assert.Equal("Explique : les trous noirs", SearchBar.BuildPrompt("Explique : {texte}", "les trous noirs"));
        Assert.Equal("Explique : les trous noirs", SearchBar.BuildPrompt("Explique :", "les trous noirs"));
        Assert.Equal("Blague du jour", SearchBar.BuildPrompt("Blague du jour", ""));
    }

    [Fact]
    public void AiUrl_UsesGoogleAiModeWithEncodedPrompt()
    {
        Assert.Equal("https://www.google.com/search?udm=50&q=R%C3%A9sume%20%3A%20a%26b", SearchBar.AiUrl("Résume : a&b"));
    }

    [Fact]
    public void AsUrl_RecognizesAddressesOnly()
    {
        Assert.Equal("https://exemple.fr/", SearchBar.AsUrl("exemple.fr"));
        Assert.Equal("https://www.youtube.com/watch?v=1", SearchBar.AsUrl("www.youtube.com/watch?v=1"));
        Assert.Equal("http://site.com/", SearchBar.AsUrl("http://site.com"));
        Assert.Null(SearchBar.AsUrl("météo rouen"));
        Assert.Null(SearchBar.AsUrl("3.14"));
        Assert.Null(SearchBar.AsUrl("fichier.c"));
    }

    [Fact]
    public void ParseSuggestions_ReadsOpenSearchAndQwant()
    {
        Assert.Equal(new[] { "météo rouen", "météo paris" }, SearchBar.ParseSuggestions("""["météo",["météo rouen","météo paris"]]"""));
        Assert.Equal(new[] { "chat", "chaton" }, SearchBar.ParseSuggestions("""{"status":"success","data":{"items":[{"value":"chat"},{"value":"chaton"}]}}"""));
        Assert.Empty(SearchBar.ParseSuggestions("pas du json"));
    }

    [Fact]
    public void ParseBookmarks_FlattensBarAndKeepsFolders()
    {
        const string json = """
        {"roots":{
          "bookmark_bar":{"children":[
            {"type":"url","name":"GitHub","url":"https://github.com/"},
            {"type":"folder","name":"Jeux","children":[{"type":"url","name":"Steam","url":"https://store.steampowered.com/"}]}
          ],"name":"Barre de favoris","type":"folder"},
          "other":{"children":[{"type":"url","name":"","url":"https://exemple.fr/"}],"name":"Autres favoris","type":"folder"},
          "synced":{"children":[],"name":"Favoris mobiles","type":"folder"}
        }}
        """;
        var items = SearchBar.ParseBookmarks(json);
        Assert.Equal(new[] { "GitHub", "Jeux", "Autres favoris" }, items.Select(b => b.Name));
        Assert.True(items[1].IsFolder);
        Assert.Equal("https://exemple.fr/", items[2].Children[0].Name); // sans nom : l'adresse sert de nom
        Assert.Equal(new[] { "Steam" }, SearchBar.FindBookmarks(items, "steam").Select(b => b.Name));
        Assert.Empty(SearchBar.FindBookmarks(items, "s")); // une seule lettre : pas de recherche
    }

    [Fact]
    public void Engine_FallsBackToGoogle()
    {
        Assert.Equal("google", SearchBar.Engine("inconnu").Id);
        Assert.Equal("DuckDuckGo", SearchBar.Engine("duckduckgo").Name);
        Assert.All(SearchBar.Engines, e => Assert.Contains("{0}", e.SearchUrl));
    }

    [Fact]
    public void CustomEngines_AreNormalizedAndSelectable()
    {
        Assert.Equal("https://fr.wikipedia.org/w/index.php?search={0}", SearchBar.NormalizeTemplate("https://fr.wikipedia.org/w/index.php?search={texte}"));
        Assert.Equal("https://site.fr/?q={0}", SearchBar.NormalizeTemplate("site.fr/?q=%s"));
        Assert.Equal("https://site.fr/recherche/{0}", SearchBar.NormalizeTemplate("https://site.fr/recherche/"));
        Assert.Null(SearchBar.NormalizeTemplate(""));
        Assert.Null(SearchBar.NormalizeTemplate("pas une adresse"));

        var s = new SearchBarSettings();
        s.CustomEngines.Add(new CustomSearchEngine { Id = "custom-1", Name = "Wiki", Url = "fr.wikipedia.org/w/index.php?search={texte}" });
        s.CustomEngines.Add(new CustomSearchEngine { Id = "custom-2", Name = "Cassé", Url = "" });
        Assert.Equal(SearchBar.Engines.Count + 1, SearchBar.AllEngines(s).Count);
        s.Engine = "custom-1";
        var engine = SearchBar.Engine(s);
        Assert.Equal("Wiki", engine.Name);
        Assert.Equal("https://fr.wikipedia.org/w/index.php?search=trou%20noir", SearchBar.SearchUrl(engine, "trou noir"));
        s.Engine = "custom-2"; // invalide ou supprimé : Google
        Assert.Equal("google", SearchBar.Engine(s).Id);
    }

    [Fact]
    public void Commands_NoneByDefault_RenamedAndUnique()
    {
        Assert.Empty(new SearchBarSettings().Commands);
        Assert.Empty(new SearchBarSettings().Favorites); // favoris choisis par l'utilisateur, aucun d'office
        Assert.Equal("resume", SearchBar.CommandKey(" /Ré sume ".Replace("é", "e")));
        var existing = new List<SearchCommand> { new() { Name = "resume" }, new() { Name = "resume2" } };
        Assert.Equal("resume3", SearchBar.UniqueName(existing, "Resume"));
        Assert.Equal("traduire", SearchBar.UniqueName(existing, "traduire"));
        Assert.Contains(SearchBar.Catalog(), c => c.Command.Correction);
    }

    [Fact]
    public void OldDefaultCommandList_IsCleared()
    {
        var old = SearchBar.Catalog().Where(c => !c.Command.Correction).Select(c => c.Command).ToList();
        Assert.True(SearchBar.IsOldDefaultList(old));
        old[0].Name = "resumer"; // renommée par l'utilisateur : on garde
        Assert.False(SearchBar.IsOldDefaultList(old));
        Assert.False(SearchBar.IsOldDefaultList(new List<SearchCommand>()));
    }

    [Fact]
    public void SpellApply_ReplacesFromTheEndAndRemovesRepeatedWords()
    {
        var text = "le le chat mangue";
        var fixes = new[] { new SpellCorrector.Fix(3, 2, ""), new SpellCorrector.Fix(11, 6, "mange") };
        Assert.Equal("le chat mange", SpellCorrector.Apply(text, fixes));
        // Corrections qui se chevauchent : seule la dernière (la plus à droite) est appliquée.
        Assert.Equal("abX", SpellCorrector.Apply("abcd", new[] { new SpellCorrector.Fix(1, 3, "Y"), new SpellCorrector.Fix(2, 2, "X") }));
    }

    [Fact]
    public void SpellCorrector_UsesWindowsDictionaryWhenAvailable()
    {
        // Correcteur réel de Windows : ignoré s'il n'y a aucun dictionnaire (machine de build sans langue installée).
        if (SpellCorrector.Language is null) return;
        Assert.Equal("Il fait beau aujourd'hui", SpellCorrector.Correct("Il fait beau aujourdhui"));
        Assert.Equal("Bonjour", SpellCorrector.Correct("Bonjour"));
    }
}
