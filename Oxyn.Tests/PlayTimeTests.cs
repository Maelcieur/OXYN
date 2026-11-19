using Oxyn.ConsoleMode;
using Xunit;

namespace Oxyn.Tests;

public class PlayTimeTests
{
    [Fact]
    public void PlaytimeBin_ReadsIdAndSecondsPairs()
    {
        var b = new byte[32];
        BitConverter.GetBytes(0x0400486024DEC000UL).CopyTo(b, 0);
        BitConverter.GetBytes(60383UL).CopyTo(b, 8);
        BitConverter.GetBytes(0x0100F2C0115B6000UL).CopyTo(b, 16);
        BitConverter.GetBytes(1350UL).CopyTo(b, 24);
        var list = PlayTimeSources.ParsePlaytimeBin(b).ToList();
        Assert.Equal(("0400486024DEC000", TimeSpan.FromSeconds(60383)), list[0]);
        Assert.Equal(("0100F2C0115B6000", TimeSpan.FromSeconds(1350)), list[1]);
    }

    [Fact]
    public void SteamLocalConfig_ReadsPlaytimeMinutes()
    {
        const string vdf = """
            "UserLocalConfigStore"
            {
                "Software"
                {
                    "valve"
                    {
                        "Steam"
                        {
                            "apps"
                            {
                                "620" { "LastPlayed" "1700000000" "Playtime" "125" "Playtime2wks" "3" }
                                "440" { "LastPlayed" "1" }
                                "730" { "Playtime" "0" }
                            }
                        }
                    }
                }
            }
            """;
        var list = PlayTimeSources.ParseSteamPlaytimes(vdf).ToList();
        Assert.Equal(new[] { ("620", TimeSpan.FromMinutes(125)) }, list);
    }

    [Fact]
    public void TitleId_FromFileNameOrSaved_AndShownTimePrefersRealOne()
    {
        var g = new ConsoleGame { EmulatorId = "e", RomPath = @"C:\Jeux\Zelda [0400486024dec000][v131072].nup", PlayTime = TimeSpan.FromHours(2) };
        Assert.Equal("0400486024DEC000", PlayTimeSources.TitleIdOf(g));
        Assert.Null(PlayTimeSources.TitleIdOf(new ConsoleGame { EmulatorId = "e", RomPath = @"C:\Jeux\Mario Eur.xci" }));
        Assert.Equal("0100ABCDEF012000", PlayTimeSources.TitleIdOf(new ConsoleGame { SwitchTitleId = "0100abcdef012000" }));

        Assert.Equal(TimeSpan.FromHours(2), g.ShownPlayTime);
        g.ExternalPlayTime = TimeSpan.FromHours(16);
        Assert.Equal(TimeSpan.FromHours(16), g.ShownPlayTime);
    }
}
