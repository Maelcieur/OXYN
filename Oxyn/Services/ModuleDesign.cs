namespace Oxyn.Services;

/// <summary>
/// Élément d'un module : une donnée affichée sous une forme (anneau, barre, courbe…), posée dans la carte.
/// Placement et taille se règlent dans l'aperçu de Personnaliser (grille et accroches).
/// </summary>
public sealed class BuilderBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>"Ring", "Bar", "Graph", "Value", "Big", "Header" ou "Text".</summary>
    public string Type { get; set; } = "Value";

    /// <summary>Identifiant de la donnée (SensorReading.Id) ; vide pour un en-tête ou un texte libre.</summary>
    public string? Source { get; set; }

    /// <summary>Données supplémentaires (ex. la 2e barrette de mémoire), combinées avec <see cref="Source"/>.</summary>
    public List<string>? MoreSources { get; set; }

    /// <summary>
    /// Calcul quand il y a plusieurs données : "Average" (moyenne), "Max", "Min", "Sum" ou "Each" (chacune affichée,
    /// « 32 / 33 °C », pour les lignes, chiffres et textes ; moyenne pour les jauges et courbes).
    /// </summary>
    public string Combine { get; set; } = "Average";

    /// <summary>Toutes les données de l'élément, dans l'ordre.</summary>
    public IEnumerable<string> AllSources()
    {
        if (!string.IsNullOrEmpty(Source)) yield return Source!;
        foreach (var s in MoreSources ?? new List<string>())
            if (!string.IsNullOrEmpty(s)) yield return s;
    }

    public static readonly IReadOnlyList<(string Key, string Label)> CombineLabels = new[]
    {
        ("Average", "Moyenne"), ("Max", "Maximum"), ("Min", "Minimum"), ("Sum", "Somme"), ("Each", "Chacune")
    };

    /// <summary>Libellé affiché ; vide = nom de la donnée.</summary>
    public string? Label { get; set; }

    /// <summary>En-tête : sous-titre (ex. nom du matériel) ; Texte : contenu, « {valeur} » remplacé par la donnée.</summary>
    public string? Text { get; set; }

    /// <summary>Position et taille dans la carte, en pixels du module.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 140;
    public double Height { get; set; } = 24;

    /// <summary>Maximum de l'échelle (anneau, barre, courbe) ; 0 = selon l'unité.</summary>
    public double Max { get; set; }

    /// <summary>Anneau : 270, 180 ou 360 degrés.</summary>
    public double Sweep { get; set; } = 270;

    /// <summary>Anneau et barre en segments.</summary>
    public bool Segmented { get; set; }

    /// <summary>Libellé en majuscules (style « TEMPERATURE »).</summary>
    public bool Uppercase { get; set; }

    /// <summary>Couleur propre (hexadécimal) ; null = dégradé du style.</summary>
    public string? Color { get; set; }

    /// <summary>Taille du texte principal du bloc.</summary>
    public double FontSize { get; set; } = 13;

    /// <summary>En-tête : symbole Segoe affiché avant le titre ; null = pastille de couleur, vide = rien.</summary>
    public string? Glyph { get; set; }

    /// <summary>Anneau : dessin ("Smooth", "Segments", "Thin", "Dots", "Needle", "Pie", "Neon", "Rays", "Double", "Capsule") ; null = style global.</summary>
    public string? Design { get; set; }

    /// <summary>Anneau : graduations ("Lines", "Dense", "Majors", "Dots", "Inner", "Numbers") ; null = aucune.</summary>
    public string? Ticks { get; set; }

    /// <summary>Anneau : épaisseur (multiplicateur, 1 = automatique).</summary>
    public double Thickness { get; set; } = 1;

    /// <summary>Libellé visible (anneau, barre, courbe, grand chiffre).</summary>
    public bool ShowLabel { get; set; } = true;

    /// <summary>Courbe : surface remplie sous la ligne.</summary>
    public bool Fill { get; set; } = true;

    /// <summary>Courbe : durée de l'historique affiché, en secondes.</summary>
    public int HistorySeconds { get; set; } = 60;

    public BuilderBlock Copy()
    {
        var copy = (BuilderBlock)MemberwiseClone();
        copy.MoreSources = MoreSources?.ToList();
        return copy;
    }

    public static readonly IReadOnlyDictionary<string, string> TypeLabels = new Dictionary<string, string>
    {
        ["Ring"] = "Anneau", ["Bar"] = "Barre", ["Graph"] = "Courbe", ["Value"] = "Ligne de valeur",
        ["Big"] = "Grand chiffre", ["Header"] = "Titre", ["Text"] = "Texte"
    };

    /// <summary>Taille de départ d'un nouvel élément selon sa forme.</summary>
    public static (double W, double H) DefaultSize(string type) => type switch
    {
        "Ring" => (110, 110),
        "Bar" => (180, 30),
        "Graph" => (200, 70),
        "Big" => (110, 64),
        "Header" => (260, 26),
        _ => (180, 22)
    };
}

/// <summary>
/// Disposition d'un module : « Classic » garde le contenu d'origine (les éléments ajoutés se posent par-dessus),
/// les autres remplacent le contenu par leurs éléments, dans une carte de taille Width × Height.
/// </summary>
public sealed class ModuleDesign
{
    public const string Classic = "Classic";

    public string Layout { get; set; } = Classic;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 180;
    public List<BuilderBlock> Blocks { get; set; } = new();

    public bool IsClassic => Layout == Classic;

    /// <summary>Rien à dessiner : disposition d'origine sans élément ajouté.</summary>
    public bool IsEmpty => IsClassic && (Blocks is null || Blocks.Count == 0);
}

/// <summary>Données ajoutées par OXYN au catalogue des capteurs (réseau, disques, FPS).</summary>
public static class BuilderSources
{
    public const string NetDown = "oxyn/net/down", NetUp = "oxyn/net/up", GpuFps = "oxyn/gpu/fps";

    public static string DiskUsage(string drive) => $"oxyn/disk/{drive}/usage";
    public static string DiskFree(string drive) => $"oxyn/disk/{drive}/free";

    public static IEnumerable<SensorReading> Network(double downBytes, double upBytes) => new[]
    {
        new SensorReading(NetDown, "Réseau", "Réception", "Mo/s", (float)(downBytes / 1_048_576)),
        new SensorReading(NetUp, "Réseau", "Envoi", "Mo/s", (float)(upBytes / 1_048_576))
    };

    public static IEnumerable<SensorReading> Disks(IEnumerable<DriveUsage> drives)
    {
        foreach (var d in drives)
        {
            var name = d.Name.TrimEnd('\\', ':');
            yield return new SensorReading(DiskUsage(name), "Disques", $"Occupation {name}:", "%", d.LoadPercent);
            yield return new SensorReading(DiskFree(name), "Disques", $"Libre {name}:", "Go", Math.Max(0, d.TotalGb - d.UsedGb));
        }
    }

    public static SensorReading Fps(float fps) => new(GpuFps, "Carte graphique", "Images par seconde", "FPS", fps);

    /// <summary>Groupe de données mis en tête de liste pour un module (les autres restent disponibles).</summary>
    public static string? GroupOf(string moduleId) => moduleId switch
    {
        "cpu" => "Processeur",
        "gpu" => "Carte graphique",
        "ram" => "Mémoire",
        "disk" => "Disques",
        "network" => "Réseau",
        _ => null
    };
}

/// <summary>Disposition prête : point de départ, modifiable ensuite élément par élément.</summary>
public sealed record DesignPreset(string Id, string Name, string Description, Func<ModuleDesign> Create);

/// <summary>Les 5 dispositions de chaque module matériel (la première est toujours la disposition d'origine).</summary>
public static class DesignPresets
{
    private static BuilderBlock B(string type, string? source, double x, double y, double w, double h, Action<BuilderBlock>? set = null)
    {
        var b = new BuilderBlock { Type = type, Source = source, X = x, Y = y, Width = w, Height = h };
        set?.Invoke(b);
        return b;
    }

    private static readonly DesignPreset ClassicPreset =
        new(ModuleDesign.Classic, "Classique", "La disposition d'origine du module ; tu peux y ajouter des éléments.", () => new ModuleDesign());

    private static ModuleDesign D(string layout, double w, double h, params BuilderBlock[] blocks)
        => new() { Layout = layout, Width = w, Height = h, Blocks = blocks.ToList() };

    /// <summary>Dispositions d'un module ; « Classique » seule pour un module sans données matérielles.</summary>
    public static IReadOnlyList<DesignPreset> For(string moduleId) => moduleId switch
    {
        "gpu" => Hardware("Carte graphique", "GPU", "", SensorCatalog.GpuLoad, SensorCatalog.GpuTemp,
            (SensorCatalog.GpuPower, "Puissance"), (SensorCatalog.GpuClock, "Fréquence"), (SensorCatalog.VramUsed, "VRAM"),
            (BuilderSources.GpuFps, "FPS")),
        "cpu" => Hardware("Processeur", "CPU", "", SensorCatalog.CpuLoad, SensorCatalog.CpuTemp,
            (SensorCatalog.CpuClock, "Fréquence"), (SensorCatalog.CpuPower, "Puissance"), (SensorCatalog.RamLoad, "RAM"),
            (SensorCatalog.GpuLoad, "GPU")),
        "ram" => Hardware("Mémoire", "RAM", "", SensorCatalog.RamLoad, SensorCatalog.RamUsed,
            (SensorCatalog.RamUsed, "Utilisée"), (SensorCatalog.VramUsed, "VRAM"), (SensorCatalog.CpuLoad, "Processeur"),
            (SensorCatalog.GpuLoad, "Carte graphique")),
        "disk" => new[]
        {
            ClassicPreset,
            new DesignPreset("Sheet", "Fiche technique", "Occupation de chaque disque en lignes.", () => D("Sheet", 300, 130,
                B("Header", null, 12, 8, 276, 24, b => { b.Label = "Stockage"; b.Glyph = ""; }),
                B("Value", BuilderSources.DiskUsage("C"), 12, 44, 276, 22, b => { b.Label = "Disque C:"; b.Uppercase = true; }),
                B("Value", BuilderSources.DiskFree("C"), 12, 72, 276, 22, b => { b.Label = "Libre sur C:"; b.Uppercase = true; }),
                B("Value", BuilderSources.DiskUsage("D"), 12, 100, 276, 22, b => { b.Label = "Disque D:"; b.Uppercase = true; }))),
            new DesignPreset("Dashboard", "Tableau de bord", "Une barre par disque avec son pourcentage.", () => D("Dashboard", 300, 150,
                B("Header", null, 12, 8, 276, 24, b => { b.Label = "Stockage"; b.Glyph = ""; }),
                B("Bar", BuilderSources.DiskUsage("C"), 12, 42, 276, 30, b => b.Label = "C:"),
                B("Bar", BuilderSources.DiskUsage("D"), 12, 78, 276, 30, b => b.Label = "D:"),
                B("Bar", BuilderSources.DiskUsage("E"), 12, 114, 276, 30, b => b.Label = "E:"))),
            new DesignPreset("Compact", "Compacte", "Grands chiffres, sans jauge.", () => D("Compact", 260, 90,
                B("Big", BuilderSources.DiskUsage("C"), 10, 10, 110, 70, b => { b.Label = "C:"; b.FontSize = 15; }),
                B("Big", BuilderSources.DiskUsage("D"), 140, 10, 110, 70, b => { b.Label = "D:"; b.FontSize = 15; }))),
            new DesignPreset("Graphs", "Courbes", "Anneau d'occupation et place libre.", () => D("Graphs", 300, 140,
                B("Ring", BuilderSources.DiskUsage("C"), 12, 12, 116, 116, b => b.Label = "C:"),
                B("Ring", BuilderSources.DiskUsage("D"), 150, 12, 116, 116, b => b.Label = "D:")))
        },
        "network" => new[]
        {
            ClassicPreset,
            new DesignPreset("Sheet", "Fiche technique", "Débits en lignes de valeurs.", () => D("Sheet", 260, 90,
                B("Header", null, 12, 8, 236, 24, b => { b.Label = "Réseau"; b.Glyph = ""; }),
                B("Value", BuilderSources.NetUp, 12, 40, 236, 22, b => { b.Label = "Envoi"; b.Uppercase = true; }),
                B("Value", BuilderSources.NetDown, 12, 64, 236, 22, b => { b.Label = "Réception"; b.Uppercase = true; }))),
            new DesignPreset("Dashboard", "Tableau de bord", "Débits en barres.", () => D("Dashboard", 260, 110,
                B("Header", null, 12, 8, 236, 24, b => { b.Label = "Réseau"; b.Glyph = ""; }),
                B("Bar", BuilderSources.NetUp, 12, 40, 236, 30, b => { b.Label = "Envoi"; b.Max = 20; }),
                B("Bar", BuilderSources.NetDown, 12, 76, 236, 30, b => { b.Label = "Réception"; b.Max = 120; }))),
            new DesignPreset("Compact", "Compacte", "Deux grands chiffres.", () => D("Compact", 240, 90,
                B("Big", BuilderSources.NetUp, 10, 10, 105, 70, b => { b.Label = "Envoi"; b.FontSize = 14; }),
                B("Big", BuilderSources.NetDown, 125, 10, 105, 70, b => { b.Label = "Réception"; b.FontSize = 14; }))),
            new DesignPreset("Graphs", "Courbes", "Deux courbes empilées, comme un moniteur.", () => D("Graphs", 220, 210,
                B("Header", null, 12, 8, 196, 24, b => { b.Label = "Réseau"; b.Glyph = ""; }),
                B("Graph", BuilderSources.NetUp, 12, 40, 196, 78, b => b.Label = "Envoi"),
                B("Graph", BuilderSources.NetDown, 12, 124, 196, 78, b => b.Label = "Réception")))
        },
        _ => new[] { ClassicPreset }
    };

    /// <summary>Les 4 dispositions communes aux modules matériels (après « Classique »).</summary>
    private static DesignPreset[] Hardware(string title, string shortTitle, string glyph, string main, string temp,
        params (string Source, string Label)[] more)
    {
        var lines = new[] { (temp, "Température") }.Concat(more.Take(3)).ToArray();
        return new[]
        {
            ClassicPreset,
            new DesignPreset("Sheet", "Fiche technique", "Grand anneau à gauche, lignes « TEMPÉRATURE … 46 °C » à droite.", () =>
            {
                var blocks = new List<BuilderBlock>
                {
                    B("Header", null, 14, 10, 302, 24, b => { b.Label = shortTitle; b.Text = title; b.Glyph = null; }),
                    B("Ring", main, 14, 44, 112, 112, b => { b.Label = "Charge"; b.Sweep = 360; })
                };
                for (var i = 0; i < lines.Length; i++)
                {
                    var (source, label) = lines[i];
                    blocks.Add(B("Value", source, 142, 50 + i * 28, 174, 22, b => { b.Label = label; b.Uppercase = true; }));
                }
                return D("Sheet", 330, 170, blocks.ToArray());
            }),
            new DesignPreset("Dashboard", "Tableau de bord", "Jauge en segments et barres avec leurs valeurs.", () =>
            {
                var bars = new[] { (temp, "Température") }.Concat(more).Take(5).ToArray();
                var blocks = new List<BuilderBlock>
                {
                    B("Header", null, 14, 10, 340, 24, b => { b.Label = shortTitle; b.Glyph = glyph; }),
                    B("Ring", main, 14, 52, 130, 130, b => { b.Label = "Charge"; b.Segmented = true; })
                };
                for (var i = 0; i < bars.Length; i++)
                {
                    var (source, label) = bars[i];
                    blocks.Add(B("Bar", source, 160, 44 + i * 34, 196, 28, b => b.Label = label));
                }
                return D("Dashboard", 370, 44 + bars.Length * 34 + 14, blocks.ToArray());
            }),
            new DesignPreset("Compact", "Compacte", "Grands chiffres côte à côte, sans jauge.", () => D("Compact", 330, 96,
                B("Big", main, 10, 14, 100, 70, b => { b.Label = "Charge"; b.FontSize = 15; }),
                B("Big", temp, 115, 14, 100, 70, b => { b.Label = "Température"; b.FontSize = 15; }),
                B("Big", more[0].Source, 220, 14, 100, 70, b => { b.Label = more[0].Label; b.FontSize = 15; }))),
            new DesignPreset("Graphs", "Courbes", "Courbes animées de la charge et de la température.", () => D("Graphs", 300, 200,
                B("Header", null, 12, 8, 276, 24, b => { b.Label = title; b.Glyph = glyph; }),
                B("Graph", main, 12, 40, 276, 70, b => { b.Label = "Charge"; b.Max = 100; }),
                B("Graph", temp, 12, 118, 276, 70, b => { b.Label = "Température"; b.Max = 100; })))
        };
    }
}
