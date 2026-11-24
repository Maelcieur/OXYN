using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Oxyn.Controls;
using Oxyn.Interop;
using Oxyn.Services;

namespace Oxyn;

/// <summary>
/// Fenêtre d'un module sur le bureau : rattachée au bureau, déplaçable à la souris, position enregistrée.
/// Le cadre et toute l'apparence du module sont dans <see cref="ModuleCard"/>.
/// </summary>
public partial class WidgetWindow : Window
{
    private readonly string _id;
    private readonly ConfigStore _configStore;
    private readonly AppConfig _config;
    private readonly ModuleCard _card;
    private double _margin; // marge transparente actuelle autour de la carte (déjà retirée de Left/Top)

    public WidgetWindow(string id, UIElement content, ConfigStore configStore, AppConfig config, double defaultX, double defaultY, bool useDefaultChrome = true)
    {
        InitializeComponent();

        _id = id;
        _configStore = configStore;
        _config = config;
        _card = new ModuleCard(id, content, config, useDefaultChrome);
        Content = _card;
        // La carte réserve une marge (ombre, survol) en agrandissant la fenêtre : on se décale d'autant vers le
        // haut et la gauche pour que la carte reste à sa position enregistrée, sans « saut » visible.
        _card.OuterMarginChanged += margin =>
        {
            var delta = margin - _margin;
            _margin = margin;
            Left -= delta;
            Top -= delta;
        };

        var saved = config.Layout.FirstOrDefault(w => w.Id == id);
        Left = saved?.X ?? defaultX;
        Top = saved?.Y ?? defaultY;

        Loaded += (_, _) => _card.PlayAppearAnimation();
    }


    /// <summary>Contenu du module (alimenté par WidgetManager).</summary>
    public UIElement ModuleContent => _card.ModuleContent;

    public ModuleCard PreviewCard => _card;

    public void ApplyAppearance(ModuleAppearance appearance) => _card.ApplyAppearance(appearance);

    public void ApplyFontScale() => _card.ApplyFontScale();

    /// <summary>Pochette du morceau en cours (module Lecture en cours), utilisable comme fond.</summary>
    public void SetMediaArtwork(ImageSource? artwork) => _card.SetMediaArtwork(artwork);

    /// <summary>Clés des éléments déplaçables du module, dans l'ordre d'affichage.</summary>
    public IReadOnlyList<string> MovableKeys => _card.MovableKeys;

    /// <summary>Taille actuelle de la carte, pour initialiser la taille manuelle à partir de l'automatique.</summary>
    public Size CurrentCardSize => _card.CurrentCardSize;

    public static BitmapImage? LoadImage(string? path) => ModuleCard.LoadImage(path);

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        // Rattache la fenêtre au bureau (derrière toute fenêtre normale, comme une icône de bureau)
        // dès que son handle natif existe. Si Explorer redémarre plus tard, le rattachement est
        // perdu ; OXYN redevient alors une fenêtre normale plutôt que de planter.
        var handle = new WindowInteropHelper(this).Handle;
        DesktopWidgetHost.AttachToDesktop(handle);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragFromContent();

    /// <summary>Déplace le module à la souris (bouton gauche enfoncé), sauf si les positions sont verrouillées.</summary>
    public void DragFromContent()
    {
        if (_config.WidgetsLocked) return;

        var startLeft = Left;
        var startTop = Top;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // le bouton a été relâché avant que DragMove() ne démarre réellement le déplacement
        }

        // Un simple clic (sans déplacement) ne doit pas réécrire le fichier de configuration.
        if (Left != startLeft || Top != startTop)
            SavePosition();
    }


    private void SavePosition()
    {
        var existing = _config.Layout.FirstOrDefault(w => w.Id == _id);
        if (existing is null)
        {
            existing = new WidgetPosition { Id = _id };
            _config.Layout.Add(existing);
        }

        // Position de la carte, pas de la fenêtre : la marge d'ombre peut changer sans déplacer la sauvegarde.
        existing.X = Left + _margin;
        existing.Y = Top + _margin;
        existing.Visible = true;
        _configStore.Save(_config);
    }
}
