using System.IO;

namespace Oxyn.Infrastructure;

/// <summary>
/// Emplacements des données d'OXYN, tous sous %AppData%\Oxyn (ancien %AppData%\SysDeck déplacé au premier lancement).
/// La variable d'environnement OXYN_DATA_DIR (ou l'ancienne SYSDECK_DATA_DIR) permet de pointer ailleurs (tests, profil portable) sans toucher aux vraies données.
/// </summary>
public static class AppPaths
{
    public static string DataDir { get; } = ResolveDataDir();

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string ShortcutsFile => Path.Combine(DataDir, "shortcuts.json");
    public static string PowerUsageFile => Path.Combine(DataDir, "power-usage.json");
    public static string LogDir => Path.Combine(DataDir, "logs");

    /// <summary>Sous-dossier géré par OXYN (copies d'icônes, d'images de fond, de polices).</summary>
    public static string Folder(string name) => Path.Combine(DataDir, name);

    private static string ResolveDataDir()
    {
        var overridden = Environment.GetEnvironmentVariable("OXYN_DATA_DIR");
        if (string.IsNullOrWhiteSpace(overridden)) overridden = Environment.GetEnvironmentVariable("SYSDECK_DATA_DIR");   // ancien nom
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            var custom = Path.GetFullPath(overridden);
            Directory.CreateDirectory(custom);
            return custom;
        }
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Oxyn");
        MigrateLegacy(Path.Combine(appData, "SysDeck"), dir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Premier lancement après le changement de nom (SysDeck -> OXYN) : l'ancien dossier de données devient le nouveau,
    /// avec tout dedans (réglages, jeux, thèmes, triches, journaux). Déplacé d'un bloc ; s'il est verrouillé, copié.
    /// Rien n'est fait si le nouveau dossier existe déjà.
    /// </summary>
    internal static void MigrateLegacy(string oldDir, string newDir)
    {
        if (Directory.Exists(newDir) || !Directory.Exists(oldDir)) return;
        try
        {
            Directory.Move(oldDir, newDir);
        }
        catch
        {
            try { CopyDirectory(oldDir, newDir); } catch { }
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        foreach (var sub in Directory.GetDirectories(from))
            CopyDirectory(sub, Path.Combine(to, Path.GetFileName(sub)));
    }
}
