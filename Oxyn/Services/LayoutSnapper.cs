using System.Windows;

namespace Oxyn.Services;

/// <summary>Réglages du magnétisme : grille (pas, en unités du rectangle) et guides d'alignement.</summary>
public sealed record SnapOptions(double GridSize, bool SnapToGrid, bool SnapToGuides, double Threshold)
{
    public static SnapOptions Default => new(16, true, true, 8);
}

/// <summary>Ligne de guide à dessiner : verticale (x = Position, de From à To) ou horizontale (y = Position).</summary>
public readonly record struct SnapGuide(bool Vertical, double Position, double From, double To);

/// <summary>Résultat : rectangle corrigé et guides des alignements trouvés.</summary>
public sealed record SnapResult(Rect Rect, IReadOnlyList<SnapGuide> Guides);

/// <summary>
/// Moteur de magnétisme commun (bureau, éléments d'un module, écran de la Switch), sans dépendance à l'affichage :
/// un rectangle déplacé se colle aux bords et aux centres des autres rectangles et de la zone (guides), sinon à la
/// grille. Chaque axe est traité indépendamment ; le guide le plus proche sous le seuil l'emporte.
/// </summary>
public static class LayoutSnapper
{
    public static SnapResult Snap(Rect moving, IEnumerable<Rect> others, Rect area, SnapOptions options)
    {
        var targets = others.Where(r => !r.IsEmpty && r.Width > 0 && r.Height > 0).ToList();
        var guides = new List<SnapGuide>();
        double dx = 0, dy = 0;
        bool snappedX = false, snappedY = false;

        if (options.SnapToGuides)
        {
            // Cibles : bords et centre de la zone, puis des autres rectangles.
            var xTargets = new List<(double Value, Rect Source)>
            {
                (area.Left, area), (area.Left + area.Width / 2, area), (area.Right, area)
            };
            var yTargets = new List<(double Value, Rect Source)>
            {
                (area.Top, area), (area.Top + area.Height / 2, area), (area.Bottom, area)
            };
            foreach (var r in targets)
            {
                xTargets.Add((r.Left, r)); xTargets.Add((r.Left + r.Width / 2, r)); xTargets.Add((r.Right, r));
                yTargets.Add((r.Top, r)); yTargets.Add((r.Top + r.Height / 2, r)); yTargets.Add((r.Bottom, r));
            }

            var xs = new[] { moving.Left, moving.Left + moving.Width / 2, moving.Right };
            var ys = new[] { moving.Top, moving.Top + moving.Height / 2, moving.Bottom };

            if (Best(xs, xTargets, options.Threshold) is { } bx)
            {
                dx = bx.Delta; snappedX = true;
            }
            if (Best(ys, yTargets, options.Threshold) is { } by)
            {
                dy = by.Delta; snappedY = true;
            }

            var snapped = new Rect(moving.X + dx, moving.Y + dy, moving.Width, moving.Height);
            if (snappedX) AddGuides(guides, vertical: true, snapped, xTargets);
            if (snappedY) AddGuides(guides, vertical: false, snapped, yTargets);
        }

        if (options.SnapToGrid && options.GridSize >= 2)
        {
            var g = options.GridSize;
            if (!snappedX) dx = Math.Round((moving.Left - area.Left) / g) * g + area.Left - moving.Left;
            if (!snappedY) dy = Math.Round((moving.Top - area.Top) / g) * g + area.Top - moving.Top;
        }

        return new SnapResult(new Rect(moving.X + dx, moving.Y + dy, moving.Width, moving.Height), guides);
    }

    private static (double Delta, double Target)? Best(double[] edges, List<(double Value, Rect Source)> targets, double threshold)
    {
        (double Delta, double Target)? best = null;
        foreach (var edge in edges)
            foreach (var (value, _) in targets)
            {
                var d = value - edge;
                if (Math.Abs(d) > threshold) continue;
                if (best is null || Math.Abs(d) < Math.Abs(best.Value.Delta)) best = (d, value);
            }
        return best;
    }

    /// <summary>Un guide par alignement exact entre le rectangle corrigé et une cible, sur toute l'étendue des deux.</summary>
    private static void AddGuides(List<SnapGuide> guides, bool vertical, Rect snapped, List<(double Value, Rect Source)> targets)
    {
        var edges = vertical
            ? new[] { snapped.Left, snapped.Left + snapped.Width / 2, snapped.Right }
            : new[] { snapped.Top, snapped.Top + snapped.Height / 2, snapped.Bottom };
        foreach (var (value, source) in targets)
        {
            if (!edges.Any(e => Math.Abs(e - value) < 0.5)) continue;
            var from = vertical ? Math.Min(snapped.Top, source.Top) : Math.Min(snapped.Left, source.Left);
            var to = vertical ? Math.Max(snapped.Bottom, source.Bottom) : Math.Max(snapped.Right, source.Right);
            var guide = new SnapGuide(vertical, value, from, to);
            if (!guides.Contains(guide)) guides.Add(guide);
        }
    }
}
