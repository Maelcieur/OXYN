using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Oxyn.Controls;

/// <summary>
/// Courbes de températures réunies sur un même graphe (échelle 20-100 °C, repères à 40, 60 et 80 °C) : autant de
/// courbes que de séries choisies, chacune de sa couleur ; une valeur absente coupe la courbe.
/// </summary>
public sealed class TemperatureChart : FrameworkElement
{
    public const int Capacity = 120;
    private double _min = 20, _max = 100, _thickness = 2;
    private bool _fill = true;

    /// <summary>Épaisseur des courbes, remplissage dessous, bas et haut de l'échelle (°C).</summary>
    public void SetLook(double thickness, bool fill, double min, double max)
    {
        _thickness = Math.Clamp(thickness, 0.5, 8);
        _fill = fill;
        _min = Math.Min(min, max - 10);
        _max = Math.Max(max, min + 10);
        InvalidateVisual();
    }

    /// <summary>Une ligne par mesure : une valeur par série (même ordre que <see cref="Colors"/>).</summary>
    private readonly List<double?[]> _points = new();
    private IReadOnlyList<Color> _colors = Array.Empty<Color>();

    /// <summary>Couleur de chaque série ; changer le nombre de séries vide l'historique.</summary>
    public IReadOnlyList<Color> Colors
    {
        get => _colors;
        set
        {
            if (value.Count != _colors.Count) _points.Clear();
            _colors = value.ToList();
            InvalidateVisual();
        }
    }

    public int Count => _points.Count;

    public void Push(double?[] values)
    {
        _points.Add(values);
        if (_points.Count > Capacity) _points.RemoveRange(0, _points.Count - Capacity);
        InvalidateVisual();
    }

    public void Load(IEnumerable<double?[]> history)
    {
        _points.Clear();
        _points.AddRange(history);
        if (_points.Count > Capacity) _points.RemoveRange(0, _points.Count - Capacity);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        const double left = 22; // place des graduations
        var plot = w - left;

        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), 1);
        grid.Freeze();
        var labelBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        labelBrush.Freeze();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // Trois repères régulièrement espacés dans l'échelle choisie (40, 60, 80 °C par défaut).
        var step = (_max - _min) / 4;
        foreach (var t in new[] { _min + step, _min + 2 * step, _min + 3 * step })
        {
            var y = Y(t, h);
            dc.DrawLine(grid, new Point(left, y), new Point(w, y));
            var text = new FormattedText(t.ToString("0", CultureInfo.InvariantCulture) + "°", CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, labelBrush, dpi);
            dc.DrawText(text, new Point(0, y - text.Height / 2));
        }

        if (_points.Count < 2) return;
        var dx = plot / (Capacity - 1);
        var offset = Capacity - _points.Count;
        // La première série est dessinée en dernier : elle reste au-dessus des autres.
        for (var s = _colors.Count - 1; s >= 0; s--)
        {
            var series = s;
            Series(dc, i => series < _points[i].Length ? _points[i][series] : null, _colors[s], left, dx, offset, h);
        }
    }

    private void Series(DrawingContext dc, Func<int, double?> value, Color color, double left, double dx, int offset, double h)
    {
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var g = line.Open())
        using (var a = area.Open())
        {
            var run = new List<Point>();
            void Flush()
            {
                if (run.Count >= 2)
                {
                    g.BeginFigure(run[0], false, false);
                    g.PolyLineTo(run.Skip(1).ToList(), true, true);
                    a.BeginFigure(new Point(run[0].X, h), true, true);
                    a.PolyLineTo(run.Append(new Point(run[^1].X, h)).ToList(), false, true);
                }
                run.Clear();
            }
            for (var i = 0; i < _points.Count; i++)
            {
                if (value(i) is { } t) run.Add(new Point(left + (offset + i) * dx, Y(t, h)));
                else Flush();
            }
            Flush();
        }
        line.Freeze();
        area.Freeze();
        var fill = new LinearGradientBrush(DrawingHelpers.WithAlpha(color, 0x50), DrawingHelpers.WithAlpha(color, 0x00), 90);
        fill.Freeze();
        if (_fill) dc.DrawGeometry(fill, null, area);
        var pen = new Pen(new SolidColorBrush(color), _thickness) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }

    private double Y(double t, double h) => 2 + (1 - (Math.Clamp(t, _min, _max) - _min) / (_max - _min)) * (h - 4);
}
