using System.Windows;
using Oxyn.Services;
using Xunit;

namespace Oxyn.Tests;

public class LayoutSnapperTests
{
    private static readonly Rect Area = new(0, 0, 1920, 1080);

    [Fact]
    public void Grid_RoundsTopLeft_WhenNoGuideIsClose()
    {
        var r = LayoutSnapper.Snap(new Rect(333, 517, 200, 100), Array.Empty<Rect>(), Area,
            new SnapOptions(16, SnapToGrid: true, SnapToGuides: false, Threshold: 8));
        Assert.Equal(336, r.Rect.X);
        Assert.Equal(512, r.Rect.Y);
        Assert.Empty(r.Guides);
    }

    [Fact]
    public void Guides_AlignLeftEdgeWithAnotherModule_AndReportAGuide()
    {
        var other = new Rect(400, 100, 300, 200);
        var r = LayoutSnapper.Snap(new Rect(405, 600, 200, 100), new[] { other }, Area, SnapOptions.Default);

        Assert.Equal(400, r.Rect.X);                    // bord gauche aligné
        var guide = Assert.Single(r.Guides, g => g.Vertical);
        Assert.Equal(400, guide.Position);
        Assert.Equal(100, guide.From);                  // de l'autre module…
        Assert.Equal(700, guide.To);                    // …jusqu'au module déplacé
    }

    [Fact]
    public void Guides_CenterOnScreen()
    {
        // centre du module à 962 : 2 px du centre de l'écran (960)
        var r = LayoutSnapper.Snap(new Rect(862, 300, 200, 100), Array.Empty<Rect>(), Area,
            new SnapOptions(16, SnapToGrid: false, SnapToGuides: true, Threshold: 8));
        Assert.Equal(860, r.Rect.X);
        Assert.Contains(r.Guides, g => g.Vertical && g.Position == 960);
    }

    [Fact]
    public void Guides_TakePriorityOverGrid_PerAxis()
    {
        var other = new Rect(100, 100, 300, 300);
        // X : bord droit à 403 près du bord droit de l'autre (400) ; Y : rien de proche → grille
        var r = LayoutSnapper.Snap(new Rect(203, 777, 200, 50), new[] { other }, Area, SnapOptions.Default);
        Assert.Equal(200, r.Rect.X);
        Assert.Equal(784, r.Rect.Y);
    }

    [Fact]
    public void ClosestTargetWins()
    {
        var a = new Rect(500, 0, 100, 100);
        var b = new Rect(503, 200, 100, 100);
        var r = LayoutSnapper.Snap(new Rect(504, 600, 50, 50), new[] { a, b }, Area,
            new SnapOptions(16, false, true, 8));
        Assert.Equal(503, r.Rect.X);
    }

    [Fact]
    public void NothingEnabled_LeavesTheRectAlone()
    {
        var moving = new Rect(333, 517, 200, 100);
        var r = LayoutSnapper.Snap(moving, new[] { new Rect(330, 0, 10, 10) }, Area, new SnapOptions(16, false, false, 8));
        Assert.Equal(moving, r.Rect);
    }
}
