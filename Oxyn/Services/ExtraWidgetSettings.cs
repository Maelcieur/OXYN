namespace Oxyn.Services;

/// <summary>Réglages des modules Horloge à cases et Températures (globaux : bureau et Switch).</summary>
public sealed class ExtraWidgetSettings : ObservableSettings
{
    private string _tileClockColor = "#FFF7931E";
    private bool _tileClockSeconds = true;
    private string _cpuCurveColor = "#FF00E0FF";
    private string _gpuCurveColor = "#FFFF5A36";


    /// <summary>Couleur des cases de l'horloge à cases (orange par défaut).</summary>
    public string TileClockColor { get => _tileClockColor; set => Set(ref _tileClockColor, value); }

    /// <summary>Affiche la case des secondes.</summary>
    public bool TileClockSeconds { get => _tileClockSeconds; set => Set(ref _tileClockSeconds, value); }

    /// <summary>Couleur de la courbe de température du processeur.</summary>
    public string CpuCurveColor { get => _cpuCurveColor; set => Set(ref _cpuCurveColor, value); }

    /// <summary>Couleur de la courbe de température de la carte graphique.</summary>
    public string GpuCurveColor { get => _gpuCurveColor; set => Set(ref _gpuCurveColor, value); }

    /// <summary>
    /// Courbes du module Températures (donnée, libellé, couleur) ; vide = processeur et carte graphique, avec les
    /// couleurs ci-dessus (réglage d'origine).
    /// </summary>
    public List<TemperatureSeries> TemperatureSeries { get; set; } = new();

    /// <summary>Courbes réellement affichées (celles d'origine si la liste est vide).</summary>
    public IReadOnlyList<TemperatureSeries> EffectiveTemperatureSeries() => TemperatureSeries is { Count: > 0 }
        ? TemperatureSeries
        : new[]
        {
            new TemperatureSeries { Source = SensorCatalog.CpuTemp, Label = "CPU", Color = CpuCurveColor },
            new TemperatureSeries { Source = SensorCatalog.GpuTemp, Label = "GPU", Color = GpuCurveColor }
        };

    private double _tempLineThickness = 2, _tempMin = 20, _tempMax = 100;
    private bool _tempFill = true;

    /// <summary>Épaisseur des courbes de températures, en pixels.</summary>
    public double TempLineThickness { get => _tempLineThickness; set => Set(ref _tempLineThickness, value); }
    /// <summary>Dégradé sous chaque courbe.</summary>
    public bool TempFill { get => _tempFill; set => Set(ref _tempFill, value); }
    /// <summary>Bas et haut de l'échelle, en °C.</summary>
    public double TempMin { get => _tempMin; set => Set(ref _tempMin, value); }
    public double TempMax { get => _tempMax; set => Set(ref _tempMax, value); }

    /// <summary>Liste des courbes modifiée (Paramètres) : le module se reconstruit.</summary>
    public void NotifySeriesChanged() => Raise(nameof(TemperatureSeries));
}

/// <summary>Une courbe du module Températures.</summary>
public sealed class TemperatureSeries
{
    /// <summary>Identifiant de la donnée (SensorReading.Id).</summary>
    public string Source { get; set; } = SensorCatalog.CpuTemp;
    public string Label { get; set; } = "CPU";
    public string Color { get; set; } = "#FF00E0FF";
}
