using System.Diagnostics;
using System.IO;
using Oxyn.Infrastructure;

namespace Oxyn.Services;

/// <summary>Mises à jour sans GitHub : un dossier (local ou réseau) où l'on dépose le nouvel installateur.</summary>
public sealed class UpdateSettings
{
    /// <summary>Dossier surveillé (vide = dossier de compilation de l'installateur s'il existe sur ce PC).</summary>
    public string Folder { get; set; } = "";

    /// <summary>Cherche une mise à jour au démarrage d'OXYN et prévient par une bulle.</summary>
    public bool AutoCheck { get; set; } = true;

    /// <summary>Empreinte du dernier installateur lancé par la mise à jour (évite de reproposer le même).</summary>
    public string LastInstalledStamp { get; set; } = "";
}

/// <summary>Installateur trouvé dans le dossier des mises à jour.</summary>
public sealed record UpdateInfo(string SetupPath, Version Version, DateTime BuiltUtc, string Stamp);

/// <summary>
/// Cherche un installateur plus récent dans le dossier des mises à jour et l'installe PAR-DESSUS l'application
/// (même identifiant Inno Setup : pas de désinstallation, réglages et données gardés), en silence, puis l'installateur
/// relance OXYN. Version plus haute = mise à jour ; même version mais installateur refait depuis = recompilation.
/// </summary>
public static class UpdateService
{
    public const string SetupName = "OxynSetup.exe";

    /// <summary>Version d'OXYN en cours (Major.Minor.Build).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Dossier par défaut : celui où l'installateur est compilé sur ce PC, s'il existe.</summary>
    public static string DefaultFolder
    {
        get
        {
            var dev = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "testia", "Oxyn", "installer", "output");
            return Directory.Exists(dev) ? dev : "";
        }
    }

    public static string FolderOf(UpdateSettings s) => string.IsNullOrWhiteSpace(s.Folder) ? DefaultFolder : s.Folder.Trim();

    /// <summary>Installateur du dossier (OxynSetup.exe, sinon le plus récent *Setup*.exe), ou null.</summary>
    public static UpdateInfo? Find(string folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
            var path = Path.Combine(folder, SetupName);
            if (!File.Exists(path))
                path = new DirectoryInfo(folder).GetFiles("*Setup*.exe").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName ?? "";
            if (!File.Exists(path)) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!TryParse(info.ProductVersion, out var version) && !TryParse(info.FileVersion, out version)) return null;
            var file = new FileInfo(path);
            return new UpdateInfo(path, version, file.LastWriteTimeUtc, $"{version}|{file.LastWriteTimeUtc:O}|{file.Length}");
        }
        catch (Exception ex)
        {
            Log.WarnOnce("update-find", "Mise à jour : lecture du dossier impossible", ex);
            return null;
        }
    }

    /// <summary>
    /// Vrai si <paramref name="u"/> est plus récent que la version installée : version plus haute, ou même version
    /// mais installateur refait nettement après les fichiers installés (et pas déjà installé par cette fonction).
    /// </summary>
    public static bool IsNewer(UpdateInfo u, Version current, DateTime installedUtc, string lastInstalledStamp)
    {
        var v = new Version(u.Version.Major, u.Version.Minor, Math.Max(0, u.Version.Build));
        if (v > current) return true;
        if (v < current || u.Stamp == lastInstalledStamp) return false;
        // L'installateur garde la date des fichiers compilés : un installateur refait 10 min après eux est une nouvelle compilation.
        return u.BuiltUtc > installedUtc + TimeSpan.FromMinutes(10);
    }

    /// <summary>Date des fichiers d'OXYN installés (Oxyn.dll).</summary>
    public static DateTime InstalledUtc
    {
        get
        {
            try { return File.GetLastWriteTimeUtc(typeof(UpdateService).Assembly.Location); }
            catch { return DateTime.MinValue; }
        }
    }

    /// <summary>Mise à jour disponible dans le dossier réglé, sinon null.</summary>
    public static UpdateInfo? Available(UpdateSettings s)
    {
        var u = Find(FolderOf(s));
        return u != null && IsNewer(u, CurrentVersion, InstalledUtc, s.LastInstalledStamp) ? u : null;
    }

    /// <summary>
    /// Copie l'installateur à part (le dossier source reste libre) et le lance en silence. L'installateur ferme
    /// OXYN, remplace les fichiers et le relance. Vrai si le lancement a réussi : l'appelant doit alors quitter.
    /// </summary>
    public static bool StartInstall(UpdateInfo u)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "OxynUpdate");
            Directory.CreateDirectory(dir);
            var copy = Path.Combine(dir, SetupName);
            File.Copy(u.SetupPath, copy, true);
            Log.Info($"Mise à jour : installation de la version {u.Version} ({u.SetupPath}).");
            Process.Start(new ProcessStartInfo(copy, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Mise à jour : lancement de l'installateur impossible", ex);
            return false;
        }
    }

    private static bool TryParse(string? s, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(s)) return false;
        var clean = new string(s.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        if (!Version.TryParse(clean.Contains('.') ? clean : clean + ".0", out var v)) return false;
        version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        return true;
    }
}
