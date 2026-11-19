using System.Windows;
using Oxyn.Services;

namespace Oxyn;

/// <summary>Page Général : mise à jour par dossier (sans GitHub).</summary>
public partial class SettingsWindow
{
    private UpdateSettings UpdateConfig => _widgetManager.Config.Updates;
    private UpdateInfo? _update;
    private bool _updatesLoading;

    private void InitUpdates()
    {
        _updatesLoading = true;
        UpdateAutoCheck.IsChecked = UpdateConfig.AutoCheck;
        _updatesLoading = false;
        ShowUpdateFolder();
        _ = RefreshUpdateAsync();
    }

    private void ShowUpdateFolder()
    {
        var folder = UpdateService.FolderOf(UpdateConfig);
        UpdateFolderText.Text = string.IsNullOrEmpty(folder)
            ? "Aucun dossier choisi."
            : (string.IsNullOrWhiteSpace(UpdateConfig.Folder) ? "Par défaut : " : "") + folder;
    }

    /// <summary>Cherche l'installateur et affiche l'état (version installée, version trouvée).</summary>
    private async Task RefreshUpdateAsync()
    {
        UpdateStatusText.Text = "Recherche…";
        var folder = UpdateService.FolderOf(UpdateConfig);
        var found = await Task.Run(() => UpdateService.Find(folder));
        var current = UpdateService.CurrentVersion;
        _update = found != null && UpdateService.IsNewer(found, current, UpdateService.InstalledUtc, UpdateConfig.LastInstalledStamp) ? found : null;
        UpdateInstallButton.Visibility = _update != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatusText.Text = _update != null
            ? $"Mise à jour disponible : version {_update.Version} (installateur du {_update.BuiltUtc.ToLocalTime():dd/MM/yyyy à HH:mm}). Version installée : {current}."
            : found != null
                ? $"OXYN est à jour (version {current})."
                : string.IsNullOrEmpty(folder)
                    ? $"Version {current}. Choisis le dossier où tu déposes le nouvel installateur."
                    : $"Version {current}. Aucun installateur ({UpdateService.SetupName}) dans le dossier.";
    }

    private async void UpdateCheck_Click(object sender, RoutedEventArgs e) => await RefreshUpdateAsync();

    private void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        var update = _update;
        if (update == null) return;
        var answer = MessageBox.Show(this,
            $"Installer la version {update.Version} ?\n\nOXYN va se fermer quelques secondes, s'installer par-dessus (réglages gardés) puis se relancer tout seul.",
            "Mise à jour d'OXYN", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;
        UpdateConfig.LastInstalledStamp = update.Stamp;
        _widgetManager.SaveConfig();
        if (UpdateService.StartInstall(update))
        {
            App.Quit();
            return;
        }
        UpdateConfig.LastInstalledStamp = "";
        _widgetManager.SaveConfig();
        UpdateStatusText.Text = "L'installateur n'a pas pu être lancé (détails dans le journal).";
    }

    private void UpdateFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Dossier où tu déposes le nouvel installateur d'OXYN" };
        var current = UpdateService.FolderOf(UpdateConfig);
        if (!string.IsNullOrEmpty(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) != true) return;
        UpdateConfig.Folder = dialog.FolderName;
        ScheduleSave();
        ShowUpdateFolder();
        _ = RefreshUpdateAsync();
    }

    private void UpdateFolderReset_Click(object sender, RoutedEventArgs e)
    {
        UpdateConfig.Folder = "";
        ScheduleSave();
        ShowUpdateFolder();
        _ = RefreshUpdateAsync();
    }

    private void UpdateAuto_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatesLoading) return;
        UpdateConfig.AutoCheck = UpdateAutoCheck.IsChecked == true;
        ScheduleSave();
    }
}
