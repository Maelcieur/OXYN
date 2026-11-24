using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Oxyn.Services;

namespace Oxyn;

public partial class SettingsWindow : Window
{
    private readonly WidgetManager _widgetManager;

    // Les réglages s'appliquent en direct ; l'écriture sur disque est regroupée (curseurs = rafales de changements).
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private readonly List<(ModuleAppearance Appearance, PropertyChangedEventHandler Handler)> _moduleSubscriptions = new();
    private bool _confirmReset;
    private bool _confirmPowerReset;

    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["cpu"] = "",
        ["gpu"] = "",
        ["ram"] = "",
        ["network"] = "",
        ["disk"] = "",
        ["media"] = "",
        ["volumemixer"] = "",
        ["mouse"] = "",
        ["audio"] = "",
        ["shortcuts"] = "",
        ["power"] = "",
        ["claude"] = "",
        ["visualizer"] = "\uE9D9",
        ["tileclock"] = "\uE916",
        ["analogclock"] = "\uE823",
        ["digitalclock"] = "\uE917",
        ["calendar"] = "\uE787",
        ["weather"] = "\uE9CA",
        ["temperatures"] = "\uE9D2",
        ["airquality"] = "\uE9CA",
        ["sensorbars"] = "\uE9D9",
        ["moon"] = "\uE708",
        ["sun"] = "\uE706",
        ["searchbar"] = "\uE721",
    };

    private sealed record WidgetRow(string Id, string Label, string Glyph, bool IsVisible, bool CanCustomize, string Category);

    /// <summary>Catégories de la page Modules, dans l'ordre d'affichage.</summary>
    private static readonly (string Name, string[] Ids)[] ModuleCategories =
    {
        ("Matériel", new[] { "cpu", "gpu", "ram", "disk", "network", "temperatures", "sensorbars", "power" }),
        ("Audio et médias", new[] { "sound", "media", "visualizer" }),
        ("Heure et météo", new[] { "clock", "calendar", "sky" }),
        ("Outils", new[] { "searchbar", "shortcuts", "mouse", "claude" }),
    };

    /// <summary>Liste de la page Modules, groupée par catégorie (les modules inconnus vont dans « Autres »).</summary>
    private System.ComponentModel.ICollectionView BuildWidgetRows()
    {
        int Order(string id)
        {
            for (var c = 0; c < ModuleCategories.Length; c++)
            {
                var i = Array.IndexOf(ModuleCategories[c].Ids, id);
                if (i >= 0) return c * 100 + i;
            }
            return 10_000;
        }
        string Category(string id) => ModuleCategories.FirstOrDefault(c => c.Ids.Contains(id)).Name ?? "Autres";
        // Les parties d'un module regroupé (horloges, météo et ciel, son) n'ont qu'une ligne : celle du regroupement.
        var rows = WidgetManager.AllWidgets
            .Where(w => !IsFamilyMember(w.Id))
            .Select(w => new WidgetRow(w.Id, w.Label, Glyphs.GetValueOrDefault(w.Id, ""), _widgetManager.IsWidgetShown(w.Id), true, Category(w.Id)))
            .Concat(Families.Select(f => new WidgetRow(f.Id, f.Label, f.Glyph, FamilyShown(f), true, Category(f.Id))))
            .OrderBy(r => Order(r.Id))
            .ToList();
        var view = new System.Windows.Data.ListCollectionView(rows);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(WidgetRow.Category)));
        return view;
    }

    private void RefreshWidgetRows() => WidgetsList.ItemsSource = BuildWidgetRows();

    // ToString = libellé : affiché tel quel par la liste des modules, même si la liaison sur Label échoue.
    public sealed record ModuleRow(string Id, string Label, ModuleAppearance Appearance)
    {
        public override string ToString() => Label;
    }

    public SettingsWindow(WidgetManager widgetManager)
    {
        InitializeComponent();
        _widgetManager = widgetManager;
        InitializeCustomizeGroups();
        ShowVersion();
        InitUpdates();
        InitCollapsibleSections();
        var config = widgetManager.Config;

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _widgetManager.SaveConfig();
        };

        // Page Modules
        RefreshWidgetRows();

        // Page Thème global : un éditeur par sous-onglet, tous sur le même thème
        GlobalThemeEditor.DataContext = GlobalCardEditor.DataContext = GlobalGaugesEditor.DataContext
            = GlobalAnimationsEditor.DataContext = widgetManager.Theme;
        widgetManager.Theme.PropertyChanged += OnGlobalThemeChanged;

        // Page Personnaliser (la barre de raccourcis a sa propre page)
        var modules = WidgetManager.AllWidgets
            .Where(w => w.Id != "shortcuts")
            .Select(w => new ModuleRow(w.Id, w.Label, widgetManager.GetAppearance(w.Id)))
            .ToList();
        // Chaque module a toujours ses réglages de style prêts (copie du thème global) : les onglets
        // de style affichent alors des valeurs réelles dès qu'un groupe est activé, sans case vide.
        foreach (var module in modules.Where(m => m.Appearance.Theme is null))
            module.Appearance.Theme = widgetManager.Theme.Clone();
        foreach (var module in modules)
        {
            var id = module.Id;
            PropertyChangedEventHandler handler = (_, e) => OnModuleChanged(id, module.Appearance, e.PropertyName);
            module.Appearance.PropertyChanged += handler;
            _moduleSubscriptions.Add((module.Appearance, handler));
        }
        ModuleSelector.ItemsSource = modules;
        EnergyPanel.DataContext = config.Power;
        config.Power.PropertyChanged += OnPowerSettingsChanged;
        InitExtraWidgets();
        ModuleSelector.SelectedIndex = 0;

        // Page Barre de raccourcis
        DockPanelOpacitySlider.Value = config.DockPanelOpacity;
        DockBorderOpacitySlider.Value = config.DockBorderOpacity;
        (config.DockOrientation == "Vertical" ? OrientationVertical : OrientationHorizontal).IsChecked = true;
        var directionRadio = config.DockAnimationDirection switch
        {
            "Down" => DirDown,
            "Left" => DirLeft,
            "Right" => DirRight,
            _ => DirUp
        };
        directionRadio.IsChecked = true;
        IconSizeSlider.Value = config.DockIconSize;
        IconSpacingSlider.Value = config.DockIconSpacing;

        InitDeckPage();
        InitProfilesPage();
        InitIntegrationsPage();
        InitPluginsPage();
        InitConsolePage();

        // Divers
        AlertsPanel.DataContext = config.Alerts;
        config.Alerts.PropertyChanged += OnAlertSettingsChanged;
        LockWidgetsCheck.IsChecked = config.WidgetsLocked;
        SetNavCollapsed(config.SettingsNavCollapsed, save: false);
        LayoutEditCheck.IsChecked = widgetManager.LayoutEditMode;
        widgetManager.LayoutEditModeChanged += OnLayoutEditModeChanged;
        widgetManager.DesktopStateChanged += OnDesktopStateChanged;

        Closed += (_, _) =>
        {
            FlushApply(); // dernier changement encore en attente d'application
            _widgetManager.LayoutEditModeChanged -= OnLayoutEditModeChanged;
            _widgetManager.DesktopStateChanged -= OnDesktopStateChanged;
            _widgetManager.Deck.StatusChanged -= ShowDeckStatus;
            _widgetManager.Theme.PropertyChanged -= OnGlobalThemeChanged;
            _widgetManager.Config.Power.PropertyChanged -= OnPowerSettingsChanged;
            _widgetManager.Config.Alerts.PropertyChanged -= OnAlertSettingsChanged;
            _widgetManager.SuspendHotkeys(false); // fermeture en pleine saisie : les raccourcis reviennent
            _integrationsTimer.Stop();
            CloseConsolePage();
            _twitchLoginCts?.Cancel();
            _widgetManager.Obs.StateChanged -= UpdateObsStatus;
            _widgetManager.Twitch.StateChanged -= UpdateTwitchStatus;
            _widgetManager.Voicemod.StateChanged -= UpdateVoicemodStatus;
            foreach (var (appearance, handler) in _moduleSubscriptions)
                appearance.PropertyChanged -= handler;
            foreach (var (style, handler) in _textSubscriptions)
                style.PropertyChanged -= handler;
            foreach (var (style, handler) in _buttonSubscriptions)
                style.PropertyChanged -= handler;
            _buttonSubscriptions.Clear();
            if (_saveTimer.IsEnabled)
            {
                _saveTimer.Stop();
                _widgetManager.SaveConfig();
            }
        };
    }

    /// <summary>Version affichée sous le nom (celle du projet, reprise par l'installateur).</summary>
    private void ShowVersion()
    {
        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = version is null ? "" : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---------- Navigation ----------

    /// <summary>Vrai : l'entrée « Modules » du menu affiche la page Personnaliser d'un module au lieu de la liste.</summary>
    private bool _customizing;
    /// <summary>Vrai (avec _customizing) : le module personnalisé est la barre de raccourcis, qui a sa propre page.</summary>
    private bool _customizingDock;

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton nav) return;
        if (nav != NavModules) _customizing = _customizingDock = false; // quitter Modules ferme la personnalisation
        ShowPageFor(nav);
    }

    /// <summary>Cliquer « Modules » alors qu'on personnalise un module revient à la liste.</summary>
    private void NavModules_Click(object sender, RoutedEventArgs e)
    {
        if (!_customizing) return;
        _customizing = _customizingDock = false;
        ShowPageFor(NavModules);
    }

    /// <summary>Fil d'Ariane de Personnaliser (et de la barre de raccourcis) : retour à la liste des modules.</summary>
    private void CustomizeBack_Click(object sender, RoutedEventArgs e)
    {
        _customizing = _customizingDock = false;
        ShowPageFor(NavModules);
    }

    /// <summary>Va à une page du menu ; <paramref name="customize"/> ouvre Personnaliser (sous l'entrée « Modules »).</summary>
    private void NavigateTo(RadioButton nav, bool customize = false)
    {
        if (customize)
        {
            _customizing = true;
            nav = NavModules;
        }
        else
        {
            _customizing = _customizingDock = false;
        }

        if (nav.IsChecked == true) ShowPageFor(nav);
        else nav.IsChecked = true;
    }

    private void ShowPageFor(RadioButton nav)
    {
        if (!IsInitialized || PageModules is null) return;

        var pages = new Dictionary<RadioButton, FrameworkElement>
        {
            [NavModules] = !_customizing ? PageModules : _customizingDock ? PageDock : PageCustomize,
            [NavProfiles] = PageProfiles,
            [NavAlerts] = PageAlerts,
            [NavSwitch] = PageSwitch,
            [NavConsole] = PageConsole,
            [NavIntegrations] = PageIntegrations,
            [NavGeneral] = PageGeneral,
        };
        var target = pages[nav];
        foreach (var page in pages.Values.Append(PageCustomize).Append(PageModules).Append(PageDock).Distinct())
            page.Visibility = page == target ? Visibility.Visible : Visibility.Collapsed;

        if (target == PageProfiles) RefreshProfilesList(); // un profil a pu être ajouté ou retiré depuis la dernière visite
        if (target == PageGeneral) UpdateFpsStatus();
        if (target == PageIntegrations && ConnectionsTabPlugins.IsChecked == true) RefreshPluginsList();
        if (target == PageDock) SyncShownChecks();
        if (target == PageConsole) RefreshConsolePage();
        if (target == PageCustomize)
        {
            ReturnModuleEditor(); // éditeur prêté à l'inspecteur de la page Switch
            RefreshPreviewElements();
        }
        SetIntegrationsTimer(target == PageIntegrations && ConnectionsTabApps.IsChecked == true);
        if (target != PageSwitch) ClosePluginPage(); // la page de réglages d'un plugin ne reste pas ouverte en arrière-plan
        else if (_inspected is not null) ShowPluginPage(_inspected);
        if (target is ScrollViewer scroller) scroller.ScrollToTop();
        else CustomizeScroller.ScrollToTop(); // Personnaliser : conserve l'en-tête et replace le panneau de réglages en haut
    }

    /// <summary>Les anciennes catégories servent de repères de navigation dans le panneau unique.</summary>
    private void CustomizeTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || TabSize is null) return;

        var sections = new Dictionary<RadioButton, FrameworkElement>
        {
            [TabSizeButton] = TabSize,
            [TabElementsButton] = TabElements,
            [TabTextsButton] = TabTexts,
            [TabButtonsButton] = TabButtons,
            [TabStyleButton] = TabStyle,
        };
        if (sender is RadioButton selected && sections.TryGetValue(selected, out var panel))
            panel.BringIntoView();

        // La liste des éléments dépend du module affiché sur le bureau : rafraîchie à chaque accès.
        if (sender == TabElementsButton)
        {
            BuildElementRows();
            BuildAudioDeviceRows(); // un périphérique a pu être branché ou une application vue depuis la dernière visite
            BuildAppIconRows();
            RefreshPreviewElements();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- Modules ----------

    private void WidgetCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _syncingShown || sender is not CheckBox { Tag: string id } checkBox)
            return;

        if (Families.FirstOrDefault(f => f.Id == id) is { } family) ToggleFamily(family, checkBox.IsChecked == true);
        else _widgetManager.ToggleWidget(id, checkBox.IsChecked == true);
        if (sender == DockShownCheck) RefreshWidgetRows();
        else SyncShownChecks();
    }

    private void CustomizeModule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) ShowModule(id);
    }

    /// <summary>Ouvre la personnalisation de ce module, sous l'entrée « Modules » (aussi depuis le menu clic droit du bureau).</summary>
    public void ShowModule(string id)
    {
        if (Families.FirstOrDefault(f => f.Id == id) is { } family) id = FamilyTarget(family);
        if (id == "shortcuts")
        {
            NavigateTo(NavModules, customize: true);
            _customizingDock = true;
            ShowPageFor(NavModules);
            return;
        }
        ModuleSelector.SelectedItem = ModuleSelector.Items.Cast<ModuleRow>().FirstOrDefault(m => m.Id == id);
        NavigateTo(NavModules, customize: true);
    }

    private bool _syncingShown;

    /// <summary>Interrupteurs « Afficher » (Personnaliser et barre de raccourcis) alignés sur l'état réel des modules.</summary>
    private void SyncShownChecks()
    {
        _syncingShown = true;
        try
        {
            DockShownCheck.IsChecked = _widgetManager.IsWidgetShown("shortcuts");
            if (SelectedModule is { } module) ModuleShownCheck.IsChecked = _widgetManager.IsWidgetShown(module.Id);
        }
        finally { _syncingShown = false; }
    }

    /// <summary>Interrupteur « Afficher » en haut de Personnaliser : affiche ou masque le module sur le bureau.</summary>
    private void ModuleShown_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _syncingShown || SelectedModule is not { } module) return;
        _widgetManager.ToggleWidget(module.Id, ModuleShownCheck.IsChecked == true);
        RefreshWidgetRows();
    }

    /// <summary>Onglets de la page Applications et plugins.</summary>
    private void ConnectionsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || IntegrationsContent is null || PagePlugins is null) return;
        var plugins = ConnectionsTabPlugins.IsChecked == true;
        IntegrationsContent.Visibility = plugins ? Visibility.Collapsed : Visibility.Visible;
        PagePlugins.Visibility = plugins ? Visibility.Visible : Visibility.Collapsed;
        SetIntegrationsTimer(!plugins && PageIntegrations.IsVisible);
        if (plugins) RefreshPluginsList();
    }

    /// <summary>Module masqué ou verrouillage changé depuis le bureau : la liste de la page Modules suit.</summary>
    private void OnDesktopStateChanged()
    {
        RefreshWidgetRows();
        SyncShownChecks();
        LockWidgetsCheck.IsChecked = _widgetManager.Config.WidgetsLocked;
    }

    private void LockWidgets_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _widgetManager.SetWidgetsLocked(LockWidgetsCheck.IsChecked == true);
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => App.Quit();
}
