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
}
