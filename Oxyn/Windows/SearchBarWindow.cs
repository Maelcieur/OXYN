using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Oxyn.Controls;
using Oxyn.Infrastructure;
using Oxyn.Services;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Oxyn;

/// <summary>
/// Saisie de la Barre de recherche, posée par-dessus le module : champ, puis liste (recherche, adresse, favoris, suggestions
/// du moteur, commandes /xxx envoyées à l'IA de Google, /correction hors ligne). Haut/Bas, Entrée, Tab, Échap.
/// Se ferme dès qu'elle perd le focus.
/// </summary>
public sealed class SearchBarWindow : Window
{
    private const int MaxSuggestions = 6, MaxBookmarkMatches = 3;

    /// <summary>Ligne de la liste : symbole, titre, détail, action (Entrée ou clic) ; Complete = texte mis dans le champ par Tab.</summary>
    private sealed record Item(string Glyph, string Title, string? Detail, Action Run, string? Complete = null);

    private readonly SearchBarSettings _settings;
    private readonly Rect _field;
    private readonly TextBox _box = new();
    private readonly TextBlock _placeholder = new();
    private readonly StackPanel _list = new() { Margin = new Thickness(6, 0, 6, 6) };
    private readonly Border _listHost;
    private readonly DockPanel _layout = new();
    private readonly DispatcherTimer _suggestTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly List<Item> _items = new();
    private List<string> _suggestions = new();
    private string _suggestedFor = "";
    private CancellationTokenSource? _suggestCts;
    private IReadOnlyList<Bookmark>? _bookmarks;
    private readonly Stack<(string Name, IReadOnlyList<Bookmark> Items)> _folders = new();
    private bool _favoritesMode, _closing, _above;
    private int _selected;

    public SearchBarWindow(SearchBarSettings settings, Rect field, bool favorites)
    {
        _settings = settings;
        _field = field;
        _favoritesMode = favorites;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Title = "OXYN — Recherche";
        SetResourceReference(FontFamilyProperty, "TextFont");
        Width = Math.Max(field.Width, 320) + 24; // + marge de l'ombre
        Left = field.Left - 12;

        // Liste au-dessus du champ si la place manque en dessous (module en bas de l'écran).
        var work = SystemParameters.WorkArea;
        _above = work.Bottom - field.Bottom < 420 && field.Top - work.Top > work.Bottom - field.Bottom;
        Top = field.Top - 12;

        // Changer de moteur sans quitter la saisie : la liste est refaite (et les suggestions redemandées) aussitôt.
        var glass = Widgets.SearchBarWidgetContent.EnginePill(() => _settings, () =>
        {
            _suggestions = new();
            _suggestedFor = "";
            if (_settings.Suggestions && _box.Text.Trim().Length > 0 && SearchBar.ParseCommand(_box.Text) is null) _suggestTimer.Start();
            Rebuild();
            FocusInput();
        }, () => Dispatcher.BeginInvoke(() => { if (!IsActive) SafeClose(); }, DispatcherPriority.ApplicationIdle));
        Widgets.SearchBarWidgetContent.SetPillText(glass, settings);
        Widgets.SearchBarWidgetContent.SizePill(glass, field.Height > 0 ? field.Height : settings.BarHeight);
        var textSize = Widgets.SearchBarWidgetContent.TextSize(field.Height > 0 ? field.Height : settings.BarHeight) + 1;

        _box.Background = Brushes.Transparent;
        _box.BorderThickness = new Thickness(0);
        _box.FontSize = textSize;
        _box.VerticalContentAlignment = VerticalAlignment.Center;
        _box.SetResourceReference(ForegroundProperty, "TextBrush");
        _box.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        _box.SetResourceReference(TextBox.SelectionBrushProperty, "AccentBrush");
        _box.TextChanged += (_, _) => OnTextChanged();
        _box.PreviewKeyDown += OnBoxKey;

        _placeholder.Text = Widgets.SearchBarWidgetContent.Placeholder(settings);
        _placeholder.FontSize = textSize;
        _placeholder.IsHitTestVisible = false;
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.Margin = new Thickness(2, 0, 0, 0);
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");

        var star = new Border
        {
            Width = 34, Height = 30, Margin = new Thickness(4, 0, 5, 0), CornerRadius = new CornerRadius(15), Cursor = Cursors.Hand,
            Background = Brushes.Transparent, ToolTip = "Favoris du navigateur", Child = Symbol("\uE70D", 12, sub: true),
            Visibility = settings.Bookmarks ? Visibility.Visible : Visibility.Collapsed
        };
        star.MouseEnter += (_, _) => star.SetResourceReference(Border.BackgroundProperty, "ButtonBrush");
        star.MouseLeave += (_, _) => star.Background = Brushes.Transparent;
        star.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _favoritesMode = !_favoritesMode || _box.Text.Length > 0;
            _folders.Clear();
            if (_box.Text.Length > 0) _box.Clear(); else Rebuild();
            _box.Focus();
        };

        var input = new Grid { Height = field.Height > 0 ? field.Height : 42 };
        input.Children.Add(_box);
        input.Children.Add(_placeholder);
        var row = new DockPanel();
        DockPanel.SetDock(glass, Dock.Left);
        DockPanel.SetDock(star, Dock.Right);
        row.Children.Add(glass);
        row.Children.Add(star);
        row.Children.Add(input);

        _listHost = new Border { Child = _list, Visibility = Visibility.Collapsed };
        DockPanel.SetDock(row, _above ? Dock.Bottom : Dock.Top);
        DockPanel.SetDock(_listHost, _above ? Dock.Top : Dock.Bottom);
        _layout.Children.Add(row);
        _layout.Children.Add(_listHost);

        var card = new Border
        {
            Margin = new Thickness(12), CornerRadius = new CornerRadius(Math.Clamp(field.Height / 2, 12, 24)), BorderThickness = new Thickness(1), Child = _layout,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 * settings.PopupOpacity, Color = Colors.Black }
        };
        // Couleurs de la barre ; fond presque opaque pour que la liste reste lisible par-dessus n'importe quoi.
        card.Background = Widgets.SearchBarWidgetContent.LookBrush(settings.BarColor, settings.PopupOpacity, Color.FromRgb(0x1B, 0x1D, 0x22));
        card.BorderBrush = Widgets.SearchBarWidgetContent.LookBrush(settings.BorderColor, settings.BorderOpacity, Colors.White);
        Widgets.SearchBarWidgetContent.ApplyContrast(this, settings.BarColor, settings.PopupOpacity);
        Content = card;

        _suggestTimer.Tick += (_, _) => { _suggestTimer.Stop(); _ = FetchSuggestionsAsync(); };
        SizeChanged += (_, _) => { if (_above) Top = _field.Bottom + 12 - ActualHeight; };
        Deactivated += (_, _) => { if (!Widgets.SearchBarWidgetContent.EngineMenuOpen) SafeClose(); };
        Loaded += (_, _) => Rebuild();
        ContentRendered += (_, _) => FocusInput();
    }

    private static TextBlock Symbol(string glyph, double size, bool sub = false)
    {
        var t = new TextBlock
        {
            Text = glyph, FontFamily = new FontFamily(ShortcutIcons.SymbolFont), FontSize = size,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, sub ? "SubTextBrush" : "TextBrush");
        return t;
    }

    /// <summary>Le module est sur le bureau (autre fenêtre au premier plan) : on force le premier plan pour pouvoir taper.</summary>
    private void FocusInput()
    {
        Activate();
        if (!IsActive) ConsoleMode.GameWindowTracker.ForceForeground(new WindowInteropHelper(this).Handle);
        _box.Focus();
        Keyboard.Focus(_box);
    }

    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        _suggestTimer.Stop();
        _suggestCts?.Cancel();
        // Close() pendant Deactivated lève une exception dans certains cas : reporté.
        Dispatcher.BeginInvoke(Close, DispatcherPriority.Background);
    }

    // ---------- Saisie ----------

    private void OnTextChanged()
    {
        _placeholder.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_box.Text.Length > 0) _favoritesMode = false;
        _suggestTimer.Stop();
        var text = _box.Text.Trim();
        if (_settings.Suggestions && text.Length > 0 && SearchBar.ParseCommand(text) is null && text != _suggestedFor)
            _suggestTimer.Start();
        if (text.Length == 0) { _suggestions = new(); _suggestedFor = ""; }
        Rebuild();
    }

    private async Task FetchSuggestionsAsync()
    {
        var text = _box.Text.Trim();
        _suggestCts?.Cancel();
        var cts = _suggestCts = new CancellationTokenSource();
        var result = await SearchBar.FetchSuggestionsAsync(SearchBar.Engine(_settings), text, cts.Token);
        if (cts.IsCancellationRequested || _closing || _box.Text.Trim() != text) return;
        _suggestions = result;
        _suggestedFor = text;
        Rebuild(keepSelection: true);
    }

    private void OnBoxKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Select(_selected + 1);
                e.Handled = true;
                break;
            case Key.Up:
                Select(_selected - 1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (_selected >= 0 && _selected < _items.Count) _items[_selected].Run();
                e.Handled = true;
                break;
            case Key.Tab:
                if (_selected >= 0 && _selected < _items.Count && _items[_selected].Complete is { } complete) SetText(complete);
                e.Handled = true;
                break;
            case Key.Escape:
                if (_folders.Count > 0) { _folders.Pop(); Rebuild(); }
                else if (_box.Text.Length > 0) _box.Clear();
                else SafeClose();
                e.Handled = true;
                break;
        }
    }

    private void SetText(string text)
    {
        _box.Text = text;
        _box.CaretIndex = text.Length;
        _box.Focus();
    }

    // ---------- Liste ----------

    private void Rebuild(bool keepSelection = false)
    {
        var previous = keepSelection && _selected >= 0 && _selected < _items.Count ? _items[_selected].Title : null;
        _items.Clear();
        var text = _box.Text.Trim();
        if (_favoritesMode || (text.Length == 0 && _settings.Bookmarks && _folders.Count > 0)) AddFavorites();
        else if (text.Length == 0) { /* rien : la liste reste fermée */ }
        else if (SearchBar.ParseCommand(_box.Text) is { } command) AddCommandItems(command.Name, command.Argument, _box.Text.TrimStart().Contains(' '));
        else AddSearchItems(text);

        _list.Children.Clear();
        for (var i = 0; i < _items.Count; i++) _list.Children.Add(BuildRow(_items[i], i));
        _listHost.Visibility = _items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var index = previous is null ? 0 : _items.FindIndex(it => it.Title == previous);
        Select(index < 0 ? 0 : index);
    }

    private void AddSearchItems(string text)
    {
        var engine = SearchBar.Engine(_settings);
        if (SearchBar.AsUrl(text) is { } url) _items.Add(new("\uE774", $"Ouvrir {text}", url, () => Go(url)));
        _items.Add(new("\uE721", text, $"Rechercher avec {engine.Name}", () => Go(SearchBar.SearchUrl(engine, text))));
        if (_settings.Bookmarks)
            foreach (var b in SearchBar.FindBookmarks(Bookmarks, text).DistinctBy(b => b.Url).Take(MaxBookmarkMatches))
                _items.Add(new("\uE734", b.Name, b.Url, () => Go(b.Url!)));
        if (_suggestedFor == text || _suggestions.Count > 0)
            foreach (var s in _suggestions.Where(s => !string.Equals(s, text, StringComparison.CurrentCultureIgnoreCase)).Take(MaxSuggestions))
                _items.Add(new("\uE721", s, null, () => Go(SearchBar.SearchUrl(engine, s)), s));
    }

    /// <summary>Commandes choisies par l'utilisateur (aucune par défaut), avec leur nom tel qu'il se tape après « / ».</summary>
    private IEnumerable<(string Name, SearchCommand Command)> AllCommands()
        => _settings.Commands.Where(c => c.Name.Trim().Length > 0).Select(c => (SearchBar.CommandKey(c.Name), c));

    private static string Describe(SearchCommand c)
        => c.Correction ? "Corrige l'orthographe (correcteur de Windows, hors ligne) et copie le texte" : c.Prompt;

    private void AddCommandItems(string name, string argument, bool typedSpace)
    {
        if (_settings.Commands.Count == 0)
        {
            _items.Add(new("\uE7BA", "Aucune commande", "Choisis tes commandes dans Paramètres › Modules › Barre de recherche", () => { }));
            return;
        }
        var known = AllCommands().FirstOrDefault(c => c.Name == name).Command;
        if (typedSpace && known is not null)
        {
            if (known.Correction)
            {
                if (argument.Length == 0) { _items.Add(new("\uE8AC", "Tape le texte à corriger", null, () => { })); return; }
                var corrected = SpellCorrector.Correct(argument);
                if (corrected is null) { _items.Add(new("\uE7BA", "Correcteur de Windows indisponible", "Installe le français dans les langues de Windows", () => { })); return; }
                _items.Add(new("\uE8C8", corrected, corrected == argument ? "Aucune faute trouvée · Entrée pour copier" : "Texte corrigé · Entrée pour copier", () => CopyAndClose(corrected)));
                return;
            }
            var prompt = SearchBar.BuildPrompt(known.Prompt, argument);
            _items.Add(new("\uE8BD", argument.Length == 0 ? $"/{name}" : prompt, "Demander à l'IA de Google", () => Go(SearchBar.AiUrl(prompt))));
            return;
        }
        // Nom en cours de frappe : commandes correspondantes ; Tab ou Entrée complète « /nom ».
        foreach (var (n, command) in AllCommands().Where(c => c.Name.StartsWith(name, StringComparison.Ordinal)))
        {
            var complete = $"/{n} ";
            _items.Add(new(command.Correction ? "\uE8AC" : "\uE8BD", $"/{n}", Describe(command), () => SetText(complete), complete));
        }
        if (_items.Count == 0) _items.Add(new("\uE7BA", $"Commande inconnue : /{name}", "Commandes modifiables dans Paramètres › Modules › Barre de recherche", () => { }));
    }

    /// <summary>Favoris choisis dans les réglages (adresses invalides ignorées).</summary>
    private IReadOnlyList<Bookmark> Bookmarks => _bookmarks ??= _settings.Favorites
        .Where(f => f.Url.Trim().Length > 0)
        .Select(f => new Bookmark(f.Name.Trim().Length > 0 ? f.Name.Trim() : f.Url.Trim(), SearchBar.AsUrl(f.Url) ?? f.Url.Trim(), Array.Empty<Bookmark>()))
        .ToList();

    private void AddFavorites()
    {
        if (_folders.Count > 0)
        {
            _items.Add(new("\uE72B", _folders.Peek().Name, "Retour", () => { _folders.Pop(); Rebuild(); }));
        }
        var items = _folders.Count > 0 ? _folders.Peek().Items : Bookmarks;
        if (items.Count == 0 && _folders.Count == 0)
        {
            _items.Add(new("\uE734", "Aucun favori", "Choisis tes favoris dans Paramètres › Modules › Barre de recherche", () => { }));
            return;
        }
        foreach (var b in items)
        {
            if (b.IsFolder)
            {
                var folder = b;
                _items.Add(new("\uE8B7", b.Name, $"{b.Children.Count} élément{(b.Children.Count > 1 ? "s" : "")}",
                    () => { _folders.Push((folder.Name, folder.Children)); Rebuild(); }));
            }
            else _items.Add(new("\uE734", b.Name, b.Url, () => Go(b.Url!)));
        }
    }

    private Border BuildRow(Item item, int index)
    {
        var title = new TextBlock { Text = item.Title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title } };
        if (!string.IsNullOrEmpty(item.Detail))
        {
            var detail = new TextBlock { Text = item.Detail, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
            text.Children.Add(detail);
        }
        var icon = Symbol(item.Glyph, 14, sub: true);
        icon.Width = 22;
        icon.Margin = new Thickness(6, 0, 10, 0);
        var row = new DockPanel { Children = { icon, text } };
        DockPanel.SetDock(icon, Dock.Left);
        var border = new Border
        {
            MinHeight = 38, Padding = new Thickness(4, 5, 10, 5), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand,
            Background = Brushes.Transparent, Child = row
        };
        border.MouseEnter += (_, _) => Select(index);
        border.MouseLeftButtonUp += (_, e) => { e.Handled = true; item.Run(); };
        return border;
    }

    private void Select(int index)
    {
        if (_items.Count == 0) { _selected = -1; return; }
        _selected = (index % _items.Count + _items.Count) % _items.Count;
        for (var i = 0; i < _list.Children.Count; i++)
        {
            var border = (Border)_list.Children[i];
            if (i == _selected) border.SetResourceReference(Border.BackgroundProperty, "ButtonBrush");
            else border.Background = Brushes.Transparent;
        }
    }

    // ---------- Actions ----------

    private void Go(string url)
    {
        SearchBar.Open(url);
        SafeClose();
    }

    private void CopyAndClose(string text)
    {
        try
        {
            Clipboard.SetText(text);
            TrayIconService.ShowNotice("Texte corrigé copié dans le presse-papiers.");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            Log.Warn("Presse-papiers occupé : texte corrigé non copié", ex);
        }
        SafeClose();
    }
}
