using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Oxyn.ConsoleMode.Cheats;

namespace Oxyn.ConsoleMode;

/// <summary>Données affichées par le menu Guide pendant un jeu.</summary>
/// <summary><paramref name="Cheats"/> : ouvre la session de triches (jeux d'émulateur seulement, sinon null).</summary>
public sealed record GuideMenuInfo(ConsoleGame Game, string Platform, DateTime SessionStart, ControllerInfo? Controller,
    Func<CheatSession?>? Cheats = null, string? CheatSlipsToken = null, bool? ShowFps = null);

/// <summary>
/// Menu rapide (appui court sur Guide / Home en jeu) : panneau à gauche de la TV, par-dessus le jeu.
/// Manette (batterie), temps de session, infos du jeu, Reprendre, Menu de la console, Quitter, Retour au PC.
/// La navigation vient de ConsoleWindow (Handle) ; le choix est rendu par l'évènement <see cref="Command"/>.
/// </summary>
public sealed class GuideMenuWindow : Window
{
    private const double DesignW = 500, DesignH = 1080;

    private readonly ConsoleTheme _t;
    private readonly GuideMenuInfo _info;
    private readonly List<(string Id, string Glyph, string Label)> _items = new();
    private readonly StackPanel _list = new();
    private readonly StackPanel _details = new();
    private readonly ScrollViewer _detailsScroll;
    private readonly TextBlock _session = new(), _clock = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Rect _bounds;
    private readonly TextBlock _hints = new();
    private CheatPanel? _cheats;
    private int _sel;
    private bool _quitArmed, _pcArmed, _closing;

    /// <summary>resume, menu, quit, pc. Rendu une seule fois pour quit / pc (après confirmation).</summary>
    public event Action<string>? Command;

    public GuideMenuWindow(ConsoleTheme theme, GuideMenuInfo info, Rect boundsPx)
    {
        _t = theme;
        _info = info;
        _bounds = boundsPx;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        FontFamily = new FontFamily(theme.Font);
        Title = "OXYN – menu";

        _items.Add(("resume", "", "Reprendre le jeu"));
        _items.Add(("info", "", "Infos du jeu"));
        if (info.Cheats != null) _items.Add(("cheats", "", "Triches"));
        if (info.ShowFps is { } fpsOn) _items.Add(("fps", "", FpsLabel(fpsOn)));
        _items.Add(("menu", "", "Menu de la console"));
        _items.Add(("quit", "", "Quitter le jeu"));
        _items.Add(("pc", "", "Retour au PC"));

        if (info.Cheats != null)
        {
            _cheats = new CheatPanel(theme, info.Cheats, _hints, info.Game, info.CheatSlipsToken) { Visibility = Visibility.Collapsed };
            _cheats.Sound += s => Sound?.Invoke(s);
        }
        _detailsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = _details, Visibility = Visibility.Collapsed };
        Content = new Viewbox { Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Child = Build() };
        RenderItems();
        _timer.Tick += (_, _) => UpdateTimes();
        UpdateTimes();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Affiche le panneau (glissé depuis la gauche) et lui donne le premier plan : le jeu perd le focus.</summary>
    public void Open()
    {
        Show();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var h = Math.Max(1, (int)_bounds.Height - 1);
        var w = (int)Math.Round(h * DesignW / DesignH);
        SetWindowPos(hwnd, new IntPtr(-1), (int)_bounds.X, (int)_bounds.Y, w, h, 0x0040);
        GameWindowTracker.AllowAnyForeground();
        GameWindowTracker.ForceForeground(hwnd);
        Activate();
        if (Content is FrameworkElement root)
        {
            var tt = new TranslateTransform(-DesignW, 0);
            ((Viewbox)root).Child.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-DesignW, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        _timer.Start();
    }

    /// <summary>Ferme le panneau (glissé vers la gauche).</summary>
    public void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        if (Content is Viewbox { Child.RenderTransform: TranslateTransform tt })
        {
            var a = new DoubleAnimation(0, -DesignW, TimeSpan.FromMilliseconds(160));
            a.Completed += (_, _) => Close();
            tt.BeginAnimation(TranslateTransform.XProperty, a);
        }
        else Close();
    }

    public bool DetailsOpen => _detailsScroll.Visibility == Visibility.Visible;

    private bool CheatsOpen => _cheats?.Visibility == Visibility.Visible;

    /// <summary>Bruitage demandé par la page Triches.</summary>
    public event Action<UiSound>? Sound;

    private void ShowCheats(bool on)
    {
        if (_cheats == null) return;
        _cheats.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) _cheats.Enter();
        else { _cheats.Leave(); _hints.Text = DefaultHints; }
    }

    private const string DefaultHints = "Ⓐ Choisir     Ⓑ Reprendre     Ⓨ Infos";

    /// <summary>Navigation manette. Renvoie vrai si le menu doit se fermer et le jeu reprendre (B, Guide).</summary>
    public bool Handle(PadButtons b)
    {
        if (_closing) return false;
        if (b == PadButtons.Guide) return true;
        if (CheatsOpen)
        {
            if (_cheats!.Handle(b)) ShowCheats(false);
            return false;
        }
        if (DetailsOpen)
        {
            switch (b)
            {
                case PadButtons.Down: _detailsScroll.ScrollToVerticalOffset(_detailsScroll.VerticalOffset + 60); break;
                case PadButtons.Up: _detailsScroll.ScrollToVerticalOffset(_detailsScroll.VerticalOffset - 60); break;
                case PadButtons.B: case PadButtons.A: case PadButtons.Y: ShowDetails(false); break;
                case PadButtons.Guide: return true;
            }
            return false;
        }
        switch (b)
        {
            case PadButtons.Up: Move(-1); break;
            case PadButtons.Down: Move(1); break;
            case PadButtons.A: Choose(); break;
            case PadButtons.Y: ShowDetails(true); break;
            case PadButtons.B: case PadButtons.Guide: return true;
        }
        return false;
    }

    private void Move(int delta)
    {
        var n = Math.Clamp(_sel + delta, 0, _items.Count - 1);
        if (n == _sel) return;
        _sel = n;
        Disarm();
        RenderItems();
        Moved?.Invoke();
    }

    /// <summary>Déplacement dans la liste (pour le bruitage).</summary>
    public event Action? Moved;

    private void Choose()
    {
        var id = _items[_sel].Id;
        switch (id)
        {
            case "info": ShowDetails(true); return;
            case "cheats": ShowCheats(true); Opened?.Invoke(); return;
            case "fps": Command?.Invoke(id); return;   // bascule : le menu reste ouvert (libellé mis à jour par SetFps)
            case "quit" when !_quitArmed: _quitArmed = true; _pcArmed = false; RenderItems(); Armed?.Invoke(); return;
            case "pc" when !_pcArmed: _pcArmed = true; _quitArmed = false; RenderItems(); Armed?.Invoke(); return;
        }
        Command?.Invoke(id);
    }

    /// <summary>Une page s'ouvre (pour le bruitage).</summary>
    public event Action? Opened;

    /// <summary>« Confirmer ? » affiché sur Quitter / Retour au PC.</summary>
    public event Action? Armed;

    private void Disarm() { _quitArmed = false; _pcArmed = false; }

    private void ShowDetails(bool on)
    {
        _detailsScroll.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _list.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on) _detailsScroll.ScrollToTop();
        Moved?.Invoke();
    }

    // ---------- Construction ----------

    private FrameworkElement Build()
    {
        var t = _t;
        var root = new Grid { Width = DesignW, Height = DesignH };
        root.Children.Add(new Border
        {
            Background = new LinearGradientBrush(t.WithAlpha(t.Panel, 0xF8), t.WithAlpha(t.Bg, 0xF0), 90),
            BorderBrush = t.Brush(t.WithAlpha(t.Accent, 0x90)),
            BorderThickness = new Thickness(0, 0, 3, 0),
        });
        var stack = new DockPanel { Margin = new Thickness(36, 40, 30, 36), LastChildFill = true };
        root.Children.Add(stack);

        // En-tête : heure + manette.
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 26) };
        _clock.FontSize = 30; _clock.FontWeight = FontWeights.SemiBold; _clock.Foreground = t.Brush(t.Text);
        DockPanel.SetDock(_clock, Dock.Right);
        top.Children.Add(_clock);
        top.Children.Add(ControllerRow());
        DockPanel.SetDock(top, Dock.Top);
        stack.Children.Add(top);

        // Jeu : jaquette + titre + plateforme + temps.
        var game = _info.Game;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 24) };
        var cover = Load(game.CoverPath, 240);
        if (cover != null)
        {
            var img = new Border
            {
                Width = 120, Height = 170, CornerRadius = new CornerRadius(Math.Max(6, t.Corner)), Margin = new Thickness(0, 0, 18, 0),
                Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill }, VerticalAlignment = System.Windows.VerticalAlignment.Top,
            };
            DockPanel.SetDock(img, Dock.Left);
            head.Children.Add(img);
        }
        var meta = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
        meta.Children.Add(new TextBlock { Text = game.Title, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = t.Brush(t.Text), TextWrapping = TextWrapping.Wrap, MaxHeight = 120 });
        meta.Children.Add(new TextBlock { Text = _info.Platform, FontSize = 20, Foreground = t.Brush(t.Accent), Margin = new Thickness(0, 6, 0, 10) });
        _session.FontSize = 20; _session.Foreground = t.Brush(t.SubText); _session.TextWrapping = TextWrapping.Wrap;
        meta.Children.Add(_session);
        head.Children.Add(meta);
        DockPanel.SetDock(head, Dock.Top);
        stack.Children.Add(head);

        var sep = new Border { Height = 2, Background = t.Brush(t.WithAlpha(t.Text, 0x22)), Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(sep, Dock.Top);
        stack.Children.Add(sep);

        // Pied : rappel des boutons.
        _hints.Text = DefaultHints;
        _hints.FontSize = 19;
        _hints.Foreground = t.Brush(t.SubText);
        _hints.Margin = new Thickness(0, 16, 0, 0);
        _hints.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(_hints, Dock.Bottom);
        stack.Children.Add(_hints);

        // Détails (description) à la place de la liste.
        _details.Children.Add(new TextBlock { Text = "Infos du jeu", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = t.Brush(t.Text), Margin = new Thickness(0, 0, 0, 12) });
        _details.Children.Add(new TextBlock
        {
            Text = $"Temps de jeu total : {ConsoleFormat.PlayTime(game.ShownPlayTime)}\nDernière session : {ConsoleFormat.LastPlayed(game.LastPlayed, DateTime.Now)}",
            FontSize = 20, Foreground = t.Brush(t.SubText), Margin = new Thickness(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap,
        });
        _details.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(game.Description) ? "Aucune description." : game.Description,
            FontSize = 21, LineHeight = 31, Foreground = t.Brush(t.Text), TextWrapping = TextWrapping.Wrap,
        });
        var center = new Grid();
        center.Children.Add(_list);
        center.Children.Add(_detailsScroll);
        if (_cheats != null) center.Children.Add(_cheats);
        stack.Children.Add(center);
        return root;
    }

    private FrameworkElement ControllerRow()
    {
        var t = _t;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        row.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 30, Foreground = t.Brush(t.Text), VerticalAlignment = System.Windows.VerticalAlignment.Center });
        var c = _info.Controller;
        string text;
        Brush color = t.Brush(t.Text);
        if (c == null) text = "Aucune manette";
        else if (c.Wired) text = "Filaire";
        else if (c.BatteryPercent < 0) text = "Batterie inconnue";
        else
        {
            text = $"{c.BatteryPercent} %{(c.Charging ? " ⚡" : "")}";
            if (c.BatteryPercent <= 20) color = new SolidColorBrush(Color.FromRgb(0xE5, 0x57, 0x4B));
            row.Children.Add(Battery(c.BatteryPercent, color));
        }
        row.Children.Add(new TextBlock { Text = text, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = color, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center });
        return row;
    }

    private FrameworkElement Battery(int percent, Brush fill)
    {
        var text = _t.Brush(_t.Text);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
        sp.Children.Add(new Border
        {
            Width = 44, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2), BorderBrush = text, Padding = new Thickness(2.5),
            Child = new Border { CornerRadius = new CornerRadius(2), Background = fill, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Width = Math.Max(3, 35 * Math.Clamp(percent, 0, 100) / 100.0) },
        });
        sp.Children.Add(new Border { Width = 4, Height = 9, CornerRadius = new CornerRadius(0, 2, 2, 0), Background = text, VerticalAlignment = System.Windows.VerticalAlignment.Center });
        return sp;
    }

    private static string FpsLabel(bool on) => on ? "Compteur FPS : affiché" : "Compteur FPS : masqué";

    /// <summary>Met à jour la ligne « Compteur FPS » après une bascule.</summary>
    public void SetFps(bool on)
    {
        var i = _items.FindIndex(x => x.Id == "fps");
        if (i < 0) return;
        _items[i] = ("fps", _items[i].Glyph, FpsLabel(on));
        RenderItems();
    }

    private void RenderItems()
    {
        var t = _t;
        _list.Children.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var (id, glyph, label) = _items[i];
            var on = i == _sel;
            if ((id == "quit" && _quitArmed) || (id == "pc" && _pcArmed)) label = "Confirmer ? (Ⓐ)";
            var fg = on ? t.Brush(t.OnAccent) : t.Brush(t.Text);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 26, Foreground = fg, Width = 44, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = label, FontSize = 25, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = fg, VerticalAlignment = System.Windows.VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340 });
            _list.Children.Add(new Border
            {
                Child = row, Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(Math.Max(6, t.Corner)),
                Background = on ? t.Brush(t.Accent) : Brushes.Transparent,
            });
            // Séparateur avant les actions qui ferment quelque chose.
            if (id == "info")
                _list.Children.Add(new Border { Height = 1, Background = t.Brush(t.WithAlpha(t.Text, 0x1A)), Margin = new Thickness(8, 4, 8, 12) });
        }
    }

    private void UpdateTimes()
    {
        _clock.Text = DateTime.Now.ToString("HH:mm");
        var s = DateTime.Now - _info.SessionStart;
        var session = s.TotalHours >= 1 ? $"{(int)s.TotalHours} h {s.Minutes:00} min" : $"{Math.Max(0, s.Minutes)} min";
        _session.Text = $"En jeu depuis {session}\nTotal : {ConsoleFormat.PlayTime(_info.Game.ShownPlayTime + s)}";
    }

    private static BitmapImage? Load(string? path, int width)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.DecodePixelWidth = width;
            b.UriSource = new Uri(path);
            b.EndInit();
            b.Freeze();
            return b;
        }
        catch { return null; }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
