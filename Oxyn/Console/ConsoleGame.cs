using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Oxyn.ConsoleMode;

/// <summary>Un jeu de la bibliothèque du Mode console (ajouté à la main dans les Paramètres).</summary>
public sealed class ConsoleGame
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>Exécutable à lancer (vide si <see cref="LaunchUri"/> ou un émulateur est utilisé).</summary>
    public string ExePath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDir { get; set; } = "";

    /// <summary>Lancement par adresse, par exemple steam://rungameid/620.</summary>
    public string LaunchUri { get; set; } = "";

    /// <summary>Émulateur à utiliser (Id d'un <see cref="ConsoleEmulator"/>) ; vide pour un jeu PC.</summary>
    public string? EmulatorId { get; set; }
    /// <summary>Fichier de jeu (ROM, image disque) passé à l'émulateur.</summary>
    public string RomPath { get; set; } = "";
    /// <summary>Plateforme affichée en badge (« GameCube »...) ; reprise de l'émulateur si vide.</summary>
    public string Platform { get; set; } = "";
    /// <summary>Cœur RetroArch propre à ce jeu (remplace celui de l'émulateur).</summary>
    public string Core { get; set; } = "";

    /// <summary>Jaquette portrait 600x900 (fichier du cache console\art).</summary>
    public string CoverPath { get; set; } = "";
    /// <summary>Fond large affiché derrière l'interface.</summary>
    public string HeroPath { get; set; } = "";
    /// <summary>Logo transparent (facultatif).</summary>
    public string LogoPath { get; set; } = "";

    public string? SteamAppId { get; set; }
    public DateTime? LastPlayed { get; set; }
    /// <summary>Temps compté par OXYN (sessions lancées depuis la console).</summary>
    public TimeSpan PlayTime { get; set; }

    /// <summary>Identifiant Switch (16 chiffres hexadécimaux) : lu dans le nom du fichier ou appris au 1er lancement.</summary>
    public string? SwitchTitleId { get; set; }

    /// <summary>Vrai temps de jeu lu chez Steam / l'émulateur (null si inconnu), rempli par <see cref="PlayTimeSources.Apply"/>.</summary>
    [JsonIgnore]
    public TimeSpan? ExternalPlayTime { get; set; }

    /// <summary>Temps affiché : celui de Steam / de l'émulateur s'il est connu, sinon celui d'OXYN.</summary>
    [JsonIgnore]
    public TimeSpan ShownPlayTime => ExternalPlayTime is { } t && t > TimeSpan.Zero ? t : PlayTime;

    [JsonIgnore]
    public bool UsesUri => !string.IsNullOrWhiteSpace(LaunchUri);

    [JsonIgnore]
    public bool UsesEmulator => !string.IsNullOrWhiteSpace(EmulatorId);

    /// <summary>Identifiant Steam : celui enregistré, sinon celui de l'adresse steam://rungameid/xxx.</summary>
    [JsonIgnore]
    public string? EffectiveSteamAppId =>
        !string.IsNullOrWhiteSpace(SteamAppId) ? SteamAppId : ParseSteamAppId(LaunchUri);

    public static string? ParseSteamAppId(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        var m = Regex.Match(uri, @"^steam://(?:rungameid|run|launch)/(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
