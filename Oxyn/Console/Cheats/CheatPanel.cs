using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Oxyn.Infrastructure;

namespace Oxyn.ConsoleMode.Cheats;

/// <summary>
/// Page « Triches » du menu Guide (jeux d'émulateur), tout à la manette :
/// - valeurs enregistrées : ◀ ▶ −1 / +1, LB / RB −10 / +10, Ⓐ valeur exacte, Ⓧ bloquer, Ⓨ supprimer ;
/// - Nouvelle recherche : on saisit la valeur affichée en jeu, on la fait changer, on affine, puis on la nomme.
/// </summary>
public sealed partial class CheatPanel : Border
{
    private enum Mode { List, Picker, Busy, Names, Message, Codes }
    private enum PickFor { Search, Refine, Set }

    private const int Digits = 6, MaxResultsToName = 8;
    private int _distinct = int.MaxValue;

    private readonly ConsoleTheme _t;
    private readonly Func<CheatSession?> _open;
    private readonly StackPanel _body = new();
    private readonly TextBlock _hints;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private CheatSession? _session;
    private string? _openError;
    private Mode _mode = Mode.List;
    private PickFor _pickFor;
    private LiveCheat? _target;
    private int _sel, _cursor, _nameSel;
    private readonly int[] _digits = new int[Digits];
    private string _message = "", _busyText = "";
    private double _progress;
    private CancellationTokenSource? _busyCts;
    private bool _deleteArmed;

    /// <summary>Bruitage / vibration à jouer (déplacement, validation, erreur...).</summary>
    public event Action<UiSound>? Sound;

    public CheatPanel(ConsoleTheme theme, Func<CheatSession?> open, TextBlock hints, ConsoleGame? game = null, string? cheatSlipsToken = null)
    {
        _t = theme;
        _game = game;
        _cheatSlipsToken = cheatSlipsToken;
        _open = open;
        _hints = hints;
        Child = _body;
        _refresh.Tick += (_, _) => { if (_mode == Mode.List && _session != null) Render(); };
        Unloaded += (_, _) => { _refresh.Stop(); _busyCts?.Cancel(); };
    }

    /// <summary>Affiche la page (ouvre la session de l'émulateur à la première fois).</summary>
    public void Enter()
    {
        if (_session == null && _openError == null)
        {
            try { _session = _open(); }
            catch (Exception ex)
            {
                Log.Warn("Triches : accès à la mémoire de l'émulateur impossible", ex);
                _openError = "Accès à la mémoire de l'émulateur impossible.\n(Émulateur lancé en administrateur ?)";
            }
        }
        _mode = _openError != null ? Mode.Message : Mode.List;
        if (_openError != null) _message = _openError;
        // Mémoire inaccessible : les codes de triche restent utilisables.
        if (_openError != null && CodesAvailable) { OpenCodes(); return; }
        _sel = Math.Min(_sel, Math.Max(0, Rows().Count - 1));
        _refresh.Start();
        Render();
        // Valeurs enregistrées qui ont changé de place depuis le dernier lancement : on les cherche par leur empreinte.
        if (_session?.NeedsRelocation == true)
            RunBusy("Recherche de tes valeurs enregistrées…", (s, p, ct) => s.Relocate(p, ct), relocation: true);
    }

    public void Leave()
    {
        _refresh.Stop();
    }

    /// <summary>Navigation. Vrai = retour à la liste du menu Guide.</summary>
    public bool Handle(PadButtons b)
    {
        switch (_mode)
        {
            case Mode.Busy:
                if (b == PadButtons.B) { _busyCts?.Cancel(); Sound?.Invoke(UiSound.Back); }
                return false;
            case Mode.Message:
                if (b is PadButtons.A or PadButtons.B)
                {
                    Sound?.Invoke(UiSound.Back);
                    if (_openError != null) return true;
                    _mode = Mode.List;
                    Render();
                }
                return false;
            case Mode.Picker: HandlePicker(b); return false;
            case Mode.Names: HandleNames(b); return false;
            case Mode.Codes: return HandleCodes(b);
            default: return HandleList(b);
        }
    }

    // ---------- Liste ----------

    private enum RowKind { Codes, Cheat, Refine, SaveNow, NewSearch, CancelSearch }

    private List<(RowKind Kind, LiveCheat? Cheat)> Rows()
    {
        var rows = new List<(RowKind, LiveCheat?)>();
        if (CodesAvailable) rows.Add((RowKind.Codes, null));
        if (_session == null) return rows;
        foreach (var c in _session.Cheats) rows.Add((RowKind.Cheat, c));
        if (_session.Search != null)
        {
            rows.Add((RowKind.Refine, null));
            if (_distinct <= CheatSession.MaxSaved) rows.Add((RowKind.SaveNow, null));
            rows.Add((RowKind.CancelSearch, null));
        }
        else rows.Add((RowKind.NewSearch, null));
        return rows;
    }

    private bool HandleList(PadButtons b)
    {
        var rows = Rows();
        if (rows.Count == 0) return b == PadButtons.B;
        _sel = Math.Clamp(_sel, 0, rows.Count - 1);
        var (kind, cheat) = rows[_sel];
        if (b != PadButtons.Y) _deleteArmed = false;
        switch (b)
        {
            case PadButtons.B: Sound?.Invoke(UiSound.Back); return true;
            case PadButtons.Up: if (_sel > 0) { _sel--; Sound?.Invoke(UiSound.Move); } break;
            case PadButtons.Down: if (_sel < rows.Count - 1) { _sel++; Sound?.Invoke(UiSound.Move); } break;
            case PadButtons.Left when kind == RowKind.Cheat: Step(cheat!, -1); break;
            case PadButtons.Right when kind == RowKind.Cheat: Step(cheat!, 1); break;
            case PadButtons.PrevTab when kind == RowKind.Cheat: Step(cheat!, -10); break;
            case PadButtons.NextTab when kind == RowKind.Cheat: Step(cheat!, 10); break;
            case PadButtons.X when kind == RowKind.Cheat && cheat!.Found:
                _session!.ToggleFreeze(cheat);
                Sound?.Invoke(UiSound.Select);
                break;
            case PadButtons.Y when kind == RowKind.Cheat:
                if (!_deleteArmed) { _deleteArmed = true; Sound?.Invoke(UiSound.Back); break; }
                _deleteArmed = false;
                _session!.Remove(cheat!);
                Sound?.Invoke(UiSound.Select);
                break;
            case PadButtons.A:
                switch (kind)
                {
                    case RowKind.Cheat when cheat!.Found:
                        _target = cheat;
                        OpenPicker(PickFor.Set, (long)Math.Round(_session!.Value(cheat) ?? 0));
                        break;
                    case RowKind.Codes: OpenCodes(); return false;
                    case RowKind.NewSearch: OpenPicker(PickFor.Search, 0); break;
                    case RowKind.Refine: OpenPicker(PickFor.Refine, _session!.LastSearchValue); break;
                    case RowKind.SaveNow: OpenNames(); break;
                    case RowKind.CancelSearch:
                        _session!.CancelSearch();
                        Sound?.Invoke(UiSound.Back);
                        break;
                }
                break;
        }
        Render();
        return false;
    }

    private void Step(LiveCheat c, int delta)
    {
        if (_session?.Value(c) is not { } v) { Sound?.Invoke(UiSound.Error); return; }
        _session.Set(c, Math.Max(0, v + delta));
        Sound?.Invoke(UiSound.Move);
    }

    // ---------- Saisie d'un nombre ----------

    private void OpenPicker(PickFor purpose, long start)
    {
        _pickFor = purpose;
        start = Math.Clamp(start, 0, 999999);
        for (var i = Digits - 1; i >= 0; i--) { _digits[i] = (int)(start % 10); start /= 10; }
        _cursor = Digits - 1;
        _mode = Mode.Picker;
        Sound?.Invoke(UiSound.Open);
    }

    private long PickedValue() => _digits.Aggregate(0L, (acc, d) => acc * 10 + d);

    private void HandlePicker(PadButtons b)
    {
        switch (b)
        {
            case PadButtons.Left: _cursor = Math.Max(0, _cursor - 1); Sound?.Invoke(UiSound.Move); break;
            case PadButtons.Right: _cursor = Math.Min(Digits - 1, _cursor + 1); Sound?.Invoke(UiSound.Move); break;
            case PadButtons.Up: _digits[_cursor] = (_digits[_cursor] + 1) % 10; Sound?.Invoke(UiSound.Move); break;
            case PadButtons.Down: _digits[_cursor] = (_digits[_cursor] + 9) % 10; Sound?.Invoke(UiSound.Move); break;
            case PadButtons.B: _mode = Mode.List; Sound?.Invoke(UiSound.Back); break;
            case PadButtons.A: ConfirmPicker(); return;
        }
        Render();
    }

    private void ConfirmPicker()
    {
        var value = PickedValue();
        Sound?.Invoke(UiSound.Select);
        switch (_pickFor)
        {
            case PickFor.Set:
                if (_target != null) _session!.Set(_target, value);
                _mode = Mode.List;
                Render();
                return;
            case PickFor.Search:
                if (value == 0) { ShowMessage("Cherche une valeur différente de 0 (il y a des millions de zéros en mémoire)."); return; }
                RunBusy("Recherche dans la mémoire…", (s, p, ct) => s.StartSearch(value, p, ct));
                return;
            case PickFor.Refine:
                RunBusy("Affinage…", (s, p, ct) => s.RefineSearch(value, p, ct));
                return;
        }
    }

    // ---------- Recherche (en arrière-plan) ----------

    private async void RunBusy(string text, Action<CheatSession, Action<double>, CancellationToken> work, bool relocation = false)
    {
        var session = _session;
        if (session == null) return;
        _busyCts?.Cancel();
        var cts = _busyCts = new CancellationTokenSource();
        _mode = Mode.Busy;
        _busyText = text;
        _progress = 0;
        Render();
        var lastUi = 0L;
        void Progress(double p)
        {
            var now = Environment.TickCount64;
            if (now - lastUi < 150 && p < 1) return;
            lastUi = now;
            Dispatcher.BeginInvoke(() => { _progress = p; if (_mode == Mode.Busy) Render(); });
        }
        try
        {
            await Task.Run(() => work(session, Progress, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!relocation) session.CancelSearch();
            ShowMessage("Recherche annulée.");
            return;
        }
        catch (Exception ex)
        {
            Log.Warn("Triches : recherche impossible", ex);
            if (!relocation) session.CancelSearch();
            ShowMessage("La recherche a échoué (émulateur fermé ?).");
            return;
        }
        if (!relocation) { AfterSearch(session); return; }
        var missing = session.Cheats.Where(c => !c.Found).Select(c => c.Name).ToList();
        if (missing.Count > 0)
            ShowMessage($"Pas retrouvé : {string.Join(", ", missing)}.\nSupprime la ligne (Ⓨ deux fois) et refais une recherche.");
        else { _mode = Mode.List; Render(); }
    }

    private void AfterSearch(CheatSession s)
    {
        var count = s.Search?.Count ?? 0;
        _distinct = s.DistinctResults();
        if (count == 0)
        {
            s.CancelSearch();
            ShowMessage("Aucun résultat.\nLe jeu ne stocke peut-être pas cette valeur telle qu'affichée (ex. cœurs comptés par quarts : 3 cœurs = 12). Recommence avec une autre valeur.");
            return;
        }
        // Peu d'adresses, ou plus rien ne s'élimine (copies de la même valeur) : on peut la nommer.
        if (_distinct <= MaxResultsToName || (s.Search!.Stalled && _distinct <= CheatSession.MaxSaved))
        {
            OpenNames();
            return;
        }
        _sel = Rows().FindIndex(r => r.Kind == RowKind.Refine);
        ShowMessage($"{count:N0} résultats.\n\nReprends le jeu, fais changer cette valeur (dépense une bombe, perds une vie...), puis reviens ici : « Affiner » avec la nouvelle valeur.");
    }

    // ---------- Nom de la valeur trouvée ----------

    private void OpenNames()
    {
        _mode = Mode.Names;
        _nameSel = 0;
        Sound?.Invoke(UiSound.Launch);
        Render();
    }

    private void HandleNames(PadButtons b)
    {
        var n = CheatSession.Names.Length;
        switch (b)
        {
            case PadButtons.Up: _nameSel = (_nameSel + n - 1) % n; Sound?.Invoke(UiSound.Move); break;
            case PadButtons.Down: _nameSel = (_nameSel + 1) % n; Sound?.Invoke(UiSound.Move); break;
            case PadButtons.B: _mode = Mode.List; Sound?.Invoke(UiSound.Back); break;
            case PadButtons.A:
                var live = _session?.SaveSearch(CheatSession.Names[_nameSel]);
                Sound?.Invoke(live != null ? UiSound.Select : UiSound.Error);
                _mode = Mode.List;
                if (live != null) _sel = _session!.Cheats.IndexOf(live);
                break;
        }
        Render();
    }

    private void ShowMessage(string text)
    {
        _message = text;
        _mode = Mode.Message;
        Render();
    }

    // ---------- Affichage ----------

    private void Render()
    {
        var t = _t;
        _body.Children.Clear();
        Title(_mode switch { Mode.Names => "Valeur trouvée !", Mode.Picker => PickerTitle(), Mode.Codes => "Codes de triche", _ => "Triches" });
        switch (_mode)
        {
            case Mode.Message:
                Text(_message, t.Text, 21);
                SetHints("Ⓐ OK");
                break;

            case Mode.Busy:
                Text(_busyText, t.Text, 22);
                var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Background = t.Brush(t.WithAlpha(t.Text, 0x30)), Margin = new Thickness(0, 18, 0, 8) };
                track.Child = new Border { CornerRadius = new CornerRadius(5), Background = t.Brush(t.Accent), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Width = 420 * Math.Clamp(_progress, 0, 1) };
                _body.Children.Add(track);
                Text($"{_progress * 100:0} %", t.SubText, 20);
                SetHints("Ⓑ Annuler");
                break;

            case Mode.Picker:
                RenderPicker();
                SetHints("◀ ▶ Chiffre     ▲ ▼ Changer     Ⓐ Valider     Ⓑ Retour");
                break;

            case Mode.Names:
                Text("Comment s'appelle cette valeur ?", t.SubText, 20);
                // Fenêtre de 8 noms autour de la sélection.
                var first = Math.Clamp(_nameSel - 3, 0, Math.Max(0, CheatSession.Names.Length - 8));
                for (var i = first; i < Math.Min(CheatSession.Names.Length, first + 8); i++)
                    _body.Children.Add(Row(CheatSession.Names[i], null, i == _nameSel, false));
                SetHints("Ⓐ Enregistrer     Ⓑ Plus tard");
                break;

            case Mode.Codes:
                RenderCodes();
                break;

            default:
                RenderList();
                break;
        }
    }

    private string PickerTitle() => _pickFor switch
    {
        PickFor.Search => "Valeur affichée en jeu",
        PickFor.Refine => "Nouvelle valeur",
        _ => _target?.Name ?? "Valeur",
    };

    private void RenderList()
    {
        var t = _t;
        var rows = Rows();
        if (_session == null) { SetHints("Ⓑ Retour"); return; }
        if (_session.Cheats.Count == 0)
            Text("Aucune valeur enregistrée pour ce jeu. Lance une recherche : bombes, vies, argent...", t.SubText, 19);
        for (var i = 0; i < rows.Count; i++)
        {
            var (kind, c) = rows[i];
            var on = i == _sel;
            switch (kind)
            {
                case RowKind.Codes:
                    var n = EnabledCodes;
                    _body.Children.Add(Row(n > 0 ? $"Codes de triche ({n} activé{(n > 1 ? "s" : "")})" : "Codes de triche", null, on, false, "\uE943"));
                    break;
                case RowKind.Cheat:
                    var v = _session.Value(c!);
                    var value = !c!.Found ? "introuvable" : v is { } x ? CheatValue.Format(x) : "—";
                    var name = on && _deleteArmed ? "Supprimer ? (Ⓨ)" : c.Name;
                    _body.Children.Add(Row(name, value, on, c.Frozen));
                    break;
                case RowKind.Refine:
                    _body.Children.Add(Row($"Affiner ({_session.Search!.Count:N0} résultats)", null, on, false, ""));
                    break;
                case RowKind.SaveNow:
                    _body.Children.Add(Row($"Enregistrer ({_distinct} adresse{(_distinct > 1 ? "s" : "")})", null, on, false, ""));
                    break;
                case RowKind.CancelSearch:
                    _body.Children.Add(Row("Annuler la recherche", null, on, false, ""));
                    break;
                default:
                    _body.Children.Add(Row("Nouvelle recherche", null, on, false, ""));
                    break;
            }
        }
        var sel = _sel < rows.Count ? rows[_sel].Kind : RowKind.NewSearch;
        SetHints(sel == RowKind.Cheat
            ? "◀ ▶ ±1   LB RB ±10   Ⓐ Valeur   Ⓧ Bloquer   Ⓨ Suppr."
            : "Ⓐ Choisir     Ⓑ Retour");
    }

    private void RenderPicker()
    {
        var t = _t;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 24) };
        for (var i = 0; i < Digits; i++)
        {
            var on = i == _cursor;
            row.Children.Add(new Border
            {
                Width = 58, Height = 82, Margin = new Thickness(4, 0, 4, 0), CornerRadius = new CornerRadius(Math.Max(6, t.Corner)),
                Background = on ? t.Brush(t.Accent) : t.Brush(t.WithAlpha(t.Text, 0x18)),
                Child = new TextBlock
                {
                    Text = _digits[i].ToString(), FontSize = 46, FontWeight = FontWeights.SemiBold,
                    Foreground = on ? t.Brush(t.OnAccent) : t.Brush(t.Text),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                },
            });
        }
        _body.Children.Add(row);
        if (_pickFor == PickFor.Search)
            Text("Entre le nombre que le jeu affiche (bombes, rubis, vies...).", t.SubText, 19);
    }

    private FrameworkElement Row(string label, string? value, bool on, bool frozen, string? glyph = null)
    {
        var t = _t;
        var fg = on ? t.Brush(t.OnAccent) : t.Brush(t.Text);
        var dock = new DockPanel();
        if (value != null)
        {
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            if (frozen) right.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, Foreground = fg, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center });
            right.Children.Add(new TextBlock { Text = value, FontSize = 25, FontWeight = FontWeights.Bold, Foreground = fg, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            DockPanel.SetDock(right, Dock.Right);
            dock.Children.Add(right);
        }
        if (glyph != null)
        {
            var g = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 24, Foreground = fg, Width = 40, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            DockPanel.SetDock(g, Dock.Left);
            dock.Children.Add(g);
        }
        dock.Children.Add(new TextBlock { Text = label, FontSize = 24, FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal, Foreground = fg, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = System.Windows.VerticalAlignment.Center });
        return new Border
        {
            Child = dock, Padding = new Thickness(18, 13, 18, 13), Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(Math.Max(6, t.Corner)), Background = on ? t.Brush(t.Accent) : Brushes.Transparent,
        };
    }

    private void Title(string text) =>
        _body.Children.Add(new TextBlock { Text = text, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = _t.Brush(_t.Text), Margin = new Thickness(0, 0, 0, 14) });

    private void Text(string text, Color color, double size) =>
        _body.Children.Add(new TextBlock { Text = text, FontSize = size, LineHeight = size * 1.4, Foreground = _t.Brush(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });

    private void SetHints(string text) => _hints.Text = text;
}
