namespace Oxyn.ConsoleMode.Cheats;

/// <summary>
/// Page « Codes de triche » du menu Guide : codes Atmosphère du jeu (fichiers .txt, bases en ligne), activés un par un
/// à la manette (désactivés par défaut), recherche en ligne. Les codes cochés s'appliquent au prochain lancement du jeu.
/// </summary>
public sealed partial class CheatPanel
{
    private readonly ConsoleGame? _game;
    private readonly string? _cheatSlipsToken;
    private Dictionary<string, GameCheatCodes>? _codesAll;
    private GameCheatCodes? _codes;
    private string? _titleId;
    private int _codeSel;
    private bool _codesBusy;
    private string _codesMessage = "";

    /// <summary>Le jeu a un identifiant Switch : ses codes peuvent être gérés.</summary>
    private bool CodesAvailable => _game != null && PlayTimeSources.TitleIdOf(_game) != null;

    private int EnabledCodes
    {
        get
        {
            LoadCodes();
            return _codes?.Codes.Count(c => c.Enabled) ?? 0;
        }
    }

    private void LoadCodes()
    {
        if (_codes != null || _game == null || PlayTimeSources.TitleIdOf(_game) is not { } tid) return;
        _titleId = tid;
        _codesAll = CheatCodes.Load();
        if (!_codesAll.TryGetValue(tid, out var g)) _codesAll[tid] = g = new GameCheatCodes();
        if (g.BuildId == null && CheatCodes.FindBuildId(tid) is { } bid) g.BuildId = bid;
        _codes = g;
    }

    private void OpenCodes()
    {
        LoadCodes();
        if (_codes == null) return;
        _mode = Mode.Codes;
        _codeSel = 0;
        _codesMessage = _codes.Codes.Count == 0 ? "Aucun code pour ce jeu : cherche en ligne, ou ajoute un fichier .txt dans les Paramètres (bouton Triches)." : "";
        Sound?.Invoke(UiSound.Open);
        Render();
    }

    /// <summary>Ligne 0 = « Chercher en ligne », puis un code par ligne. Vrai = retour au menu Guide.</summary>
    private bool HandleCodes(PadButtons b)
    {
        if (_codes == null) return true;
        if (_codesBusy) return false;
        var count = 1 + _codes.Codes.Count;
        switch (b)
        {
            case PadButtons.Up: if (_codeSel > 0) { _codeSel--; Sound?.Invoke(UiSound.Move); } break;
            case PadButtons.Down: if (_codeSel < count - 1) { _codeSel++; Sound?.Invoke(UiSound.Move); } break;
            case PadButtons.B:
                Sound?.Invoke(UiSound.Back);
                if (_session == null) return true;   // mémoire inaccessible : pas de page de recherche derrière
                _mode = Mode.List;
                break;
            case PadButtons.A:
                if (_codeSel == 0) SearchCodesOnline();
                else ToggleCode(_codes.Codes[_codeSel - 1]);
                break;
        }
        Render();
        return false;
    }

    private void ToggleCode(CheatCode c)
    {
        c.Enabled = !c.Enabled;
        CheatCodes.Save(_codesAll!);
        Sound?.Invoke(UiSound.Select);
        if (_codes!.BuildId == null)
        {
            _codesMessage = "Version du jeu inconnue : quitte puis relance le jeu, OXYN la notera et installera les codes cochés.";
            return;
        }
        var ok = CheatCodes.Apply(_titleId!, _codes);
        _codesMessage = !ok ? "Installation impossible dans l'émulateur (voir le journal)."
            : c.Enabled ? $"« {c.Name} » activé : effet au prochain lancement du jeu (Quitter puis relancer)."
            : $"« {c.Name} » désactivé (au prochain lancement).";
    }

    private async void SearchCodesOnline()
    {
        _codesBusy = true;
        _codesMessage = "Recherche en ligne (switch-cheats-db, CheatSlips)…";
        Sound?.Invoke(UiSound.Select);
        Render();
        try
        {
            var (added, message) = await CheatCodes.SearchOnlineAsync(_codes!, _titleId!, _cheatSlipsToken);
            CheatCodes.Save(_codesAll!);
            _codesMessage = message;
            Sound?.Invoke(added > 0 ? UiSound.Launch : UiSound.Back);
        }
        catch (Exception ex)
        {
            Infrastructure.Log.Warn("Codes de triche en ligne (menu Guide)", ex);
            _codesMessage = "Recherche impossible (pas de connexion ?).";
            Sound?.Invoke(UiSound.Error);
        }
        _codesBusy = false;
        if (_mode == Mode.Codes) Render();
    }

    private void RenderCodes()
    {
        var t = _t;
        if (_codes == null) return;
        Text(_codes.BuildId == null ? "Version du jeu inconnue (notée à la fin de la partie)." : $"Version {_codes.BuildId}", t.SubText, 17);
        var total = 1 + _codes.Codes.Count;
        // Fenêtre de 8 lignes autour de la sélection.
        var first = Math.Clamp(_codeSel - 3, 0, Math.Max(0, total - 8));
        for (var i = first; i < Math.Min(total, first + 8); i++)
        {
            var on = i == _codeSel;
            if (i == 0)
            {
                _body.Children.Add(Row(_codesBusy ? "Recherche…" : "Chercher en ligne", null, on, false, ""));
                continue;
            }
            var c = _codes.Codes[i - 1];
            _body.Children.Add(Row(c.Name, null, on, false, c.Enabled ? "" : ""));
        }
        if (_codesMessage.Length > 0) Text(_codesMessage, t.SubText, 17);
        SetHints(_codeSel == 0 ? "Ⓐ Chercher     Ⓑ Retour" : "Ⓐ Activer / désactiver     Ⓑ Retour");
    }
}
