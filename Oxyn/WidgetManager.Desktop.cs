using System.Windows.Controls;
using System.Windows.Threading;

namespace Oxyn;

// Bureau : menu clic droit de chaque module (le reste se règle dans Personnaliser) et sauvegarde différée.
public sealed partial class WidgetManager
{
    /// <summary>Clic simple sur un module du bureau (les Paramètres ouvrent alors sa personnalisation s'ils y sont déjà).</summary>
    public event Action<string>? WidgetClicked;

    // Rafale de changements (nom tapé lettre par lettre…) : une seule écriture de config.json à la fin.
    private DispatcherTimer? _desktopSaveTimer;

    private void SaveConfigSoon()
    {
        if (_desktopSaveTimer is null)
        {
            _desktopSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _desktopSaveTimer.Tick += (_, _) =>
            {
                _desktopSaveTimer.Stop();
                SaveConfig();
            };
        }
        _desktopSaveTimer.Stop();
        _desktopSaveTimer.Start();
    }

    /// <summary>« Personnaliser ce module… » choisi sur le bureau : la fenêtre Paramètres s'ouvre sur ce module.</summary>
    public event Action<string>? CustomizeRequested;

    /// <summary>Module masqué ou verrouillage changé depuis le bureau : la fenêtre Paramètres se remet à jour.</summary>
    public event Action? DesktopStateChanged;

    /// <summary>Menu du module sur le bureau, reconstruit à chaque ouverture pour refléter l'état courant.</summary>
    private ContextMenu BuildWidgetMenu(string id)
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) => FillWidgetMenu(menu, id);
        return menu;
    }

    private void FillWidgetMenu(ContextMenu menu, string id)
    {
        menu.Items.Clear();

        MenuItem Item(string header, Action action, bool isChecked = false)
        {
            var item = new MenuItem { Header = header, IsCheckable = isChecked, IsChecked = isChecked };
            item.Click += (_, _) => action();
            return item;
        }

        var hasCard = id != "shortcuts"; // la barre de raccourcis a sa propre page de réglages et sa propre taille
        if (hasCard)
        {
            // Le module se modifie dans l'application (Personnaliser) ; sur le bureau, on le déplace seulement.
            menu.Items.Add(Item("Personnaliser ce module…", () => CustomizeRequested?.Invoke(id)));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item("Verrouiller la position des modules", () =>
        {
            SetWidgetsLocked(!_config.WidgetsLocked);
            DesktopStateChanged?.Invoke();
        }, _config.WidgetsLocked));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Masquer ce module", () =>
        {
            ToggleWidget(id, false);
            DesktopStateChanged?.Invoke();
        }));
    }

}
