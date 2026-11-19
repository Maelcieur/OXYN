using System.IO;
using Oxyn.Infrastructure;
using Xunit;

namespace Oxyn.Tests;

/// <summary>Changement de nom SysDeck -> OXYN : l'ancien dossier de données est repris tel quel.</summary>
public class RenameMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"oxyn-migration-{Guid.NewGuid():N}");

    public RenameMigrationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void LegacyFolder_IsMovedWithEverythingInside()
    {
        var oldDir = Path.Combine(_root, "SysDeck");
        var newDir = Path.Combine(_root, "Oxyn");
        Directory.CreateDirectory(Path.Combine(oldDir, "logs"));
        File.WriteAllText(Path.Combine(oldDir, "config.json"), "{\"a\":1}");
        File.WriteAllText(Path.Combine(oldDir, "logs", "sysdeck.log"), "x");

        AppPaths.MigrateLegacy(oldDir, newDir);

        Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(newDir, "config.json")));
        Assert.True(File.Exists(Path.Combine(newDir, "logs", "sysdeck.log")));
        Assert.False(Directory.Exists(oldDir));
    }

    [Fact]
    public void ExistingNewFolder_IsNeverOverwritten()
    {
        var oldDir = Path.Combine(_root, "SysDeck");
        var newDir = Path.Combine(_root, "Oxyn");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);
        File.WriteAllText(Path.Combine(oldDir, "config.json"), "ancien");
        File.WriteAllText(Path.Combine(newDir, "config.json"), "nouveau");

        AppPaths.MigrateLegacy(oldDir, newDir);

        Assert.Equal("nouveau", File.ReadAllText(Path.Combine(newDir, "config.json")));
        Assert.True(Directory.Exists(oldDir));
    }

    [Fact]
    public void NoLegacyFolder_DoesNothing()
    {
        var newDir = Path.Combine(_root, "Oxyn");
        AppPaths.MigrateLegacy(Path.Combine(_root, "absent"), newDir);
        Assert.False(Directory.Exists(newDir));
    }
}
