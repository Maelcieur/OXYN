using Oxyn.ConsoleMode.Cheats;
using Xunit;

namespace Oxyn.Tests;

public class CheatCodesTests
{
    [Fact]
    public void Parse_ReadsAtmosphereCodes_AllDisabled()
    {
        const string text = "[Rupees Infinis]\r\n04100000\r\n7F37C134\r\n0000270F\r\n\r\n[Noix de Deku Infinies]\n04100000\n7F37CA1C\n0000270F\n\n[Titre vide]\n";
        var codes = CheatCodes.Parse(text);
        Assert.Equal(2, codes.Count);
        Assert.Equal("Rupees Infinis", codes[0].Name);
        Assert.Equal("[Rupees Infinis]\n04100000\n7F37C134\n0000270F\n", codes[0].Body);
        Assert.Equal("Noix de Deku Infinies", codes[1].Name);
        Assert.All(codes, c => Assert.False(c.Enabled));
    }

    [Fact]
    public void Merge_NewCodesDisabled_ExistingKeepTheirState()
    {
        var game = new GameCheatCodes { Codes = { new CheatCode { Name = "Bombes", Body = "[Bombes]\n1\n", Enabled = true } } };
        var added = CheatCodes.Merge(game, CheatCodes.Parse("[bombes]\n04100000\n[Flèches]\n04100000\n"));
        Assert.Equal(1, added);
        Assert.Equal(2, game.Codes.Count);
        Assert.True(game.Codes[0].Enabled);                 // déjà activé : une nouvelle recherche ne le décoche pas
        Assert.Equal("[bombes]\n04100000\n", game.Codes[0].Body);
        Assert.False(game.Codes[1].Enabled);                // nouveau : désactivé
        Assert.Equal("Flèches", game.Codes[1].Name);
    }

    [Fact]
    public void CheatSlips_LockedWithoutToken_AndOtherVersionsAsFallback()
    {
        const string locked = """{ "titleid": "0400486024DEC000", "cheats": [ { "buildid": "8A30F8939963C7D5", "content": "Please register and send APIToken...", "id": 6251 } ] }""";
        var r = CheatCodes.ParseCheatSlips(locked, "E0F3CCFAF50065AE");
        Assert.True(r.Locked);
        Assert.Empty(r.Codes);

        const string open = """{ "cheats": [ { "buildid": "8A30F8939963C7D5", "content": "[Max Hearts]\n04100000 7F37C130 00000140\n[Infinite Bombs]\n04100000 7F37C83C 0000270F\n" } ] }""";
        var other = CheatCodes.ParseCheatSlips(open, "E0F3CCFAF50065AE");
        Assert.False(other.ExactVersion);
        Assert.Equal(new[] { "Max Hearts", "Infinite Bombs" }, other.Codes.Select(c => c.Name));
        Assert.Contains("8A30F8939963C7D5", other.Codes[0].Source);
        Assert.True(CheatCodes.ParseCheatSlips(open, "8a30f8939963c7d5").ExactVersion);
    }

    [Fact]
    public void CheatSlips_DailyQuotaIsReported()
    {
        // Réponse réelle du 16/11 avec un compte gratuit : plus de téléchargements pour la journée.
        const string json = """{ "cheats": [ { "buildid": "8A30F8939963C7D5", "content": "Quota exceeded for today !", "id": 6251 } ] }""";
        var r = CheatCodes.ParseCheatSlips(json, "E0F3CCFAF50065AE");
        Assert.True(r.Quota);
        Assert.False(r.Locked);
        Assert.False(r.Unreadable);
        Assert.Empty(r.Codes);
    }

    [Fact]
    public void BuildId_IsReadFromTheEmulatorLogAfterTheTitle()
    {
        const string log = """
            [0.1] Loader <Info> patch_manager.cpp:769:HasNSOPatch: Querying NSO patch existence for build_id=AAAAAAAAAAAAAAAA0000, name=main
            [0.9] Loader <Info> patch_manager.cpp:360:PatchExeFS: Patching ExeFS for title_id=0400486024DEC000
            [1.0] Loader <Info> patch_manager.cpp:769:HasNSOPatch: Querying NSO patch existence for build_id=8E213F228D12C7B0F8C2, name=rtld
            [1.1] Loader <Info> patch_manager.cpp:769:HasNSOPatch: Querying NSO patch existence for build_id=E0F3CCFAF50065AE4EA01EB5AEB13B1A45DA3348, name=main
            """;
        Assert.Equal("E0F3CCFAF50065AE", CheatCodes.FindBuildIdInLog(log, "0400486024DEC000"));
        Assert.Null(CheatCodes.FindBuildIdInLog(log, "0100000000000000"));
    }

    [Fact]
    public void Online_KeepsOnlyTheMatchingVersion()
    {
        const string json = """
            {
              "16A91992BBA71201": { "[Infinite Rupies]": "[Infinite Rupies]\n580F0000 025ce340\n", "[Moon Jump]": "[Moon Jump]\n80000040\n" },
              "8E9978D50BDD20B4": { "[Autre]": "[Autre]\n1\n" },
              "attribution": { "x": "y" }
            }
            """;
        var r = CheatCodes.ParseOnline(json, "16a91992bba71201");
        Assert.Equal(new[] { "Infinite Rupies", "Moon Jump" }, r.Codes.Select(c => c.Name));
        Assert.All(r.Codes, c => Assert.Equal("en ligne", c.Source));
        Assert.Equal(new[] { "16A91992BBA71201", "8E9978D50BDD20B4" }, r.KnownVersions);
        Assert.Empty(CheatCodes.ParseOnline(json, "0000000000000000").Codes);
    }

    [Fact]
    public void OnlineFilter_KeepsFrenchOrEnglish_WithoutDuplicates()
    {
        var existing = new GameCheatCodes { Codes = { new CheatCode { Name = "Bombes infinies", Body = "[Bombes infinies]\n04100000 7F37C83C 0000270F\n" } } };
        var found = CheatCodes.Parse("""
            [心心最大值]
            04100000 11111111 00000001
            [Бесконечное здоровье]
            04100000 22222222 00000001
            [#03. Inf. Durability]
            04100000 33333333 00000001
            [№ 3. Inf. Durability]
            04100000 44444444 00000001
            [Vie infinie]
            04100000 55555555 00000001
            [Infinite Health]
            04100000 55555555 00000001
            [Infinite Bombs]
            04100000 7f37c83c 0000270f
            [Zelda TOTK v1.2.1 TID: 0100F2C0115B6000 BID: 9B4E43650501A4D4]
            04000000 00000000 00000000
            """, "en ligne");
        var keep = CheatCodes.FilterOnline(found, existing);
        // Chinois et russe écartés ; « №3. » = doublon de « #03. » ; « Infinite Health » = mêmes lignes que « Vie infinie » ;
        // « Infinite Bombs » = mêmes lignes que le code déjà présent ; ligne de titre TID/BID écartée.
        Assert.Equal(new[] { "#03. Inf. Durability", "Vie infinie" }, keep.Select(c => c.Name));
    }

    [Fact]
    public void SafeName_RemovesForbiddenCharacters()
    {
        Assert.Equal("Bombes _ infinies", CheatCodes.SafeName("Bombes : infinies"));
        Assert.Equal("code", CheatCodes.SafeName("..."));
    }
}
