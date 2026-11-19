using Oxyn.ConsoleMode;
using Xunit;

namespace Oxyn.Tests;

public class ConsoleThemeTests
{
    [Fact]
    public void Themes_HaveUniqueIds_AndDefaultFallback()
    {
        Assert.True(ConsoleThemes.All.Count >= 8);
        Assert.Equal(ConsoleThemes.All.Count, ConsoleThemes.All.Select(t => t.Id).Distinct().Count());
        Assert.Equal("nuit", ConsoleThemes.Default.Id);
        Assert.Same(ConsoleThemes.Default, ConsoleThemes.Find("inconnu"));
        Assert.Same(ConsoleThemes.Default, ConsoleThemes.Find(null));
        Assert.Equal(0, ConsoleThemes.IndexOf("inconnu"));
    }

    [Fact]
    public void LightThemes_UseDarkText()
    {
        foreach (var t in ConsoleThemes.All.Where(t => t.Light))
            Assert.True(t.Text.R + t.Text.G + t.Text.B < 300, t.Id);
    }

    [Fact]
    public void IntroSound_IsStereo_AndBounded()
    {
        var pcm = IntroSound.Orbit();
        Assert.Equal((int)(8.0 * IntroSound.Rate) * 2, pcm.Length);
        Assert.All(pcm, v => Assert.InRange(v, -1f, 1f));
        Assert.Contains(pcm, v => Math.Abs(v) > 0.05f);
    }

    [Fact]
    public void WelcomeSound_IsAudible_AndBounded()
    {
        var pcm = IntroSound.Welcome();
        Assert.True(pcm.Length >= (int)(3 * IntroSound.Rate) * 2);
        Assert.All(pcm, v => Assert.InRange(v, -1f, 1f));
        Assert.Contains(pcm, v => Math.Abs(v) > 0.02f);
    }

    [Fact]
    public void ConsoleSettings_Defaults()
    {
        var s = new Oxyn.Services.ConsoleSettings();
        Assert.Equal("nuit", s.ThemeId);
        Assert.True(s.IntroEnabled);
    }

    [Fact]
    public void Emulator_FullscreenCheckbox_AddsOrRemovesTheFlag()
    {
        var e = new ConsoleEmulator { ArgumentsTemplate = "-g \"{rom}\"", FullscreenFlag = "-f", Fullscreen = true };
        Assert.Equal("-f -g \"{rom}\"", EmulatorCommandBuilder.WithFullscreen(e));
        e.ArgumentsTemplate = "-f -g \"{rom}\"";
        Assert.Equal("-f -g \"{rom}\"", EmulatorCommandBuilder.WithFullscreen(e));   // déjà présente : pas de doublon
        e.Fullscreen = false;
        Assert.Equal("-g \"{rom}\"", EmulatorCommandBuilder.WithFullscreen(e));
        e.FullscreenFlag = "";
        Assert.Equal("-f -g \"{rom}\"", EmulatorCommandBuilder.WithFullscreen(e));   // option inconnue : modèle intact
    }

    [Fact]
    public void Emulator_KnownPresetGetsItsFlag()
    {
        var e = new ConsoleEmulator { Name = "PCSX2", ExePath = @"C:\nulle\part\pcsx2.exe" };
        EmulatorPresets.EnsureFullscreenFlag(e);
        Assert.Equal("-fullscreen", e.FullscreenFlag);
        var game = new ConsoleGame { RomPath = @"C:\Jeux\a b.iso" };
        e.ArgumentsTemplate = "-nogui \"{rom}\"";
        Assert.Equal("-fullscreen -nogui \"C:\\Jeux\\a b.iso\"", EmulatorCommandBuilder.BuildEmulatorCommand(e, game).Arguments);
    }

    [Fact]
    public void Themes_AreTwenty_AndEveryAmbientIsUsedOrNone()
    {
        Assert.Equal(21, ConsoleThemes.All.Count);
        foreach (ConsoleAmbient a in Enum.GetValues(typeof(ConsoleAmbient)))
            if (a != ConsoleAmbient.None) Assert.Contains(ConsoleThemes.All, t => t.Ambient == a);
    }

    [Fact]
    public void UiSounds_AreShortStereoClips_WithinRange()
    {
        foreach (UiSound s in Enum.GetValues(typeof(UiSound)))
        {
            var clip = UiSounds.Build(s);
            Assert.True(clip.Length > 0 && clip.Length % 2 == 0, s.ToString());
            Assert.True(clip.Length / 2 <= 48000, s.ToString());   // une seconde au plus
            Assert.All(clip, v => Assert.InRange(v, -1f, 1f));
        }
    }

    [Fact]
    public void CyberSounds_AreValid()
    {
        foreach (UiSound s in Enum.GetValues(typeof(UiSound)))
        {
            var clip = UiSounds.BuildCyber(s);
            Assert.True(clip.Length > 0 && clip.Length / 2 <= 48000, s.ToString());
            Assert.All(clip, v => Assert.InRange(v, -1f, 1f));
        }
    }

    [Theory]
    [InlineData("Switch", "SWITCH", 11)]
    [InlineData("Switch 2", "SWITCH 2", 11)]
    [InlineData("PS1", "PS1", 10)]
    [InlineData("PlayStation 2", "PS2", 14)]
    [InlineData("Xbox 360", "XBOX 360", 14)]
    [InlineData("Xbox One", "XBOX ONE", 14)]
    [InlineData("GameCube", "GAMECUBE", 14)]
    [InlineData("Game Boy Advance", "GBA", 28)]
    [InlineData("GameBoy Advance", "GBA", 28)]
    [InlineData("", "PC", 14)]
    public void CaseStyle_MatchesPlatform(string platform, string label, double depth)
    {
        var st = CaseStyle.For(platform);
        Assert.Equal(label, st.Label);
        Assert.Equal(depth, st.DepthMm);
    }

    [Fact]
    public void ShelfBottomRise_GrowsWithCornerRadius()
    {
        Assert.Equal(0, GameBox.ShelfBottomRise(0), 6);
        var small = GameBox.ShelfBottomRise(0.03);
        var big = GameBox.ShelfBottomRise(0.12);
        Assert.True(small > 0 && big > small);
        Assert.True(big < 0.05);   // quelques pixels au plus : simple correction de perspective
    }
}
