using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Oxyn.Controls;
using Oxyn.Services;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Oxyn.Widgets;

/// <summary>
/// Module Barre de recherche (bureau seulement). Les modules sont rattachés au bureau, où la saisie au clavier n'est pas
/// fiable : le champ affiché ici n'est qu'une façade, un clic ouvre <see cref="SearchBarWindow"/> exactement par-dessus.
/// </summary>
public sealed class SearchBarWidgetContent : UserControl
{
    private readonly Border _field;
    private readonly TextBlock _placeholder;
    private readonly Border _favorites;
    private readonly Border _engine;
    private SearchBarSettings? _settings;
    private SearchBarWindow? _open;
    private bool _pressed;
    private Point _pressPoint;

    public SearchBarWidgetContent()
    {
        Width = 420;
        // Moteur actuel, à gauche : un clic ouvre la liste des moteurs.
        _engine = EnginePill(() => _settings, () => { });

        _placeholder = WidgetUi.Text("placeholder", 14, sub: true);
        _placeholder.VerticalAlignment = VerticalAlignment.Center;

        _favorites = new Border
        {
            Width = 34, Height = 30, Margin = new Thickness(4, 0, 5, 0), CornerRadius = new CornerRadius(15),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Favoris du navigateur",
            Child = Glyph("\uE70D", 12)
        };
        _favorites.MouseEnter += (_, _) => _favorites.SetResourceReference(Border.BackgroundProperty, "ButtonBrush");
        _favorites.MouseLeave += (_, _) => _favorites.Background = Brushes.Transparent;
        _favorites.MouseLeftButtonDown += (_, e) => { e.Handled = true; Open(favorites: true); };

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_engine, Dock.Left);
        DockPanel.SetDock(_favorites, Dock.Right);
        row.Children.Add(_engine);
        row.Children.Add(_favorites);
        row.Children.Add(_placeholder);

        _field = new Border { Height = 42, CornerRadius = new CornerRadius(21), BorderThickness = new Thickness(1), Child = row, Cursor = Cursors.IBeam };
        // Le module n'a pas de carte : on le déplace en faisant glisser la barre elle-même. Un clic (sans glisser) ouvre la saisie.
        _field.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _pressed = true;
            _pressPoint = e.GetPosition(this);
            _field.CaptureMouse();
        };
        _field.MouseMove += (_, e) =>
        {
            if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _pressPoint.X) < 4 && Math.Abs(p.Y - _pressPoint.Y) < 4) return;
            _pressed = false;
            _field.ReleaseMouseCapture();
            if (Window.GetWindow(this) is WidgetWindow window) window.DragFromContent();
        };
        _field.MouseLeftButtonUp += (_, e) =>
        {
            if (!_pressed) return;
            e.Handled = true;
            _pressed = false;
            _field.ReleaseMouseCapture();
            Open(favorites: false);
        };
        Content = _field;
        Apply(new SearchBarSettings());
    }

    private static TextBlock Glyph(string glyph, double size)
    {
        var t = new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily(ShortcutIcons.SymbolFont), FontSize = size,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
        return t;
    }

    /// <summary>
    /// Pastille « Moteur ▾ » : un clic ouvre la liste des moteurs (coché = actuel) ; le choix est enregistré dans les
    /// réglages (le module se met à jour) puis <paramref name="changed"/> est appelé.
    /// </summary>
    public static Border EnginePill(Func<SearchBarSettings?> settings, Action changed, Action? menuClosed = null)
    {
        var name = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var chevron = Glyph("\uE70D", 8);
        chevron.Margin = new Thickness(6, 1, 0, 0);
        var pill = new Border
        {
            Height = 28, Margin = new Thickness(7, 0, 8, 0), Padding = new Thickness(11, 0, 9, 0), CornerRadius = new CornerRadius(14),
            Cursor = Cursors.Hand, ToolTip = "Changer de moteur de recherche",
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, chevron } }
        };
        pill.SetResourceReference(Border.BackgroundProperty, "ButtonBrush");
        pill.MouseLeftButtonUp += (_, e) => e.Handled = true;
        pill.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            if (settings() is not { } s) return;
            var menu = new ContextMenu { PlacementTarget = pill, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var current = SearchBar.Engine(s).Id;
            var builtIn = true;
            foreach (var engine in SearchBar.AllEngines(s))
            {
                // Séparateur entre les moteurs intégrés et ceux ajoutés par l'utilisateur.
                if (builtIn && !SearchBar.Engines.Contains(engine)) { menu.Items.Add(new Separator()); builtIn = false; }
                var item = new MenuItem { Header = engine.Name, IsCheckable = true, IsChecked = engine.Id == current };
                item.Click += (_, _) =>
                {
                    s.Engine = engine.Id;
                    SetPillText(pill, s);
                    changed();
                };
                menu.Items.Add(item);
            }
            menu.Opened += (_, _) => EngineMenuOpen = true;
            menu.Closed += (_, _) => { EngineMenuOpen = false; menuClosed?.Invoke(); };
            menu.IsOpen = true;
        };
        return pill;
    }

    /// <summary>Vrai pendant que la liste des moteurs est ouverte (la fenêtre de saisie ne doit pas se fermer).</summary>
    public static bool EngineMenuOpen { get; private set; }

    /// <summary>Texte d'invite : « / » n'est mentionné que si des commandes ont été ajoutées.</summary>
    public static string Placeholder(SearchBarSettings settings)
        => settings.Commands.Count > 0 ? "Rechercher ou taper / pour l'IA" : "Rechercher";

    /// <summary>Taille du texte saisi pour une barre de cette hauteur (14 pour 42 px).</summary>
    public static double TextSize(double barHeight) => Math.Clamp(barHeight / 3, 10, 26);

    /// <summary>Pastille du moteur ajustée à la hauteur de la barre.</summary>
    public static void SizePill(Border pill, double barHeight)
    {
        pill.Height = Math.Max(18, barHeight - 14);
        pill.CornerRadius = new CornerRadius(pill.Height / 2);
        if (pill.Child is not StackPanel { Children: [TextBlock name, TextBlock chevron] }) return;
        name.FontSize = Math.Clamp(barHeight * 0.3, 9, 20);
        chevron.FontSize = Math.Clamp(barHeight * 0.19, 7, 14);
    }

    /// <summary>Nom du moteur actuel dans la pastille.</summary>
    public static void SetPillText(Border pill, SearchBarSettings settings)
    {
        if (pill.Child is StackPanel { Children: [TextBlock name, ..] }) name.Text = SearchBar.Engine(settings).Name;
    }

    /// <summary>
    /// Barre claire et assez opaque : textes et boutons passent en sombre (ressources locales de <paramref name="scope"/>),
    /// sinon ceux du thème sont gardés.
    /// </summary>
    public static void ApplyContrast(FrameworkElement scope, string hex, double opacity)
    {
        var c = ThemeService.Parse(hex, Colors.Black);
        var light = opacity >= 0.5 && (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255 > 0.6;
        foreach (var key in new[] { "TextBrush", "SubTextBrush", "ButtonBrush" }) scope.Resources.Remove(key);
        if (!light) return;
        scope.Resources["TextBrush"] = LookBrush("#FF1B1D22", 1, Colors.Black);
        scope.Resources["SubTextBrush"] = LookBrush("#FF5C6070", 1, Colors.Gray);
        scope.Resources["ButtonBrush"] = LookBrush("#FF000000", 0.08, Colors.Black);
    }

    /// <summary>Couleur hexadécimale avec l'opacité choisie (pinceau figé).</summary>
    public static Brush LookBrush(string hex, double opacity, Color fallback)
    {
        var c = ThemeService.Parse(hex, fallback);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>Réglages actuels : couleurs de la barre, moteur affiché dans le texte d'invite, flèche des favoris.</summary>
    public void Apply(SearchBarSettings settings)
    {
        _settings = settings;
        _field.Background = LookBrush(settings.BarColor, settings.BarOpacity, Color.FromRgb(0x1B, 0x1D, 0x22));
        _field.BorderBrush = LookBrush(settings.BorderColor, settings.BorderOpacity, Colors.White);
        ApplyContrast(_field, settings.BarColor, settings.BarOpacity);
        // Taille : largeur et hauteur choisies, coins toujours arrondis en pilule, textes proportionnels à la hauteur.
        Width = settings.BarWidth;
        _field.Height = settings.BarHeight;
        _field.CornerRadius = new CornerRadius(settings.BarHeight / 2);
        _placeholder.FontSize = TextSize(settings.BarHeight);
        SizePill(_engine, settings.BarHeight);
        _favorites.Width = Math.Max(24, settings.BarHeight * 0.8);
        _favorites.Height = Math.Max(20, settings.BarHeight - 12);
        _favorites.CornerRadius = new CornerRadius(_favorites.Height / 2);
        if (_favorites.Child is TextBlock arrow) arrow.FontSize = Math.Clamp(settings.BarHeight * 0.29, 8, 22);
        _placeholder.Text = Placeholder(settings);
        SetPillText(_engine, settings);
        _favorites.Visibility = settings.Bookmarks ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Open(bool favorites)
    {
        if (_settings is null || PresentationSource.FromVisual(_field) is not { CompositionTarget: { } target }) return;
        if (_open is { IsLoaded: true })
        {
            _open.Activate();
            return;
        }
        // Coins du champ à l'écran (pixels) convertis en unités WPF : la fenêtre se pose exactement dessus.
        var fromDevice = target.TransformFromDevice;
        var topLeft = fromDevice.Transform(_field.PointToScreen(new Point(0, 0)));
        var bottomRight = fromDevice.Transform(_field.PointToScreen(new Point(_field.ActualWidth, _field.ActualHeight)));
        _open = new SearchBarWindow(_settings, new Rect(topLeft, bottomRight), favorites);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
    }
}
