using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Oxyn.ConsoleMode;
using Oxyn.ConsoleMode.Cheats;
using Oxyn.Infrastructure;

namespace Oxyn;

/// <summary>
/// Codes de triche d'un jeu d'émulateur Switch (bouton « Triches » de la liste des jeux) : ajout de fichiers .txt,
/// recherche dans la base en ligne, activation code par code (tout est désactivé par défaut). Les codes cochés sont
/// installés dans l'émulateur et s'appliquent au prochain lancement du jeu.
/// </summary>
public sealed class CheatCodesWindow : Window
{
    private readonly ConsoleGame _game;
    private readonly string? _titleId;
    private readonly Dictionary<string, GameCheatCodes> _all;
    private readonly GameCheatCodes _codes;
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button _online;

    private readonly string? _cheatSlipsToken;

    public CheatCodesWindow(ConsoleGame game, string? cheatSlipsToken = null)
    {
        _game = game;
        _cheatSlipsToken = cheatSlipsToken;
        _titleId = PlayTimeSources.TitleIdOf(game);
        _all = CheatCodes.Load();
        var key = _titleId ?? game.Id;
        if (!_all.TryGetValue(key, out var codes)) _all[key] = codes = new GameCheatCodes();
        _codes = codes;
        if (_titleId != null && CheatCodes.FindBuildId(_titleId) is { } bid) _codes.BuildId = bid;   // version actuelle du jeu

        Title = "OXYN — Codes de triche";
        Width = 600;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0xE8, 0xEB));
        Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1B, 0x1F));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Oxyn;component/Themes/SettingsStyles.xaml") });

        var root = new DockPanel { Margin = new Thickness(20) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = $"Codes de triche — {game.Title}", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis });
        _status.Foreground = Muted;
        head.Children.Add(_status);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var add = new Button { Content = "Ajouter un fichier .txt…", Margin = new Thickness(0, 0, 8, 0) };
        add.Click += (_, _) => AddFiles();
        _online = new Button { Content = "Chercher en ligne", Margin = new Thickness(0, 0, 8, 0) };
        _online.Click += async (_, _) => await SearchOnlineAsync();
        var none = new Button { Content = "Tout désactiver", Margin = new Thickness(0, 0, 8, 0) };
        none.Click += (_, _) => { foreach (var c in _codes.Codes) c.Enabled = false; Changed(); };
        // Supprime tous les codes du jeu (et les retire de l'émulateur), après confirmation.
        var clear = new Button { Content = "Tout supprimer", Style = (Style)FindResource("DangerButton") };
        clear.Click += (_, _) =>
        {
            if (_codes.Codes.Count == 0) return;
            if (MessageBox.Show(this, $"Supprimer les {_codes.Codes.Count} codes de « {_game.Title} » ?", "OXYN",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _codes.Codes.Clear();
            Changed();
            _message.Text = "Tous les codes ont été supprimés (et retirés de l'émulateur).";
        };
        buttons.Children.Add(add);
        buttons.Children.Add(_online);
        buttons.Children.Add(none);
        buttons.Children.Add(clear);
        head.Children.Add(buttons);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var foot = new StackPanel();
        _message.Foreground = Muted;
        foot.Children.Add(_message);
        var close = new Button { Content = "Fermer", HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), IsCancel = true };
        close.Click += (_, _) => Close();
        foot.Children.Add(close);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(foot);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        UpdateStatus();
        Render();
        if (_titleId == null) { add.IsEnabled = false; _online.IsEnabled = false; }
    }

    private static Brush Muted => new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB2));

    private void UpdateStatus()
    {
        if (_titleId == null)
        {
            _status.Text = "Identifiant Switch inconnu : lance une fois le jeu depuis le mode console (OXYN l'apprend tout seul), puis reviens ici.";
            return;
        }
        _status.Text = _codes.BuildId == null
            ? $"Jeu Switch {_titleId}. Version du jeu inconnue : lance-le une fois dans l'émulateur pour que les codes puissent être installés."
            : $"Jeu Switch {_titleId} · version {_codes.BuildId}. Les codes cochés s'appliquent au prochain lancement du jeu.";
    }

    private void Render()
    {
        _list.Children.Clear();
        if (_codes.Codes.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = "Aucun code pour ce jeu. Ajoute un fichier .txt ou cherche en ligne.", Foreground = Muted, Margin = new Thickness(0, 8, 0, 0) });
            return;
        }
        foreach (var code in _codes.Codes.ToList())
        {
            var c = code;
            var check = new CheckBox { IsChecked = c.Enabled, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            check.Checked += (_, _) => { c.Enabled = true; Changed(); };
            check.Unchecked += (_, _) => { c.Enabled = false; Changed(); };
            var del = new Button { Content = "Supprimer", Style = (Style)FindResource("DangerButton"), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            del.Click += (_, _) => { _codes.Codes.Remove(c); Changed(); Render(); };
            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = c.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            texts.Children.Add(new TextBlock { Text = c.Source switch { "fichier" => "Fichier ajouté", "en ligne" => "Base en ligne (switch-cheats-db)", _ => c.Source }, FontSize = 11.5, Foreground = Muted });
            var dock = new DockPanel();
            DockPanel.SetDock(check, Dock.Left);
            DockPanel.SetDock(del, Dock.Right);
            dock.Children.Add(check);
            dock.Children.Add(del);
            dock.Children.Add(texts);
            // Même ligne que les listes des Paramètres (style « RowBorder », propre à SettingsWindow).
            _list.Children.Add(new Border
            {
                Child = dock, Padding = new Thickness(0, 12, 0, 12), BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            });
        }
    }

    /// <summary>Enregistre et installe les codes cochés dans l'émulateur.</summary>
    private void Changed()
    {
        CheatCodes.Save(_all);
        if (_titleId == null) return;
        if (_codes.Codes.Any(c => c.Enabled) && _codes.BuildId == null)
        {
            _message.Text = "Version du jeu inconnue : lance le jeu une fois dans l'émulateur, puis reviens cocher les codes.";
            return;
        }
        var ok = CheatCodes.Apply(_titleId, _codes);
        var n = _codes.Codes.Count(c => c.Enabled);
        _message.Text = !ok ? "Installation impossible dans l'émulateur (voir le journal)."
            : n == 0 ? "Aucun code activé."
            : $"{n} code{(n > 1 ? "s" : "")} activé{(n > 1 ? "s" : "")} : effet au prochain lancement du jeu.";
        Render();
    }

    private void AddFiles()
    {
        var dlg = new OpenFileDialog { Title = "Fichiers de codes (format Atmosphère)", Filter = "Codes de triche (*.txt)|*.txt|Tous les fichiers|*.*", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        var n = 0;
        foreach (var f in dlg.FileNames)
        {
            try { n += CheatCodes.Merge(_codes, CheatCodes.ReadFile(f)); }
            catch (Exception ex) { Log.Warn("Fichier de codes illisible", ex); }
        }
        CheatCodes.Save(_all);
        _message.Text = n == 0 ? "Aucun code trouvé dans ce fichier (attendu : [Nom] puis des lignes hexadécimales)."
            : $"{n} code{(n > 1 ? "s" : "")} ajouté{(n > 1 ? "s" : "")}, désactivé{(n > 1 ? "s" : "")} : coche ceux que tu veux.";
        Render();
    }

    private async Task SearchOnlineAsync()
    {
        if (_titleId == null) return;
        _online.IsEnabled = false;
        _message.Text = "Recherche en ligne (switch-cheats-db, CheatSlips)…";
        try
        {
            var (_, message) = await CheatCodes.SearchOnlineAsync(_codes, _titleId, _cheatSlipsToken);
            CheatCodes.Save(_all);
            _message.Text = message;
            Render();
        }
        catch (Exception ex)
        {
            Log.Warn("Recherche de codes en ligne", ex);
            _message.Text = "Recherche impossible (pas de connexion ?).";
        }
        finally { _online.IsEnabled = true; }
    }
}
