using Oxyn.Services;
using Xunit;

namespace Oxyn.Tests;

public class UpdateServiceTests
{
    private static readonly DateTime Installed = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    private static UpdateInfo Setup(string version, DateTime built) =>
        new(@"C:\x\OxynSetup.exe", Version.Parse(version), built, $"{version}|{built:O}|1");

    [Fact]
    public void HigherVersion_IsAnUpdate()
    {
        Assert.True(UpdateService.IsNewer(Setup("1.16.0", Installed), new Version(1, 15, 0), Installed, ""));
    }

    [Fact]
    public void LowerVersion_IsNotAnUpdate()
    {
        Assert.False(UpdateService.IsNewer(Setup("1.14.0", Installed.AddDays(3)), new Version(1, 15, 0), Installed, ""));
    }

    [Fact]
    public void SameVersion_RebuiltLater_IsAnUpdate_UntilInstalled()
    {
        var rebuilt = Setup("1.15.0", Installed.AddHours(2));
        Assert.True(UpdateService.IsNewer(rebuilt, new Version(1, 15, 0), Installed, ""));
        Assert.False(UpdateService.IsNewer(rebuilt, new Version(1, 15, 0), Installed, rebuilt.Stamp));   // déjà installé par la mise à jour
    }

    [Fact]
    public void SameVersion_SameBuild_IsNotAnUpdate()
    {
        // L'installateur est compilé une minute après les fichiers : c'est la version déjà installée.
        Assert.False(UpdateService.IsNewer(Setup("1.15.0", Installed.AddMinutes(1)), new Version(1, 15, 0), Installed, ""));
    }

    [Fact]
    public void Find_ReturnsNull_ForMissingFolder()
    {
        Assert.Null(UpdateService.Find(@"Z:\dossier\qui\n'existe\pas"));
        Assert.Null(UpdateService.Find(""));
    }
}
