using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Oxyn.Controls;
using Oxyn.Services;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Oxyn.Widgets;

/// <summary>
/// Calque d'éléments d'un module (disposition ModuleDesign) : anneaux, barres, courbes animées, chiffres… posés dans
/// la carte. Disposition « Classique » : calque transparent par-dessus le contenu d'origine ; autres dispositions :
/// surface de taille fixe qui remplace le contenu. Chaque élément est déplaçable (clé « block:Id ») dans l'aperçu.
/// </summary>
public sealed class BuiltWidgetContent : UserControl
{
    private interface IBlockView
    {
        FrameworkElement Root { get; }
        void Update(SensorReading? reading);
    }

    private readonly List<(BuilderBlock Block, IBlockView View)> _views = new();

    private IReadOnlyDictionary<string, SensorReading>? _values;

    /// <summary>Reconstruit les éléments à partir de la disposition.</summary>
    public void Apply(ModuleDesign module)
    {
        _views.Clear();
        var surface = new Canvas();
        if (!module.IsClassic)
        {
            surface.Width = Math.Max(60, module.Width);
            surface.Height = Math.Max(40, module.Height);
        }
        foreach (var block in module.Blocks ?? new List<BuilderBlock>())
        {
            var view = Create(block);
            var root = view.Root;
            root.Width = Math.Max(10, block.Width);
            root.Height = Math.Max(10, block.Height);
            Canvas.SetLeft(root, block.X);
            Canvas.SetTop(root, block.Y);
            Movable.SetKey(root, $"block:{block.Id}");
            surface.Children.Add(root);
            _views.Add((block, view));
        }
        if (!module.IsClassic && (module.Blocks?.Count ?? 0) == 0)
        {
            var empty = WidgetUi.Text("label", 12, sub: true);
            empty.Text = "Ajoute des éléments dans la personnalisation du module.";
            empty.TextWrapping = TextWrapping.Wrap;
            empty.Width = surface.Width - 24;
            Canvas.SetLeft(empty, 12);
            Canvas.SetTop(empty, 12);
            surface.Children.Add(empty);
        }
        Content = surface;
        if (_values is not null) Update(_values);
        // La carte recherche ses éléments déplaçables après la mise en page (voir ModuleCard.ApplyDesign).
    }

    /// <summary>Nouvelles mesures (catalogue complet des capteurs, réseau et disques compris).</summary>
    public void Update(IReadOnlyDictionary<string, SensorReading> values)
    {
        _values = values;
        foreach (var (block, view) in _views)
        {
            var readings = block.AllSources().Select(id => values.TryGetValue(id, out var r) ? r : null).OfType<SensorReading>().ToList();
            view.Update(Combine(block, readings));
            if (view is IMultiView multi) multi.UpdateEach(block.Combine == "Each" && readings.Count > 1 ? readings : null);
        }
    }

    /// <summary>Plusieurs données : une seule valeur (moyenne, maximum, minimum, somme) dans l'unité de la première.</summary>
    private static SensorReading? Combine(BuilderBlock b, IReadOnlyList<SensorReading> readings)
    {
        if (readings.Count <= 1) return readings.FirstOrDefault();
        var values = readings.Select(r => r.Value).ToList();
        var value = b.Combine switch
        {
            "Max" => values.Max(),
            "Min" => values.Min(),
            "Sum" => values.Sum(),
            _ => values.Average()
        };
        var how = b.Combine switch { "Max" => "maximum", "Min" => "minimum", "Sum" => "total", _ => "moyenne" };
        return readings[0] with { Value = value, Name = $"{readings[0].Name} ({how})" };
    }

    /// <summary>Vue qui sait afficher chaque donnée séparément (« 32 / 33 °C »).</summary>
    private interface IMultiView
    {
        void UpdateEach(IReadOnlyList<SensorReading>? each);
    }

    private static string EachText(IReadOnlyList<SensorReading> each)
    {
        var unit = each[0].Unit;
        var numbers = each.Select(r => SensorCatalog.Format(r.Value, r.Unit).Replace(r.Unit, "").Replace("°C", "").Trim());
        return $"{string.Join(" / ", numbers)} {unit}".Trim();
    }

    private static IBlockView Create(BuilderBlock b) => b.Type switch
    {
        "Ring" => new RingView(b),
        "Bar" => new BarView(b),
        "Graph" => new GraphView(b),
        "Big" => new BigView(b),
        "Header" => new HeaderView(b),
        "Text" => new TextView(b),
        _ => new ValueView(b)
    };

    // ---------- Aides ----------

    private static string LabelOf(BuilderBlock b, SensorReading? r)
    {
        var label = !string.IsNullOrWhiteSpace(b.Label) ? b.Label! : r?.Name ?? "—";
        return b.Uppercase ? label.ToUpper(WidgetUi.Fr) : label;
    }

    private static double MaxOf(BuilderBlock b, SensorReading? r)
        => b.Max > 0 ? b.Max : r is null ? 100 : SensorCatalog.DefaultMax(r.Unit);

    private static string NumberOf(SensorReading r)
        => SensorCatalog.Format(r.Value, r.Unit).Replace(r.Unit, "").Replace("°C", "").Trim();

    private static string UnitOf(SensorReading r) => r.Unit;

    private static Brush? OwnBrush(BuilderBlock b)
        => string.IsNullOrWhiteSpace(b.Color) ? null : WidgetUi.Brush(b.Color!, Colors.White);

    private static void Accent(Shape shape, BuilderBlock b, DependencyProperty property)
    {
        if (OwnBrush(b) is { } own) shape.SetValue(property, own);
        else shape.SetResourceReference(property, "AccentBrush");
    }

    // ---------- Blocs ----------

    private sealed class RingView : IBlockView
    {
        private readonly BuilderBlock _b;
        private readonly RadialGauge _gauge = new();
        private readonly TextBlock _value, _unit, _label;
        public FrameworkElement Root { get; }

        public RingView(BuilderBlock b)
        {
            _b = b;
            _gauge.SweepAngle = b.Sweep is 180 or 360 ? b.Sweep : 270;
            _gauge.Thickness = Math.Max(2, Math.Min(b.Width, b.Height) / 14 * Math.Clamp(b.Thickness, 0.3, 4));
            if (!string.IsNullOrEmpty(b.Ticks))
            {
                _gauge.ShowTicks = true;
                _gauge.TicksEnabled = true;
                _gauge.TickDesign = b.Ticks;
            }
            if (!string.IsNullOrEmpty(b.Design)) _gauge.Design = b.Design;
            else if (b.Segmented) _gauge.Design = "Segments";
            if (OwnBrush(b) is SolidColorBrush own) { _gauge.StartColor = own.Color; _gauge.EndColor = own.Color; }
            _value = WidgetUi.Text("value", b.FontSize * 1.8, number: true, weight: FontWeights.SemiBold);
            _unit = WidgetUi.Text("unit", b.FontSize * 0.8, sub: true, number: true);
            _label = WidgetUi.Text("label", b.FontSize * 0.75, sub: true, weight: FontWeights.SemiBold);
            var number = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            number.Children.Add(_value);
            _unit.VerticalAlignment = VerticalAlignment.Bottom;
            _unit.Margin = new Thickness(1, 0, 0, 4);
            number.Children.Add(_unit);
            var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            center.Children.Add(number);
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(_label);
            var grid = new Grid();
            grid.Children.Add(_gauge);
            grid.Children.Add(center);
            Root = grid;
        }

        public void Update(SensorReading? r)
        {
            _label.Text = LabelOf(_b, r);
            _label.Visibility = _b.ShowLabel ? Visibility.Visible : Visibility.Collapsed;
            if (r is null) { _value.Text = "—"; _unit.Text = ""; return; }
            _gauge.Maximum = MaxOf(_b, r);
            _gauge.Value = Math.Clamp(r.Value, 0, _gauge.Maximum);
            _value.Text = NumberOf(r);
            _unit.Text = UnitOf(r);
        }
    }

    private sealed class BarView : IBlockView
    {
        private readonly BuilderBlock _b;
        private readonly TextBlock _label, _value;
        private readonly Grid _track = new() { Height = 8, VerticalAlignment = VerticalAlignment.Bottom };
        private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4) };
        private readonly UniformGrid _segments = new() { Rows = 1 };
        public FrameworkElement Root { get; }

        public BarView(BuilderBlock b)
        {
            _b = b;
            _label = WidgetUi.Text("label", b.FontSize * 0.9, sub: true, weight: FontWeights.SemiBold);
            _value = WidgetUi.Text("value", b.FontSize, number: true, weight: FontWeights.SemiBold);
            var head = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(_value, Dock.Right);
            head.Children.Add(_value);
            head.Children.Add(_label);

            var back = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.18 };
            back.SetResourceReference(Border.BackgroundProperty, "TextBrush");
            _track.Children.Add(back);
            if (b.Segmented)
            {
                for (var i = 0; i < 20; i++)
                {
                    var cell = new Border { Margin = new Thickness(1, 0, 1, 0), CornerRadius = new CornerRadius(1.5) };
                    _segments.Children.Add(cell);
                }
                _track.Children.Add(_segments);
            }
            else
            {
                if (OwnBrush(b) is { } own) _fill.Background = own; else _fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                _track.Children.Add(_fill);
            }
            var grid = new Grid();
            grid.Children.Add(head);
            grid.Children.Add(_track);
            Root = grid;
            grid.SizeChanged += (_, _) => Update(_last);
        }

        private SensorReading? _last;

        public void Update(SensorReading? r)
        {
            _last = r;
            _label.Text = LabelOf(_b, r);
            _label.Visibility = _b.ShowLabel ? Visibility.Visible : Visibility.Hidden;
            _value.Text = r is null ? "—" : SensorCatalog.Format(r.Value, r.Unit);
            var ratio = r is null ? 0 : Math.Clamp(r.Value / MaxOf(_b, r), 0, 1);
            if (_b.Segmented)
            {
                var lit = (int)Math.Round(ratio * _segments.Children.Count);
                for (var i = 0; i < _segments.Children.Count; i++)
                {
                    var cell = (Border)_segments.Children[i];
                    if (i < lit)
                    {
                        if (OwnBrush(_b) is { } own) cell.Background = own; else cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    }
                    else cell.Background = Brushes.Transparent;
                }
            }
            else _fill.Width = Math.Max(0, Root.ActualWidth * ratio);
        }
    }

    private sealed class GraphView : IBlockView
    {
        private int Capacity => Math.Clamp(_b.HistorySeconds, 10, 600);
        private readonly TranslateTransform _scroll = new();
        private readonly BuilderBlock _b;
        private readonly List<double> _history = new();
        private readonly TextBlock _label, _value, _scale;
        private readonly Polyline _line = new() { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        private readonly Polygon _area = new() { Opacity = 0.18 };
        private readonly Ellipse _dot = new() { Width = 7, Height = 7 };
        private readonly Canvas _plot = new() { ClipToBounds = true };
        public FrameworkElement Root { get; }

        public GraphView(BuilderBlock b)
        {
            _b = b;
            _label = WidgetUi.Text("label", b.FontSize * 0.9, sub: true, weight: FontWeights.SemiBold);
            _value = WidgetUi.Text("value", b.FontSize, number: true, weight: FontWeights.SemiBold);
            _scale = WidgetUi.Text("scale", b.FontSize * 0.75, sub: true, number: true);
            Accent(_line, b, Shape.StrokeProperty);
            Accent(_area, b, Shape.FillProperty);
            Accent(_dot, b, Shape.FillProperty);
            // Ligne, surface et point glissent ensemble vers la gauche à chaque mesure : courbe animée.
            var moving = new Canvas { RenderTransform = _scroll };
            moving.Children.Add(_area);
            moving.Children.Add(_line);
            moving.Children.Add(_dot);
            _plot.Children.Add(moving);
            _area.Visibility = b.Fill ? Visibility.Visible : Visibility.Collapsed;
            var head = new DockPanel();
            DockPanel.SetDock(_value, Dock.Right);
            head.Children.Add(_value);
            head.Children.Add(_label);
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.Children.Add(head);
            Grid.SetRow(_plot, 1);
            _plot.Margin = new Thickness(0, 4, 4, 4);
            grid.Children.Add(_plot);
            _scale.VerticalAlignment = VerticalAlignment.Top;
            _scale.Margin = new Thickness(0, 4, 0, 0);
            Grid.SetRow(_scale, 1);
            grid.Children.Add(_scale);
            Root = grid;
            _plot.SizeChanged += (_, _) => Redraw();
        }

        public void Update(SensorReading? r)
        {
            _label.Text = LabelOf(_b, r);
            _label.Visibility = _b.ShowLabel ? Visibility.Visible : Visibility.Collapsed;
            if (r is null) { _value.Text = "—"; return; }
            _value.Text = SensorCatalog.Format(r.Value, r.Unit);
            _history.Add(r.Value);
            while (_history.Count > Capacity) _history.RemoveAt(0);
            Redraw();
            // Glissement d'un pas sur ~0,9 s : la courbe défile au lieu de sauter.
            var step = _plot.ActualWidth / Math.Max(1, Capacity - 1);
            if (step > 0 && _history.Count > 1)
                _scroll.BeginAnimation(TranslateTransform.XProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(step, 0, TimeSpan.FromMilliseconds(900)));
        }

        private void Redraw()
        {
            double w = _plot.ActualWidth, h = _plot.ActualHeight;
            if (w <= 0 || h <= 0 || _history.Count == 0) return;
            var max = _b.Max > 0 ? _b.Max : Math.Max(1, _history.Max() * 1.15);
            _scale.Text = max >= 10 ? Math.Round(max).ToString(WidgetUi.Fr) : max.ToString("0.#", WidgetUi.Fr);
            var points = new PointCollection();
            var step = w / (Capacity - 1);
            var start = Capacity - _history.Count;
            for (var i = 0; i < _history.Count; i++)
                points.Add(new Point((start + i) * step, h - Math.Clamp(_history[i] / max, 0, 1) * (h - 4) - 2));
            _line.Points = points;
            var area = new PointCollection(points) { new Point(points[^1].X, h), new Point(points[0].X, h) };
            _area.Points = area;
            Canvas.SetLeft(_dot, points[^1].X - 3.5);
            Canvas.SetTop(_dot, points[^1].Y - 3.5);
        }
    }

    private sealed class ValueView : IBlockView, IMultiView
    {
        public void UpdateEach(IReadOnlyList<SensorReading>? each)
        {
            if (each is not null) _value.Text = EachText(each);
        }

        private readonly BuilderBlock _b;
        private readonly TextBlock _label, _value;
        public FrameworkElement Root { get; }

        public ValueView(BuilderBlock b)
        {
            _b = b;
            _label = WidgetUi.Text("label", b.FontSize * 0.85, sub: true, weight: b.Uppercase ? FontWeights.SemiBold : FontWeights.Normal);
            _value = WidgetUi.Text("value", b.FontSize, number: true, weight: FontWeights.SemiBold);
            if (OwnBrush(b) is { } own) _value.Foreground = own;
            _label.VerticalAlignment = _value.VerticalAlignment = VerticalAlignment.Center;
            var dock = new DockPanel();
            DockPanel.SetDock(_value, Dock.Right);
            dock.Children.Add(_value);
            dock.Children.Add(_label);
            Root = dock;
        }

        public void Update(SensorReading? r)
        {
            _label.Text = LabelOf(_b, r);
            _value.Text = r is null ? "—" : SensorCatalog.Format(r.Value, r.Unit);
        }
    }

    private sealed class BigView : IBlockView, IMultiView
    {
        public void UpdateEach(IReadOnlyList<SensorReading>? each)
        {
            if (each is null) return;
            _value.Text = string.Join(" / ", each.Select(NumberOf));
            _unit.Text = UnitOf(each[0]);
        }

        private readonly BuilderBlock _b;
        private readonly TextBlock _value, _unit, _label;
        public FrameworkElement Root { get; }

        public BigView(BuilderBlock b)
        {
            _b = b;
            _value = WidgetUi.Text("value", b.FontSize * 2, number: true, weight: FontWeights.SemiBold);
            _unit = WidgetUi.Text("unit", b.FontSize * 0.8, sub: true, number: true);
            _label = WidgetUi.Text("label", b.FontSize * 0.75, sub: true, weight: FontWeights.SemiBold);
            if (OwnBrush(b) is { } own) _value.Foreground = own;
            var number = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            number.Children.Add(_value);
            _unit.VerticalAlignment = VerticalAlignment.Bottom;
            _unit.Margin = new Thickness(2, 0, 0, 5);
            number.Children.Add(_unit);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(number);
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(_label);
            Root = stack;
        }

        public void Update(SensorReading? r)
        {
            _label.Text = LabelOf(_b, r);
            _label.Visibility = _b.ShowLabel ? Visibility.Visible : Visibility.Collapsed;
            if (r is null) { _value.Text = "—"; _unit.Text = ""; return; }
            _value.Text = NumberOf(r);
            _unit.Text = UnitOf(r);
        }
    }

    private sealed class HeaderView : IBlockView
    {
        public FrameworkElement Root { get; }
        public HeaderView(BuilderBlock b)
        {
            var dock = new DockPanel { LastChildFill = true };
            if (b.Glyph is null)
            {
                var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                Accent(dot, b, Shape.FillProperty);
                dock.Children.Add(dot);
            }
            else if (b.Glyph.Length > 0)
            {
                var icon = WidgetUi.Text("icon", b.FontSize * 1.2);
                icon.FontFamily = new FontFamily(ShortcutIcons.SymbolFont);
                icon.Text = b.Glyph;
                icon.Margin = new Thickness(0, 0, 8, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                dock.Children.Add(icon);
            }
            var title = WidgetUi.Text("title", b.FontSize * 1.15, weight: FontWeights.SemiBold);
            title.Text = string.IsNullOrWhiteSpace(b.Label) ? "Titre" : b.Label;
            title.VerticalAlignment = VerticalAlignment.Center;
            if (!string.IsNullOrWhiteSpace(b.Text))
            {
                var sub = WidgetUi.Text("subtitle", b.FontSize * 0.8, sub: true);
                sub.Text = b.Text;
                sub.VerticalAlignment = VerticalAlignment.Center;
                sub.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(sub, Dock.Right);
                dock.Children.Add(sub);
            }
            dock.Children.Add(title);
            Root = dock;
        }

        public void Update(SensorReading? r) { }
    }

    private sealed class TextView : IBlockView
    {
        private readonly BuilderBlock _b;
        private readonly TextBlock _text;
        public FrameworkElement Root { get; }

        public TextView(BuilderBlock b)
        {
            _b = b;
            _text = WidgetUi.Text("text", b.FontSize * 0.9, sub: true);
            _text.TextWrapping = TextWrapping.Wrap;
            if (OwnBrush(b) is { } own) _text.Foreground = own;
            Root = _text;
            Update(null);
        }

        public void Update(SensorReading? r)
        {
            var value = r is null ? "—" : SensorCatalog.Format(r.Value, r.Unit);
            _text.Text = (_b.Text ?? "").Replace("{valeur}", value);
        }
    }
}
