using System.Windows;
using System.Windows.Threading;
using Oxyn.Infrastructure;
using Oxyn.Services;
using Oxyn.Services.ConsoleMode;

namespace Oxyn;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private WidgetManager? _widgetManager;
    private TrayIconService? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private DateTime _lastErrorDialog = DateTime.MinValue;
    private bool _startupComplete;
    // Activation (second lancement) reçue avant que le gestionnaire de modules existe : traitée une fois prêt.
    private bool _openSettingsWhenReady;

    /// <summary>
    /// Vrai dès qu'OXYN se ferme (Quitter, fermeture de session) : WPF ferme alors lui-même les
    /// fenêtres des modules, ce qui ne doit pas être pris pour un redémarrage d'Explorer.
    /// </summary>
    public static bool IsExiting { get; private set; }

    /// <summary>Quelques secondes après le démarrage : bulle si un installateur plus récent attend dans le dossier des mises à jour.</summary>
    private async void CheckForUpdateLater()
    {
        try
        {
            var settings = _widgetManager?.Config.Updates;
            if (settings is not { AutoCheck: true }) return;
            await Task.Delay(TimeSpan.FromSeconds(8));
            var update = await Task.Run(() => UpdateService.Available(settings));
            if (update == null) return;
            Log.Info($"Mise à jour disponible : {update.Version} ({update.SetupPath}).");
            TrayIconService.ShowNotice($"Mise à jour {update.Version} disponible : Paramètres → Général → Mettre à jour.");
        }
        catch (Exception ex)
        {
            Log.Warn("Recherche de mise à jour au démarrage", ex);
        }
    }

    /// <summary>Quitte OXYN (menu de l'icône, bouton des Paramètres).</summary>
    public static void Quit()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        RegisterGlobalErrorHandlers();

        try
        {
            // AppPaths.DataDir crée le dossier de données (variable OXYN_DATA_DIR invalide, droits...) :
            // sans ce try, l'erreur sortirait comme un plantage muet au démarrage.
            _ = AppPaths.DataDir;
            _singleInstance = new SingleInstance(() => Dispatcher.BeginInvoke(OpenSettings));
        }
        catch (Exception ex)
        {
            var folder = Environment.GetEnvironmentVariable("OXYN_DATA_DIR");
            Log.Error("Dossier de données inutilisable", ex);
            MessageBox.Show($"OXYN ne peut pas utiliser son dossier de données"
                            + (string.IsNullOrWhiteSpace(folder) ? "" : $" (« {folder} », défini par OXYN_DATA_DIR)")
                            + $" :\n{ex.Message}\n\nVérifiez que le dossier existe, est accessible en écriture, ou supprimez la variable OXYN_DATA_DIR.",
                "OXYN", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (!_singleInstance.IsFirst)
        {
            // OXYN tourne déjà : on lui demande d'ouvrir ses Paramètres et on s'arrête là.
            _singleInstance.ActivateFirstInstance();
            Shutdown();
            return;
        }

        Log.Info($"Démarrage d'OXYN {typeof(App).Assembly.GetName().Version} (données : {AppPaths.DataDir})");

        // Mode console interrompu (plantage, coupure) : écrans, icônes et audio sont remis AVANT de créer les modules.
        var consoleService = new ConsoleModeService();
        try
        {
            if (consoleService.RecoverIfNeeded()) Log.Info("Mode console : configuration d'avant le plantage restaurée.");
        }
        catch (Exception ex)
        {
            Log.Warn("Mode console : reprise au démarrage en échec", ex);
        }

        try
        {
            Oxyn.ConsoleMode.CursorHider.RepairAfterCrash();   // curseur resté invisible après un plantage ?
            _widgetManager = new WidgetManager(consoleService);
        }
        catch (Exception ex)
        {
            Log.Error("Échec du démarrage", ex);
            MessageBox.Show($"OXYN n'a pas pu démarrer :\n{ex.Message}\n\nDétails dans {AppPaths.LogDir}\\oxyn.log",
                "OXYN", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _trayIcon = new TrayIconService();
        _trayIcon.SettingsRequested += OpenSettings;
        _trayIcon.ExitRequested += Quit;
        SessionEnding += (_, _) => IsExiting = true;
        _widgetManager.CustomizeRequested += id =>
        {
            OpenSettings();
            _settingsWindow?.ShowModule(id);
        };

        // « Oxyn.exe --settings » ouvre directement les Paramètres (raccourci, tests).
        if (_openSettingsWhenReady || e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
            OpenSettings();

        _widgetManager.EnsureTvDisabledIfConfigured();
        CheckForUpdateLater();

        _startupComplete = true;
    }

    /// <summary>
    /// Toute exception non rattrapée est journalisée (pile complète). Celles du thread d'interface sont absorbées
    /// une fois OXYN démarré : un widget qui échoue ne doit pas faire disparaître tous les autres. Pendant le
    /// démarrage, ou pour une erreur fatale (mémoire...), on n'absorbe rien : continuer à moitié initialisé
    /// cacherait le vrai problème.
    /// </summary>
    private void RegisterGlobalErrorHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Exception non gérée (interface)", args.Exception);
            if (!_startupComplete || args.Exception is OutOfMemoryException or AccessViolationException or System.Runtime.InteropServices.SEHException)
                return; // non absorbée : WPF termine l'application après la journalisation
            args.Handled = true;

            // Une erreur qui se répète à chaque rafraîchissement ne doit pas ouvrir une fenêtre par seconde.
            if (DateTime.Now - _lastErrorDialog < TimeSpan.FromMinutes(1)) return;
            _lastErrorDialog = DateTime.Now;
            MessageBox.Show($"Une erreur inattendue s'est produite, OXYN continue de fonctionner.\n\n{args.Exception.Message}\n\nDétails dans {AppPaths.LogDir}\\oxyn.log",
                "OXYN", MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Exception non observée (tâche)", args.Exception);
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error($"Exception fatale (arrêt : {args.IsTerminating})", args.ExceptionObject as Exception);
    }

    private void OpenSettings()
    {
        if (_widgetManager is null)
        {
            _openSettingsWhenReady = true;
            return;
        }

        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_widgetManager);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _widgetManager?.Dispose();
            _trayIcon?.Dispose();
            Oxyn.ConsoleMode.CursorHider.Show();   // jamais de curseur invisible après OXYN
        }
        catch (Exception ex)
        {
            Log.Error("Erreur pendant la fermeture", ex);
        }
        finally
        {
            _singleInstance?.Dispose();
            if (_widgetManager is not null) Log.Info("Arrêt d'OXYN");
        }
        base.OnExit(e);
    }
}
