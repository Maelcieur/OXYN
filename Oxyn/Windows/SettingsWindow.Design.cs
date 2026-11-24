using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Oxyn.Controls;
using Oxyn.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Oxyn;

// Personnaliser › « Disposition et éléments » : 5 dispositions par module, ajout d'éléments (une donnée sous une
// forme : anneau, barre, courbe animée, chiffre…) et réglages complets de l'élément choisi dans l'aperçu.
public partial class SettingsWindow
{
    private DispatcherTimer? _designTimer;
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Applique la disposition modifiée (regroupe les rafales de curseurs) puis remet l'aperçu à jour.</summary>
    private void QueueDesignApply()
    {
        if (_designTimer is null)
        {
            _designTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _designTimer.Tick += (_, _) =>
            {
                _designTimer.Stop();
                CurrentAppearance?.NotifyDesignChanged();
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, RefreshPreviewElements);
            };
        }
        _designTimer.Stop();
        _designTimer.Start();
    }

    /// <summary>Section de gauche : dispositions, ajout d'un élément, taille de la carte (dispositions à éléments).</summary>
    private void BuildDesignEditor()
    {
        DesignHost.Children.Clear();
        if (SelectedModule is not { } module || CurrentAppearance is not { } appearance || module.Id == "searchbar")
        {
            DesignExpander.Visibility = Visibility.Collapsed;
            return;
        }
        DesignExpander.Visibility = Visibility.Visible;
        var design = appearance.Design;
        var current = design?.Layout ?? ModuleDesign.Classic;

        // Dispositions
        var presets = DesignPresets.For(module.Id);
        if (presets.Count > 1)
        {
            var gallery = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var preset in presets)
            {
                var selected = preset.Id == current;
                var text = new StackPanel { Width = 150 };
                text.Children.Add(new TextBlock { Text = preset.Name, FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
                text.Children.Add(new TextBlock
                {
                    Text = preset.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
                    Foreground = (Brush)FindResource("Muted")
                });
                var card = new Border
                {
                    Child = text, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(selected ? 2 : 1), Cursor = System.Windows.Input.Cursors.Hand,
                    BorderBrush = selected ? (Brush)FindResource("UiAccent") : (Brush)FindResource("CardBorder"),
                    Background = (Brush)FindResource("UiHover"), ToolTip = "Appliquer cette disposition"
                };
                card.MouseLeftButtonUp += (_, _) => ApplyDesignPreset(preset);
                gallery.Children.Add(card);
            }
            DesignHost.Children.Add(new SettingRow
            {
                Label = "Disposition",
                Description = "Point de départ : chaque élément se déplace et se règle ensuite dans l'aperçu.",
                Content = gallery, Stacked = true
            });
        }

        // Ajouter un élément : une donnée sous une forme
        var source = SourceCombo(null, module.Id);
        var type = new ComboBox { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var (key, label) in BuilderBlock.TypeLabels) type.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        type.SelectedIndex = 2; // Courbe
        var add = new Button { Content = "Ajouter", Style = (Style)FindResource("PrimaryButton") };
        add.Click += (_, _) => AddDesignBlock((string)((ComboBoxItem)type.SelectedItem).Tag, (source.SelectedItem as ComboBoxItem)?.Tag as string);
        var row = new WrapPanel();
        source.Margin = new Thickness(0, 0, 8, 6);
        row.Children.Add(source);
        row.Children.Add(type);
        row.Children.Add(add);
        DesignHost.Children.Add(new SettingRow
        {
            Label = "Ajouter un élément",
            Description = "Une donnée sous la forme de ton choix (ex. courbe animée de la température). Il apparaît en haut à gauche : glisse-le à sa place.",
            Content = row, Stacked = true
        });

        // Taille de la carte (dispositions à éléments)
        if (design is { IsClassic: false })
        {
            DesignHost.Children.Add(new SettingRow { Label = "Largeur de la carte", Content = DesignSlider(120, 900, design.Width, v => design.Width = v, "0 px") });
            DesignHost.Children.Add(new SettingRow { Label = "Hauteur de la carte", Content = DesignSlider(60, 700, design.Height, v => design.Height = v, "0 px") });
        }
    }

    /// <summary>Liste des données : celles du module en premier, puis toutes les autres.</summary>
    private ComboBox SourceCombo(string? selected, string moduleId)
    {
        var combo = new ComboBox { Width = 300, IsTextSearchEnabled = true };
        var group = BuilderSources.GroupOf(moduleId);
        var readings = _widgetManager.DesignValues().Values
            .OrderBy(r => r.Group == group ? 0 : 1)
            .ThenBy(r => r.Group, StringComparer.CurrentCulture).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        if (selected is not null && readings.All(r => r.Id != selected))
            combo.Items.Add(new ComboBoxItem { Content = $"{selected} (pas encore lue)", Tag = selected });
        foreach (var r in readings)
            combo.Items.Add(new ComboBoxItem { Content = $"{r.Group} · {r.Name} ({r.Unit})", Tag = r.Id });
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected)
            ?? (combo.Items.Count > 0 ? combo.Items[0] : null);
        return combo;
    }

    private FrameworkElement DesignSlider(double min, double max, double value, Action<double> set, string format, double tick = 1)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), IsSnapToTickEnabled = true, TickFrequency = tick };
        var text = new TextBlock { Style = (Style)FindResource("FieldValue"), Text = slider.Value.ToString(format, Fr) };
        slider.ValueChanged += (_, e) =>
        {
            text.Text = e.NewValue.ToString(format, Fr);
            set(e.NewValue);
            QueueDesignApply();
        };
        var dock = new DockPanel { Width = 260 };
        DockPanel.SetDock(text, Dock.Right);
        dock.Children.Add(text);
        dock.Children.Add(slider);
        return dock;
    }

    private void ApplyDesignPreset(DesignPreset preset)
    {
        if (CurrentAppearance is not { } appearance) return;
        if (appearance.Design is { Blocks.Count: > 0 } && System.Windows.MessageBox.Show(this,
                $"Passer à la disposition « {preset.Name} » ? Les éléments actuels seront remplacés.",
                "OXYN", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        // Les anciens placements des éléments n'ont plus de sens.
        foreach (var key in appearance.Elements.Keys.Where(k => k.StartsWith("block:")).ToList())
            appearance.Elements.Remove(key);
        var fresh = preset.Create();
        appearance.Design = fresh.IsEmpty ? null : fresh;
        appearance.NotifyElementsChanged();
        _selectedPreviewPart = null;
        BuildDesignEditor();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, RefreshPreviewElements);
    }

    private void AddDesignBlock(string type, string? source)
    {
        if (CurrentAppearance is not { } appearance) return;
        var design = appearance.Design ?? new ModuleDesign();
        var (w, h) = BuilderBlock.DefaultSize(type);
        var block = new BuilderBlock
        {
            Type = type, Width = w, Height = h, X = 8, Y = 8,
            Source = type is "Header" ? null : source,
            Label = type == "Header" ? "Titre" : null,
            Text = type == "Text" ? "{valeur}" : null,
            FontSize = type == "Big" ? 16 : 13
        };
        if (!design.IsClassic)
        {
            block.Y = design.Blocks.Count == 0 ? 8 : design.Blocks.Max(b => b.Y + b.Height) + 8;
            if (block.Y + h > design.Height) design.Height = Math.Min(700, block.Y + h + 10);
        }
        design.Blocks.Add(block);
        appearance.Design = design;
        appearance.NotifyDesignChanged();
        _selectedPreviewPart = new ModulePreviewPart($"block:{block.Id}", BlockTitle(block), "elements", Rect.Empty);
        BuildDesignEditor();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            RefreshPreviewElements();
            ShowBlockInspector(block);
        });
    }

    private string BlockTitle(BuilderBlock block)
    {
        var form = BuilderBlock.TypeLabels.GetValueOrDefault(block.Type, block.Type);
        if (block.Type == "Header") return $"{form} · {block.Label}";
        var reading = block.Source is { } id && _widgetManager.DesignValues().TryGetValue(id, out var r) ? r : null;
        var data = !string.IsNullOrWhiteSpace(block.Label) ? block.Label : reading?.Name ?? block.Source ?? "";
        return string.IsNullOrWhiteSpace(data) ? form : $"{form} · {data}";
    }

    private BuilderBlock? BlockOf(string key)
        => key.StartsWith("block:") ? CurrentAppearance?.Design?.Blocks.FirstOrDefault(b => b.Id == key[6..]) : null;

    // ---------- Réglages de l'élément choisi ----------

    /// <summary>
    /// Élément d'origine choisi : ses textes et ses boutons (ceux dont le centre est dans son cadre) sont réglables
    /// au même endroit que lui (texte, police, taille, couleur ; icône, couleurs du bouton).
    /// </summary>
    private void ShowInnerParts(ModulePreviewPart part)
    {
        if (_placeCard is null || part.Bounds.IsEmpty) return;
        bool Inside(Rect r) => part.Bounds.Contains(new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
        var inner = _placeCard.GetPreviewParts().Where(p => p.Section is "texts" or "buttons" && Inside(p.Bounds)).ToList();
        var texts = inner.Where(p => p.Section == "texts").Select(p => p.Key).ToHashSet();
        var buttons = inner.Where(p => p.Section == "buttons").Select(p => p.Key).ToHashSet();
        TextPartsList.ItemsSource = _textRows.Where(r => texts.Contains(r.Key)).ToList();
        ButtonPartsList.ItemsSource = _buttonRows.Where(r => buttons.Contains(r.Info.Key)).ToList();
        SelectedTextListHost.Visibility = texts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectedButtonListHost.Visibility = buttons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowGaugeSettings(part);
    }

    private static readonly string[] GaugeKeys =
        { "GaugeShape", "GaugeStyle", "GaugeThickness", "ShowTicks", "TickStyle", "Glow" };

    private static bool Contains<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T) return true;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Contains<T>(VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }

    /// <summary>
    /// Élément d'origine qui contient une jauge (anneau ou barre) : forme, style, épaisseur, graduations et halo,
    /// propres à ce module (les autres modules gardent le style global).
    /// </summary>
    private void ShowGaugeSettings(ModulePreviewPart part)
    {
        SelectedInnerHost.Children.Clear();
        SelectedInnerHost.Visibility = Visibility.Collapsed;
        if (ElementOf(part.Key) is not { } element || CurrentAppearance is not { } appearance || SelectedModule is not { } module) return;
        if (!Contains<RadialGauge>(element) && !Contains<LinearGauge>(element)) return;
        // L'ancienne section « Jauges › Propre à ce module » ferait doublon avec celle-ci.
        if (SelectedElementAdvancedHost.Children.Contains(StyleGaugesSection)) StyleGaugesSection.Visibility = Visibility.Collapsed;

        if (appearance.Theme is null) appearance.Theme = _widgetManager.Theme.Clone();
        if (!appearance.CustomGauges) appearance.Theme.CopyGaugesFrom(_widgetManager.Theme); // part du style global
        var theme = appearance.Theme;

        SelectedInnerHost.Children.Add(new TextBlock
        {
            Text = "Jauges", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 16, 0, 2)
        });
        var note = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var back = new Button { Content = "Revenir au style global", Style = (Style)FindResource("LinkButton"), Margin = new Thickness(12, 0, 0, 0) };
        back.Click += (_, _) =>
        {
            appearance.CustomGauges = false;
            theme.CopyGaugesFrom(_widgetManager.Theme);
        };
        DockPanel.SetDock(back, Dock.Right);
        note.Children.Add(back);
        note.Children.Add(new TextBlock
        {
            Text = "Forme, style, épaisseur, graduations et halo des jauges de ce module. Les couleurs de cet élément sont plus haut.",
            FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted")
        });
        SelectedInnerHost.Children.Add(note);

        // Forme de CETTE jauge (les autres gardent la leur).
        if (Contains<RadialGauge>(element))
        {
            var layout = appearance.GetElement(part.Key);
            var shapes = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
            foreach (var (value, text) in new (double?, string)[] { (null, "Comme à l'origine"), (270.0, "Arc 270°"), (360.0, "Cercle complet"), (180.0, "Demi-cercle") })
            {
                var chip = new RadioButton { Content = text, GroupName = "ElementSweep", IsChecked = layout.GaugeSweep == value };
                chip.SetResourceReference(StyleProperty, "Chip");
                chip.Checked += (_, _) =>
                {
                    layout.GaugeSweep = value;
                    appearance.NotifyElementsChanged();
                    Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, RefreshPreviewElements);
                };
                shapes.Children.Add(chip);
            }
            SelectedInnerHost.Children.Add(new SettingRow { Label = "Forme de cette jauge", Content = shapes, Stacked = true });
        }

        var editor = new ThemeEditor
        {
            Section = "Gauges", Embedded = true, ModuleId = module.Id, DiskStyle = appearance.DiskStyle,
            OnSwitch = _customizeSwitch, DataContext = theme
        };
        // Premier réglage changé : les jauges de ce module ne suivent plus le style global.
        System.ComponentModel.PropertyChangedEventHandler edited = (_, e) =>
        {
            if (GaugeKeys.Contains(e.PropertyName) && !appearance.CustomGauges) appearance.CustomGauges = true;
        };
        theme.PropertyChanged += edited;
        editor.Unloaded += (_, _) => theme.PropertyChanged -= edited;
        SelectedInnerHost.Children.Add(editor);
        SelectedInnerHost.Visibility = Visibility.Visible;
    }

    private void AddBlockRow(string label, string? description, FrameworkElement control, bool stacked = false)
        => SelectedBlockHost.Children.Add(new SettingRow { Label = label, Description = description, Content = control, Stacked = stacked });

    private void ShowBlockInspector(BuilderBlock block)
    {
        SelectedBlockHost.Children.Clear();
        SelectedBlockHost.Visibility = Visibility.Visible;
        SelectedPartTitle.Text = BlockTitle(block);
        SelectedPartEmptyText.Visibility = Visibility.Collapsed;
        void Changed() => QueueDesignApply();

        var type = new ComboBox { Width = 200 };
        foreach (var (key, typeLabel) in BuilderBlock.TypeLabels) type.Items.Add(new ComboBoxItem { Content = typeLabel, Tag = key });
        type.SelectedItem = type.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == block.Type);
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedItem is not ComboBoxItem { Tag: string t } || t == block.Type) return;
            block.Type = t;
            (block.Width, block.Height) = BuilderBlock.DefaultSize(t);
            Changed();
            ShowBlockInspector(block);
        };
        AddBlockRow("Forme", "Anneau, barre, courbe animée, ligne de valeur, grand chiffre, titre ou texte.", type);

        if (block.Type != "Header")
            AddBlockRow("Données", "Une ou plusieurs (ex. les deux barrettes de mémoire) : elles sont alors combinées.", SourcesEditor(block), stacked: true);

        var label = new TextBox { Text = block.Label ?? "", Width = 240 };
        label.TextChanged += (_, _) => { block.Label = string.IsNullOrWhiteSpace(label.Text) ? null : label.Text; Changed(); };
        AddBlockRow(block.Type == "Header" ? "Titre" : "Libellé", block.Type == "Header" ? null : "Vide : nom de la donnée.", label);
        if (block.Type is "Ring" or "Bar" or "Graph" or "Big")
            AddBlockRow("Afficher le libellé", null, DesignToggle(block.ShowLabel, v => { block.ShowLabel = v; Changed(); }));
        if (block.Type is "Value" or "Ring" or "Bar" or "Big" or "Graph")
            AddBlockRow("Libellé en majuscules", null, DesignToggle(block.Uppercase, v => { block.Uppercase = v; Changed(); }));

        if (block.Type is "Header" or "Text")
        {
            var text = new TextBox { Text = block.Text ?? "", Width = 240 };
            text.TextChanged += (_, _) => { block.Text = text.Text; Changed(); };
            AddBlockRow(block.Type == "Header" ? "Sous-titre" : "Texte",
                block.Type == "Text" ? "« {valeur} » est remplacé par la donnée." : "À droite du titre (ex. nom du matériel).", text);
        }

        if (block.Type == "Ring")
        {
            AddBlockRow("Forme de la jauge", null, Chips("BlockSweep",
                new (object?, string)[] { (270.0, "Arc 270°"), (360.0, "Cercle complet"), (180.0, "Demi-cercle") },
                block.Sweep, v => { block.Sweep = (double)v!; Changed(); }), stacked: true);
            AddBlockRow("Style de la jauge", "« Comme le style » : celui choisi dans la page Style.", Chips("BlockDesign",
                new (object?, string)[]
                {
                    (null, "Comme le style"), ("Smooth", "Lisse"), ("Segments", "Segments"), ("Thin", "Fine"), ("Dots", "Pointillés"),
                    ("Needle", "Compteur"), ("Pie", "Secteur"), ("Neon", "Néon"), ("Rays", "Rayons"), ("Double", "Double"), ("Capsule", "Capsule")
                },
                block.Design, v => { block.Design = (string?)v; block.Segmented = (string?)v == "Segments"; Changed(); }), stacked: true);
            AddBlockRow("Épaisseur de l'anneau", null, DesignSlider(0.3, 3, block.Thickness, v => block.Thickness = v, "×0.0", 0.1));
        }
        if (block.Type == "Bar")
            AddBlockRow("En segments", null, DesignToggle(block.Segmented, v => { block.Segmented = v; Changed(); }));
        if (block.Type == "Graph")
        {
            AddBlockRow("Surface sous la courbe", null, DesignToggle(block.Fill, v => { block.Fill = v; Changed(); }));
            AddBlockRow("Durée affichée", null, DesignSlider(10, 600, block.HistorySeconds, v => block.HistorySeconds = (int)v, "0 s", 10));
        }
        if (block.Type is "Ring" or "Bar" or "Graph")
        {
            var max = new TextBox { Text = block.Max > 0 ? block.Max.ToString(CultureInfo.CurrentCulture) : "", Width = 100 };
            max.TextChanged += (_, _) =>
            {
                block.Max = double.TryParse(max.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var m) && m > 0 ? m : 0;
                Changed();
            };
            AddBlockRow("Maximum de l'échelle", "Vide : selon l'unité (100 %, 100 °C, 6 000 MHz…) ; courbe : automatique.", max);
        }

        AddBlockRow("Taille du texte", null, DesignSlider(8, 40, block.FontSize, v => block.FontSize = v, "0"));
        AddBlockRow("Largeur", null, DesignSlider(10, 900, block.Width, v => block.Width = v, "0 px"));
        AddBlockRow("Hauteur", null, DesignSlider(10, 700, block.Height, v => block.Height = v, "0 px"));

        var swatch = new ColorSwatch { Hex = block.Color ?? "#FF5B8DEF", Content = block.Color is null ? "Couleur du style" : "Couleur propre" };
        DependencyPropertyDescriptor.FromProperty(ColorSwatch.HexProperty, typeof(ColorSwatch)).AddValueChanged(swatch, (_, _) =>
        {
            block.Color = swatch.Hex;
            swatch.Content = "Couleur propre";
            Changed();
        });
        var resetColor = new Button { Content = "Couleur du style", Style = (Style)FindResource("LinkButton"), Margin = new Thickness(12, 0, 0, 0) };
        resetColor.Click += (_, _) => { block.Color = null; Changed(); ShowBlockInspector(block); };
        var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
        colorRow.Children.Add(swatch);
        colorRow.Children.Add(resetColor);
        AddBlockRow("Couleur", "Par défaut : le dégradé du style global.", colorRow);

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var duplicate = new Button { Content = "Dupliquer", Margin = new Thickness(0, 0, 8, 8) };
        duplicate.Click += (_, _) =>
        {
            if (CurrentAppearance?.Design is not { } design) return;
            var copy = block.Copy();
            copy.Id = Guid.NewGuid().ToString("N")[..8];
            copy.X += 12; copy.Y += 12;
            design.Blocks.Add(copy);
            CurrentAppearance.NotifyDesignChanged();
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => { RefreshPreviewElements(); ShowBlockInspector(copy); });
        };
        var remove = new Button { Content = "Supprimer l'élément", Margin = new Thickness(0, 0, 8, 8) };
        remove.Click += (_, _) =>
        {
            if (CurrentAppearance is not { Design: { } design } appearance) return;
            design.Blocks.Remove(block);
            appearance.Elements.Remove($"block:{block.Id}");
            if (design.IsEmpty) appearance.Design = null;
            appearance.NotifyDesignChanged();
            appearance.NotifyElementsChanged();
            _selectedPreviewPart = null;
            SelectedBlockHost.Children.Clear();
            SelectedBlockHost.Visibility = Visibility.Collapsed;
            SelectedPartTitle.Text = "Sélectionnez un élément dans l'aperçu";
            SelectedPartEmptyText.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, RefreshPreviewElements);
        };
        actions.Children.Add(duplicate);
        actions.Children.Add(remove);
        SelectedBlockHost.Children.Add(actions);
    }

    /// <summary>
    /// Données de l'élément : la première, puis d'autres (« + Ajouter une autre donnée ») ; dès qu'il y en a deux,
    /// le calcul à faire : moyenne, maximum, minimum, somme (ex. 2 × 16 Go = 32 Go) ou chacune affichée.
    /// </summary>
    private FrameworkElement SourcesEditor(BuilderBlock block)
    {
        var panel = new StackPanel();
        var moduleId = SelectedModule?.Id ?? "";
        void Changed() => QueueDesignApply();

        var first = SourceCombo(block.Source, moduleId);
        first.SelectionChanged += (_, _) =>
        {
            if (first.SelectedItem is ComboBoxItem { Tag: string id }) { block.Source = id; Changed(); }
        };
        panel.Children.Add(first);

        block.MoreSources ??= new List<string>();
        for (var i = 0; i < block.MoreSources.Count; i++)
        {
            var index = i;
            var combo = SourceCombo(block.MoreSources[i], moduleId);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: string id }) { block.MoreSources[index] = id; Changed(); }
            };
            var remove = new Button { Content = "Retirer", Style = (Style)FindResource("LinkButton"), Margin = new Thickness(8, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                block.MoreSources.RemoveAt(index);
                Changed();
                ShowBlockInspector(block);
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(combo);
            row.Children.Add(remove);
            panel.Children.Add(row);
        }

        var add = new Button { Content = "+ Ajouter une autre donnée", Style = (Style)FindResource("LinkButton"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        add.Click += (_, _) =>
        {
            block.MoreSources.Add(block.Source ?? "");
            Changed();
            ShowBlockInspector(block);
        };
        panel.Children.Add(add);

        if (block.MoreSources.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Calcul", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
            panel.Children.Add(Chips("Combine", BuilderBlock.CombineLabels.Select(c => ((object?)c.Key, c.Label)).ToArray(),
                block.Combine, v => { block.Combine = (string)v!; Changed(); }));
            panel.Children.Add(new TextBlock
            {
                Text = "« Chacune » affiche les valeurs côte à côte (lignes, chiffres) ; les jauges et courbes prennent la moyenne.",
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Foreground = (Brush)FindResource("Muted")
            });
        }
        return panel;
    }

    /// <summary>Rangée de puces (une seule choisie), comme dans les réglages du style.</summary>
    private static WrapPanel Chips(string group, (object? Value, string Text)[] items, object? current, Action<object?> set)
    {
        var panel = new WrapPanel();
        var name = group + Guid.NewGuid().ToString("N")[..6]; // groupe propre à ce panneau
        foreach (var (value, text) in items)
        {
            var chip = new RadioButton { Content = text, GroupName = name, IsChecked = Equals(value, current) };
            chip.SetResourceReference(StyleProperty, "Chip");
            chip.Checked += (_, _) => set(value);
            panel.Children.Add(chip);
        }
        return panel;
    }

    private static CheckBox DesignToggle(bool value, Action<bool> set)
    {
        var check = new CheckBox { IsChecked = value, Margin = new Thickness(0) };
        check.SetResourceReference(StyleProperty, "ToggleSwitch");
        check.Checked += (_, _) => set(true);
        check.Unchecked += (_, _) => set(false);
        return check;
    }
}
