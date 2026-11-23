using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Oxyn;

// Modules regroupés : une seule ligne dans la page Modules pour plusieurs modules proches (horloges, météo et ciel,
// son). Chaque partie reste un module à part entière en dessous (apparence propre, textes, Switch, profils) : seul
// l'affichage dans les Paramètres est regroupé.
// - Horloge : une seule horloge à la fois, le style se choisit (à cases, ronde, numérique) et garde la même place.
// - Météo et ciel / Son : on choisit les parties affichées ; masquer le module retient lesquelles l'étaient.
public partial class SettingsWindow
{
    private sealed record ModuleFamily(string Id, string Label, string Glyph, string[] Members, string[] PartLabels, string[] DefaultOn, bool Exclusive);

    private static readonly ModuleFamily[] Families =
    {
        new("clock", "Horloge", "", new[] { "tileclock", "analogclock", "digitalclock" },
            new[] { "À cases", "Ronde", "Numérique" }, new[] { "tileclock" }, Exclusive: true),
        new("sky", "Météo et ciel", "", new[] { "weather", "sun", "airquality", "moon" },
            new[] { "Prévisions", "Lever et coucher du soleil", "Qualité de l'air", "Phases de la Lune" }, new[] { "weather" }, Exclusive: false),
        new("sound", "Son", "", new[] { "audio", "volumemixer" },
            new[] { "Sortie audio", "Volume par application" }, new[] { "audio", "volumemixer" }, Exclusive: false),
    };

    private static ModuleFamily? FamilyOf(string id) => Families.FirstOrDefault(f => f.Id == id || f.Members.Contains(id));

    private static bool IsFamilyMember(string id) => Families.Any(f => f.Members.Contains(id));

    private bool FamilyShown(ModuleFamily family) => family.Members.Any(_widgetManager.IsWidgetShown);

    /// <summary>Partie à personnaliser quand on ouvre le module regroupé : une partie affichée, sinon la dernière retenue.</summary>
    private string FamilyTarget(ModuleFamily family)
        => family.Members.FirstOrDefault(_widgetManager.IsWidgetShown)
           ?? _widgetManager.Config.FamilyParts.GetValueOrDefault(family.Id)?.FirstOrDefault(family.Members.Contains)
           ?? family.DefaultOn[0];

    /// <summary>Interrupteur du module regroupé : affiche les parties retenues, ou masque tout en les retenant.</summary>
    private void ToggleFamily(ModuleFamily family, bool on)
    {
        var parts = _widgetManager.Config.FamilyParts;
        if (on)
        {
            var wanted = parts.GetValueOrDefault(family.Id)?.Where(family.Members.Contains).ToList();
            foreach (var id in wanted is { Count: > 0 } ? wanted : family.DefaultOn.ToList())
                _widgetManager.ToggleWidget(id, true);
        }
        else
        {
            var shown = family.Members.Where(_widgetManager.IsWidgetShown).ToList();
            if (shown.Count > 0) parts[family.Id] = shown;
            foreach (var id in shown) _widgetManager.ToggleWidget(id, false);
            ScheduleSave();
        }
    }

    // ---------- Panneau du module regroupé dans Personnaliser ----------

    private bool _buildingFamily;

    /// <summary>En haut de Personnaliser : style d'horloge, ou parties affichées (météo et ciel, son).</summary>
    private void BuildFamilyPanel()
    {
        var id = SelectedModule?.Id;
        var family = id is null ? null : FamilyOf(id);
        FamilyExpander.Visibility = family is null ? Visibility.Collapsed : Visibility.Visible;
        FamilyPanel.Children.Clear();
        if (family is null || id is null) return;

        _buildingFamily = true;
        try
        {
            if (family.Exclusive)
            {
                FamilyExpander.Header = "Style d'horloge";
                var combo = new System.Windows.Controls.ComboBox { Width = 200, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 10) };
                foreach (var label in family.PartLabels) combo.Items.Add(label);
                combo.SelectedIndex = Array.IndexOf(family.Members, id);
                combo.SelectionChanged += (_, _) =>
                {
                    if (_buildingFamily || combo.SelectedIndex < 0) return;
                    var next = family.Members[combo.SelectedIndex];
                    var current = family.Members.FirstOrDefault(_widgetManager.IsWidgetShown);
                    if (current is not null && current != next) _widgetManager.ReplaceWidget(current, next);
                    _widgetManager.Config.FamilyParts[family.Id] = new List<string> { next };
                    ScheduleSave();
                    RefreshWidgetRows();
                    ShowModule(next);
                };
                FamilyPanel.Children.Add(Hint("Une seule horloge à la fois : le nouveau style prend la place de l'ancien."));
                FamilyPanel.Children.Add(combo);
                return;
            }

            FamilyExpander.Header = family.Id == "sky" ? "Parties affichées (météo et ciel)" : "Parties affichées (son)";
            FamilyPanel.Children.Add(Hint("Chaque partie est une carte sur le bureau, avec sa propre apparence : « Personnaliser » la sélectionne ci-dessous."));
            for (var i = 0; i < family.Members.Length; i++)
            {
                var member = family.Members[i];
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var check = new CheckBox
                {
                    Style = (Style)FindResource("ToggleSwitch"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    IsChecked = _widgetManager.IsWidgetShown(member),
                };
                check.Checked += (_, _) => SetFamilyPart(member, true);
                check.Unchecked += (_, _) => SetFamilyPart(member, false);
                DockPanel.SetDock(check, Dock.Right);
                row.Children.Add(check);
                var edit = new Button
                {
                    Style = (Style)FindResource("LinkButton"), Content = member == id ? "En cours" : "Personnaliser",
                    IsEnabled = member != id, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                };
                edit.Click += (_, _) => ShowModule(member);
                DockPanel.SetDock(edit, Dock.Right);
                row.Children.Add(edit);
                row.Children.Add(new TextBlock
                {
                    Text = family.PartLabels[i], VerticalAlignment = VerticalAlignment.Center, FontSize = 13,
                    Foreground = (Brush)FindResource("UiText"), FontWeight = member == id ? FontWeights.SemiBold : FontWeights.Normal,
                });
                FamilyPanel.Children.Add(row);
            }
        }
        finally { _buildingFamily = false; }
    }

    private void SetFamilyPart(string member, bool on)
    {
        if (_buildingFamily) return;
        _widgetManager.ToggleWidget(member, on);
        RefreshWidgetRows();
        SyncShownChecks();
    }

    private TextBlock Hint(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 0, 0, 6),
        Foreground = (Brush)FindResource("Muted"),
    };
}
