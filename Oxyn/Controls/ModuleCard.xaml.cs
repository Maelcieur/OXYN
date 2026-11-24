using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Oxyn.Services;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Oxyn.Controls;

/// <summary>
/// Carte d'un module : cadre, fond, contenu et toute son apparence personnalisée (style, taille, textes,
/// disposition libre des éléments). Utilisée par la fenêtre du bureau et par l'écran de la Switch,
/// chacun avec sa propre <see cref="ModuleAppearance"/>.
/// </summary>
public partial class ModuleCard : UserControl
{
    private readonly string _id;
    private readonly AppConfig _config;
    private readonly bool _useDefaultChrome;
    // Module sans carte (Barre de recherche) : ni fond, ni contour, ni ombre ; il dessine lui-même son apparence.
    private readonly bool _bare;
    private readonly bool _fillFrame;

    /// <param name="fillFrame">Vrai sur la Switch : la carte remplit le cadre imposé, la taille du module est ignorée.</param>
    public ModuleCard(string id, UIElement content, AppConfig config, bool useDefaultChrome = true, bool fillFrame = false)
    {
        InitializeComponent();
        InitEffects();

        _id = id;
        _config = config;
        _useDefaultChrome = useDefaultChrome;
        _fillFrame = fillFrame;
        _content = content;
        // Contenu d'origine + calque d'éléments (disposition du module) par-dessus, dans la même grille.
        var stack = new Grid();
        stack.Children.Add(content);
        stack.Children.Add(_designLayer);
        ContentHost.Content = stack;
        ContentHost.SizeChanged += (_, _) => NaturalSizeChanged?.Invoke();

        _bare = id == "searchbar";
        if (!useDefaultChrome || _bare)
        {
            Chrome.Style = null;
            Chrome.Background = Brushes.Transparent;
            Chrome.BorderThickness = new Thickness(0);
            ContentScaler.Margin = new Thickness(_bare ? 4 : 0);
        }

        AddHandler(Movable.ElementsChangedEvent, new RoutedEventHandler((_, _) => RefreshMovables()));
        // Un bouton qui change d'état (lecture ↔ pause, muet ↔ son) change de réglage.
        AddHandler(ButtonPart.KeyChangedEvent, new RoutedEventHandler((_, _) => ApplyButtons()));

        Loaded += (_, _) =>
        {
            _movables = null; // l'arbre visuel a pu changer depuis le dernier affichage
            ApplyElementLayouts();
            ApplyTexts();
            ApplyButtons();
            WatchListsForTexts();
        };
        // Sans désabonnement, chaque Loaded ajouterait un gestionnaire de plus sur les listes du module.
        Unloaded += (_, _) =>
        {
            UnwatchLists();
            _movables = null;
            StopFit();
        };
    }

    public string ModuleId => _id;

    /// <summary>Contenu du module (alimenté par WidgetManager).</summary>
    private readonly UIElement _content;
    private readonly Oxyn.Widgets.BuiltWidgetContent _designLayer = new() { Visibility = Visibility.Collapsed };
    private ModuleDesign? _design;

    public UIElement ModuleContent => _content;

    /// <summary>
    /// Disposition du module : « Classique » = contenu d'origine (+ éléments ajoutés par-dessus) ; sinon le contenu
    /// est masqué et seuls les éléments sont affichés.
    /// </summary>
    private string? _designSignature;

    private void ApplyDesign(ModuleDesign? design)
    {
        _design = design;
        // Les éléments ne sont reconstruits que si la disposition a vraiment changé : un simple déplacement (ElementLayout)
        // ou un autre réglage de l'apparence garde les mêmes éléments (et l'historique des courbes).
        var signature = design is null || design.IsEmpty ? null : System.Text.Json.JsonSerializer.Serialize(design);
        if (signature == _designSignature) return;
        _designSignature = signature;

        if (signature is null)
        {
            _designLayer.Visibility = Visibility.Collapsed;
            _designLayer.Content = null;
            _content.Visibility = Visibility.Visible;
        }
        else
        {
            _designLayer.Apply(design!);
            _designLayer.Visibility = Visibility.Visible;
            _content.Visibility = design!.IsClassic ? Visibility.Visible : Visibility.Collapsed;
        }
        // Les nouveaux éléments n'existent dans l'arbre visuel qu'après la mise en page : on les recherche ensuite,
        // puis on leur applique leur position enregistrée.
        _movables = null;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            RefreshMovables();
            DesignRebuilt?.Invoke(); // l'aperçu de Personnaliser redessine ses cadres
        });
    }

    /// <summary>Les éléments de la disposition viennent d'être reconstruits et placés.</summary>
    public event Action? DesignRebuilt;

    /// <summary>Vrai si le module affiche des éléments (il faut alors lui envoyer les mesures).</summary>
    public bool HasDesign => _design is { IsEmpty: false };

    /// <summary>Dernières mesures pour les éléments du module (capteurs, réseau, disques, FPS).</summary>
    public void UpdateDesign(IReadOnlyDictionary<string, SensorReading> values)
    {
        if (HasDesign) _designLayer.Update(values);
    }

    /// <summary>Apparence appliquée en dernier (null avant la première).</summary>
    public ModuleAppearance? Appearance => _appearance;

    /// <summary>Applique l'apparence propre au module : style, taille, image de fond, textes affichés.</summary>
    public void ApplyAppearance(ModuleAppearance appearance)
    {
        if (!_useDefaultChrome) return;

        // Style propre (couleurs et polices, carte, jauges, animations : chaque groupe indépendamment) :
        // les ressources de la carte masquent celles de l'application.
        Resources.Clear();
        if (appearance.HasAnyCustomStyle)
            ThemeService.ApplyTheme(Resources, appearance.EffectiveTheme(_config.Theme!));

        // Taille : le contenu garde sa mise en page et est mis à l'échelle pour remplir la carte,
        // ou garde sa taille réelle (calé en haut à gauche) pour laisser de la place où déplacer les éléments.
        var autoSize = appearance.AutoSize && !_fillFrame;
        var realSize = autoSize || appearance.StretchMode == "None";
        // Switch : le cadre épouse toujours le contenu à l'échelle choisie, qui est mis à l'échelle sans déformation.
        ContentScaler.Stretch = _fillFrame ? Stretch.Uniform
            : realSize ? Stretch.None : appearance.StretchMode == "Fill" ? Stretch.Fill : Stretch.Uniform;
        ContentScaler.HorizontalAlignment = realSize && !_fillFrame ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        ContentScaler.VerticalAlignment = realSize && !_fillFrame ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        if (_fillFrame || autoSize)
        {
            Chrome.Width = Chrome.Height = double.NaN;
        }
        else
        {
            Chrome.Width = Math.Max(60, appearance.Width);
            Chrome.Height = Math.Max(40, appearance.Height);
        }

        _appearance = appearance;
        ApplyDesign(appearance.Design);
        ApplyCardEffects(appearance.EffectiveTheme(_config.Theme!));
        ApplyElementLayouts();
        ApplyBackground();
        ApplyFontScale();
        ApplyButtons();
    }

    /// <summary>Taille des textes : facteur du thème (global ou propre au module) × facteur du module.</summary>
    public void ApplyFontScale()
    {
        if (_appearance is not { } a) return;
        var theme = a.EffectiveTheme(_config.Theme!);
        _fontScale = Math.Clamp(theme.FontScale, 0.4, 3) * Math.Clamp(a.FontScale, 0.4, 3);
        ApplyTexts();
    }

    private double _fontScale = 1;
    private bool _textRescanPending;

    /// <summary>
    /// Réglages de chaque texte du module (TextPart.Key) : affiché ou masqué, police propre, taille propre
    /// (cumulée avec celle du module et du thème). Sans réglage, le texte reprend son style d'origine.
    /// </summary>
    private void ApplyTexts()
    {
        if (!IsLoaded || _appearance is not { } a) return;

        foreach (var text in FindVisualChildren<TextBlock>(ContentHost))
        {
            if (TextPart.GetKey(text) is not { } key) continue;
            a.Texts.TryGetValue(key, out var style);

            if (style?.Hidden == true) text.Visibility = Visibility.Collapsed;
            else text.ClearValue(VisibilityProperty);

            if (!string.IsNullOrEmpty(style?.Font)) text.FontFamily = FontService.Resolve(style.Font);
            else text.ClearValue(TextBlock.FontFamilyProperty); // police du style ou du thème

            TextOverrides.ApplyColor(text, style?.Color);
            // Textes fixes seulement (titres, libellés) : une valeur mise à jour par le module écraserait le remplacement.
            TextOverrides.ApplyText(text, TextPart.IsEditable(_id, key) ? style?.CustomText : null);
        }

        FontScaler.ApplyAll(ContentHost, _fontScale, text =>
            TextPart.GetKey(text) is { } key && a.Texts.TryGetValue(key, out var style) ? Math.Clamp(style.Scale, 0.3, 4) : 1);
    }

    /// <summary>
    /// Style de chaque bouton du module (ButtonPart.Key) : masqué, fond, texte, survol, icône. Sans réglage,
    /// le bouton garde son rendu d'origine. Rejoué aussi sur les boutons recréés par les listes.
    /// </summary>
    private void ApplyButtons()
    {
        if (!IsLoaded || _appearance is not { } a) return;
        if (!ButtonPart.ByModule.TryGetValue(_id, out var infos)) return;

        foreach (var button in FindVisualChildren<Button>(ContentHost))
        {
            if (ButtonPart.GetKey(button) is not { } key || infos.FirstOrDefault(i => i.Key == key) is not { } info) continue;
            ButtonPart.Apply(button, info, a.Buttons is not null && a.Buttons.TryGetValue(key, out var style) ? style : null);
        }
    }

    private readonly List<ItemContainerGenerator> _watchedGenerators = new();

    /// <summary>
    /// Les listes du module (disques, périphériques, applications) recréent leurs éléments à chaque
    /// rafraîchissement : leurs textes doivent recevoir les réglages une fois générés.
    /// </summary>
    private void WatchListsForTexts()
    {
        UnwatchLists(); // garde anti-doublon : Loaded peut survenir plusieurs fois
        foreach (var list in FindVisualChildren<ItemsControl>(ContentHost))
        {
            list.ItemContainerGenerator.StatusChanged += OnListGeneratorStatusChanged;
            _watchedGenerators.Add(list.ItemContainerGenerator);
        }
    }

    private void UnwatchLists()
    {
        foreach (var generator in _watchedGenerators)
            generator.StatusChanged -= OnListGeneratorStatusChanged;
        _watchedGenerators.Clear();
    }

    private void OnListGeneratorStatusChanged(object? sender, EventArgs e)
    {
        if (_textRescanPending) return;
        _textRescanPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _textRescanPending = false;
            ApplyTexts();
            ApplyButtons();
        });
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    public static BitmapImage? LoadImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            // OnLoad : l'image est lue en mémoire et le fichier n'est pas verrouillé (il peut être remplacé).
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 900;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
