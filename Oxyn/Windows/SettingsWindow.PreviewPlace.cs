using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Oxyn.Controls;
using Oxyn.Services;

namespace Oxyn;

// Personnaliser › aperçu du module › « Placer les éléments » : chaque élément se glisse dans l'aperçu, avec une grille
// et des guides d'alignement (bords et centres des autres éléments et de la carte), comme sur le bureau. Molette :
// taille de l'élément ; Alt : placement libre. Le déplacement est converti dans le repère de l'élément (ElementLayout).
public partial class SettingsWindow
{
    private sealed class PlaceDrag
    {
        public required ModulePreviewPart Part { get; init; }
        public required FrameworkElement Hotspot { get; init; }
        public required FrameworkElement Element { get; init; }
        public required Point Start { get; init; }
        public required double StartX { get; init; }
        public required double StartY { get; init; }
        public bool Moved { get; set; }
    }

    private ModuleCard? _placeCard;
    private readonly List<ModulePreviewPart> _placeParts = new();
    private PlaceDrag? _placeDrag;
    private bool _placeOptionsLoading;

    /// <summary>Placement direct dans l'aperçu : toujours actif quand le module a une apparence (sauf la barre de recherche).</summary>
    private bool PlacingElements => CurrentAppearance is not null && SelectedModule?.Id != "searchbar";

    /// <summary>Grille et accroches de l'aperçu, lues une fois depuis la configuration.</summary>
    private void InitPreviewPlace()
    {
        _placeOptionsLoading = true;
        var size = _widgetManager.Config.ElementGridSize;
        PreviewGridCombo.SelectedItem = PreviewGridCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == size.ToString()) ?? PreviewGridCombo.Items[2];
        PreviewGuidesCheck.IsChecked = _widgetManager.Config.ElementGuides;
        _placeOptionsLoading = false;
    }

    private void PreviewGrid_Changed(object sender, RoutedEventArgs e)
    {
        if (_placeOptionsLoading || !IsLoaded || PreviewGridCombo.SelectedItem is not ComboBoxItem item) return;
        _widgetManager.Config.ElementGridSize = int.Parse((string)item.Tag);
        _widgetManager.Config.ElementGuides = PreviewGuidesCheck.IsChecked == true;
        ScheduleSave();
        RefreshPreviewElements();
    }

    /// <summary>Appelé par RefreshPreviewElements en mode placement : grille et zones glissables des éléments.</summary>
    private void BuildPlaceHotspots(ModuleCard card, IReadOnlyList<ModulePreviewPart> parts)
    {
        _placeCard = card;
        _placeParts.Clear();
        PreviewWidgetGuides.Children.Clear();
        DrawPreviewGrid(card.ActualWidth, card.ActualHeight);

        foreach (var part in parts.Where(p => p.Section == "elements"))
        {
            var bounds = part.Bounds;
            bounds.Intersect(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
            if (bounds.IsEmpty || bounds.Width < 2 || bounds.Height < 2) continue;
            _placeParts.Add(part with { Bounds = bounds });

            var selected = _selectedPreviewPart is { } current && current.Key == part.Key && current.Section == part.Section;
            var hotspot = new Border
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Background = new SolidColorBrush(Color.FromArgb(selected ? (byte)40 : (byte)18, 0x5B, 0x8D, 0xEF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(selected ? (byte)255 : (byte)150, 0x5B, 0x8D, 0xEF)),
                BorderThickness = new Thickness(selected ? 1.5 : 1),
                Cursor = System.Windows.Input.Cursors.SizeAll,
                Tag = part,
                ToolTip = $"{part.Label} : glisser pour déplacer, molette pour la taille, clic pour ses réglages"
            };
            hotspot.MouseLeftButtonDown += PlaceHotspot_MouseDown;
            hotspot.MouseMove += PlaceHotspot_MouseMove;
            hotspot.MouseLeftButtonUp += PlaceHotspot_MouseUp;
            hotspot.MouseWheel += PlaceHotspot_MouseWheel;
            PreviewWidgetParts.Children.Add(hotspot);
            Canvas.SetLeft(hotspot, bounds.X);
            Canvas.SetTop(hotspot, bounds.Y);
        }
    }

    private void DrawPreviewGrid(double width, double height)
    {
        PreviewWidgetGrid.Children.Clear();
        PreviewWidgetGrid.Width = width;
        PreviewWidgetGrid.Height = height;
        var step = _widgetManager.Config.ElementGridSize;
        if (step >= 2)
        {
            var minor = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
            var major = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
            var i = 0;
            for (double x = 0; x <= width; x += step, i++)
                PreviewWidgetGrid.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = height, Stroke = i % 4 == 0 ? major : minor, StrokeThickness = 0.5 });
            i = 0;
            for (double y = 0; y <= height; y += step, i++)
                PreviewWidgetGrid.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = i % 4 == 0 ? major : minor, StrokeThickness = 0.5 });
        }
        // Axes du centre de la carte, toujours visibles en mode placement : repères pour centrer.
        var axis = new SolidColorBrush(Color.FromArgb(110, 0x5B, 0x8D, 0xEF));
        PreviewWidgetGrid.Children.Add(new Line { X1 = width / 2, X2 = width / 2, Y1 = 0, Y2 = height, Stroke = axis, StrokeThickness = 0.75, StrokeDashArray = new DoubleCollection { 4, 3 } });
        PreviewWidgetGrid.Children.Add(new Line { X1 = 0, X2 = width, Y1 = height / 2, Y2 = height / 2, Stroke = axis, StrokeThickness = 0.75, StrokeDashArray = new DoubleCollection { 4, 3 } });
    }

    private FrameworkElement? ElementOf(string key)
        => _placeCard?.MovableElements.FirstOrDefault(e => Movable.GetKey(e) == key);

    private void PlaceHotspot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: ModulePreviewPart part } hotspot || CurrentAppearance is not { } appearance) return;
        if (ElementOf(part.Key) is not { } element) return;
        var layout = appearance.GetElement(part.Key);
        _placeDrag = new PlaceDrag
        {
            Part = part, Hotspot = hotspot, Element = element,
            Start = e.GetPosition(PreviewWidgetParts), StartX = layout.X, StartY = layout.Y
        };
        hotspot.CaptureMouse();
        e.Handled = true;
    }

    private void PlaceHotspot_MouseMove(object sender, MouseEventArgs e)
    {
        if (_placeDrag is not { } drag || _placeCard is not { } card || CurrentAppearance is not { } appearance) return;
        var delta = e.GetPosition(PreviewWidgetParts) - drag.Start;
        if (!drag.Moved && Math.Abs(delta.X) < 2 && Math.Abs(delta.Y) < 2) return;
        drag.Moved = true;

        var start = _placeParts.First(p => p.Key == drag.Part.Key).Bounds;
        var moving = new Rect(start.X + delta.X, start.Y + delta.Y, start.Width, start.Height);
        var free = (GetAsyncKeyState(0x12) & 0x8000) != 0; // Alt
        var grid = _widgetManager.Config.ElementGridSize;
        var options = new SnapOptions(grid, grid >= 2 && !free, _widgetManager.Config.ElementGuides && !free, 4);
        var others = _placeParts.Where(p => p.Key != drag.Part.Key).Select(p => p.Bounds);
        var result = LayoutSnapper.Snap(moving, others, new Rect(0, 0, card.ActualWidth, card.ActualHeight), options);

        // Décalage dans le repère du parent de l'élément (mises à l'échelle du contenu de la carte).
        if (VisualTreeHelper.GetParent(drag.Element) is not Visual parent || card.TransformToDescendant(parent) is not { } toParent) return;
        var snapped = new Vector(result.Rect.X - start.X, result.Rect.Y - start.Y);
        var local = toParent.Transform(new Point(snapped.X, snapped.Y)) - toParent.Transform(new Point(0, 0));
        var layout = appearance.GetElement(drag.Part.Key);
        layout.X = Math.Round(drag.StartX + local.X);
        layout.Y = Math.Round(drag.StartY + local.Y);
        card.ApplyElementLayouts();

        Canvas.SetLeft(drag.Hotspot, result.Rect.X);
        Canvas.SetTop(drag.Hotspot, result.Rect.Y);
        DrawPreviewGuides(result.Guides);
    }

    private void DrawPreviewGuides(IReadOnlyList<SnapGuide> guides)
    {
        PreviewWidgetGuides.Children.Clear();
        var pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x4F, 0xA3));
        foreach (var g in guides)
            PreviewWidgetGuides.Children.Add(g.Vertical
                ? new Line { X1 = g.Position, X2 = g.Position, Y1 = g.From - 6, Y2 = g.To + 6, Stroke = pink, StrokeThickness = 1 }
                : new Line { X1 = g.From - 6, X2 = g.To + 6, Y1 = g.Position, Y2 = g.Position, Stroke = pink, StrokeThickness = 1 });
    }

    private void PlaceHotspot_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_placeDrag is not { } drag) return;
        _placeDrag = null;
        drag.Hotspot.ReleaseMouseCapture();
        PreviewWidgetGuides.Children.Clear();
        if (drag.Moved) CurrentAppearance?.NotifyElementsChanged(); // enregistrement et bureau / Switch
        // Cadres recalculés une fois la carte remise en page (taille automatique, contenu décalé).
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => SelectPreviewPart(drag.Part));
        e.Handled = true;
    }

    private void PlaceHotspot_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Border { Tag: ModulePreviewPart part } || CurrentAppearance is not { } appearance) return;
        var layout = appearance.GetElement(part.Key);
        layout.Scale = Math.Round(Math.Clamp(layout.Scale * (e.Delta > 0 ? 1.06 : 1 / 1.06), 0.3, 4), 3);
        _placeCard?.ApplyElementLayouts();
        appearance.NotifyElementsChanged();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, RefreshPreviewElements);
        e.Handled = true;
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
