using System.Windows;
using System.Windows.Controls;
using Oxyn.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Oxyn;

/// <summary>Personnalisation du module Barre de recherche : moteurs (dont ceux ajoutés), commandes /xxx choisies, apparence.</summary>
public partial class SettingsWindow
{
    private SearchBarSettings SearchBarConfig => _widgetManager.Config.SearchBar;
    private bool _fillingSearchEngines;

    private void BuildSearchBarEditor()
    {
        SearchBarCard.DataContext = SearchBarConfig;
        FillSearchEngineBox();
        BuildSearchFavoriteRows();
        BuildSearchEngineRows();
        BuildSearchCommandRows();
    }

    // ---------- Favoris de la barre ----------

    private void BuildSearchFavoriteRows()
    {
        SearchFavoritesItems.Children.Clear();
        var favorites = SearchBarConfig.Favorites;
        if (favorites.Count == 0)
            SearchFavoritesItems.Children.Add(new TextBlock
            {
                Text = "Aucun favori pour l'instant.", Margin = new Thickness(4, 4, 0, 8),
                Foreground = (System.Windows.Media.Brush)FindResource("Muted")
            });

        foreach (var favorite in favorites.ToList())
        {
            var name = new TextBox
            {
                Width = 180, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 60,
                Text = favorite.Name, ToolTip = "Nom affiché dans la barre : renomme-le comme tu veux"
            };
            name.LostFocus += (_, _) =>
            {
                if (name.Text.Trim() == favorite.Name) return;
                favorite.Name = name.Text.Trim();
                SearchBarConfig.FavoritesChanged();
            };

            var url = new TextBox
            {
                Width = 360, Height = 30, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center,
                Text = favorite.Url, ToolTip = "Adresse du site"
            };
            url.LostFocus += (_, _) =>
            {
                if (url.Text.Trim() == favorite.Url) return;
                favorite.Url = url.Text.Trim();
                SearchBarConfig.FavoritesChanged();
            };

            var up = SmallButton("\uE74A", "Monter");
            up.IsEnabled = favorites.IndexOf(favorite) > 0;
            up.Click += (_, _) =>
            {
                var i = favorites.IndexOf(favorite);
                if (i <= 0) return;
                (favorites[i - 1], favorites[i]) = (favorites[i], favorites[i - 1]);
                SearchBarConfig.FavoritesChanged();
                BuildSearchFavoriteRows();
            };
            var remove = SmallButton("\uE74D", "Retirer");
            remove.Click += (_, _) =>
            {
                favorites.Remove(favorite);
                SearchBarConfig.FavoritesChanged();
                BuildSearchFavoriteRows();
            };

            SearchFavoritesItems.Children.Add(new Border
            {
                Style = (Style)FindResource("RowBorder"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, url, up, remove } }
            });
        }
    }

    /// <summary>« Ajouter depuis le navigateur… » : favoris du navigateur par défaut, dossiers en sous-menus (déjà ajoutés = grisés).</summary>
    private void SearchFavoriteFromBrowser_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SearchFavoriteBrowserButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var bookmarks = SearchBar.LoadBookmarks();
        if (bookmarks.Count == 0)
        {
            menu.Items.Add(new MenuItem
            {
                IsEnabled = false,
                Header = SearchBar.DefaultBrowserName() is { } browser
                    ? $"Aucun favori trouvé dans {browser}"
                    : "Navigateur par défaut non pris en charge (Chrome, Edge, Brave ou Opera)"
            });
        }
        else
        {
            var used = SearchBarConfig.Favorites.Select(f => f.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in BookmarkMenuItems(bookmarks, used)) menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private IEnumerable<MenuItem> BookmarkMenuItems(IEnumerable<Bookmark> items, HashSet<string> used)
    {
        foreach (var bookmark in items)
        {
            if (bookmark.IsFolder)
            {
                if (bookmark.Children.Count == 0) continue;
                var folder = new MenuItem { Header = bookmark.Name };
                foreach (var child in BookmarkMenuItems(bookmark.Children, used)) folder.Items.Add(child);
                yield return folder;
                continue;
            }
            var entry = new MenuItem { Header = bookmark.Name, ToolTip = bookmark.Url, IsEnabled = !used.Contains(bookmark.Url!) };
            var chosen = bookmark;
            entry.Click += (_, _) => AddSearchFavorite(chosen.Name, chosen.Url!);
            yield return entry;
        }
    }

    private void SearchFavoriteAdd_Click(object sender, RoutedEventArgs e) => AddSearchFavorite("Mon site", "https://");

    private void AddSearchFavorite(string name, string url)
    {
        SearchBarConfig.Favorites.Add(new SearchFavorite { Name = name, Url = url });
        SearchBarConfig.FavoritesChanged();
        BuildSearchFavoriteRows();
    }

    /// <summary>Liste déroulante du moteur : moteurs intégrés puis moteurs ajoutés.</summary>
    private void FillSearchEngineBox()
    {
        _fillingSearchEngines = true;
        SearchEngineBox.Items.Clear();
        foreach (var engine in SearchBar.AllEngines(SearchBarConfig))
            SearchEngineBox.Items.Add(new ComboBoxItem { Content = engine.Name, Tag = engine.Id });
        var current = SearchBar.Engine(SearchBarConfig).Id;
        SearchEngineBox.SelectedItem = SearchEngineBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == current);
        _fillingSearchEngines = false;
    }

    private void SearchEngine_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingSearchEngines || SearchEngineBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        SearchBarConfig.Engine = id;
    }

    // ---------- Moteurs ajoutés ----------

    private void BuildSearchEngineRows()
    {
        SearchEnginesItems.Children.Clear();
        foreach (var engine in SearchBarConfig.CustomEngines.ToList())
        {
            var name = new TextBox
            {
                Width = 150, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 30,
                Text = engine.Name, ToolTip = "Nom affiché dans la liste des moteurs"
            };
            name.LostFocus += (_, _) =>
            {
                if (name.Text.Trim() == engine.Name) return;
                engine.Name = name.Text.Trim();
                SearchEnginesUpdated();
            };

            var url = new TextBox
            {
                Width = 400, Height = 30, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center,
                Text = engine.Url, ToolTip = "Adresse de recherche avec {texte} à la place des mots cherchés"
            };
            var invalid = new TextBlock
            {
                Text = "Adresse invalide", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.IndianRed,
                Visibility = SearchBar.NormalizeTemplate(engine.Url) is null ? Visibility.Visible : Visibility.Collapsed
            };
            url.LostFocus += (_, _) =>
            {
                invalid.Visibility = SearchBar.NormalizeTemplate(url.Text) is null ? Visibility.Visible : Visibility.Collapsed;
                if (url.Text.Trim() == engine.Url) return;
                engine.Url = url.Text.Trim();
                SearchEnginesUpdated();
            };

            var remove = SmallButton("\uE74D", "Retirer");
            remove.Click += (_, _) =>
            {
                SearchBarConfig.CustomEngines.Remove(engine);
                SearchEnginesUpdated();
                BuildSearchEngineRows();
            };

            SearchEnginesItems.Children.Add(new Border
            {
                Style = (Style)FindResource("RowBorder"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, url, remove, invalid } }
            });
        }
    }

    /// <summary>Moteurs modifiés : enregistrement, pastille du module et liste déroulante à jour.</summary>
    private void SearchEnginesUpdated()
    {
        SearchBarConfig.EnginesChanged();
        FillSearchEngineBox();
    }

    /// <summary>« Ajouter un moteur… » : sites prêts à l'emploi, ou moteur libre à compléter.</summary>
    private void SearchEngineAdd_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SearchEngineAddButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var custom = new MenuItem { Header = "Moteur personnalisé (nom et adresse à remplir)" };
        custom.Click += (_, _) => AddSearchEngine("Mon moteur", "https://exemple.fr/recherche?q={texte}");
        menu.Items.Add(custom);
        menu.Items.Add(new Separator());
        var used = SearchBarConfig.CustomEngines.Select(c => c.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (engineName, engineUrl) in SearchBar.EngineCatalog)
        {
            var item = new MenuItem { Header = engineName, IsEnabled = !used.Contains(engineUrl) };
            item.Click += (_, _) => AddSearchEngine(engineName, engineUrl);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void AddSearchEngine(string name, string url)
    {
        SearchBarConfig.CustomEngines.Add(new CustomSearchEngine { Name = name, Url = url });
        SearchEnginesUpdated();
        BuildSearchEngineRows();
    }

    // ---------- Commandes /xxx ----------

    private void BuildSearchCommandRows()
    {
        SearchCommandsItems.Children.Clear();
        if (SearchBarConfig.Commands.Count == 0)
            SearchCommandsItems.Children.Add(new TextBlock
            {
                Text = "Aucune commande pour l'instant.", Margin = new Thickness(4, 4, 0, 8),
                Foreground = (System.Windows.Media.Brush)FindResource("Muted")
            });

        foreach (var command in SearchBarConfig.Commands.ToList())
        {
            var name = new TextBox
            {
                Width = 130, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 20,
                Text = "/" + command.Name, ToolTip = "Nom tapé après / : renomme-la comme tu veux (sans espace)"
            };
            name.LostFocus += (_, _) =>
            {
                var clean = SearchBar.CommandKey(name.Text);
                if (clean.Length == 0) clean = command.Name;
                else if (clean != command.Name) clean = SearchBar.UniqueName(SearchBarConfig.Commands.Where(c => c != command), clean);
                name.Text = "/" + clean;
                if (clean == command.Name) return;
                command.Name = clean;
                SearchBarConfig.CommandsChanged();
            };

            FrameworkElement detail;
            if (command.Correction)
            {
                detail = new TextBlock
                {
                    Text = "Correction orthographique hors ligne (le texte corrigé est copié)", Width = 420,
                    Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)FindResource("Muted")
                };
            }
            else
            {
                var prompt = new TextBox
                {
                    Width = 420, Height = 30, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center,
                    Text = command.Prompt, ToolTip = "Prompt envoyé à l'IA de Google ; {texte} est remplacé par ce que tu tapes après la commande"
                };
                prompt.LostFocus += (_, _) =>
                {
                    if (prompt.Text == command.Prompt) return;
                    command.Prompt = prompt.Text;
                    SearchBarConfig.CommandsChanged();
                };
                detail = prompt;
            }

            var remove = SmallButton("\uE74D", "Retirer");
            remove.Click += (_, _) =>
            {
                SearchBarConfig.Commands.Remove(command);
                SearchBarConfig.CommandsChanged();
                BuildSearchCommandRows();
            };

            SearchCommandsItems.Children.Add(new Border
            {
                Style = (Style)FindResource("RowBorder"),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, detail, remove } }
            });
        }
    }

    /// <summary>« Ajouter une commande… » : catalogue (celles déjà ajoutées sont grisées) ou commande libre.</summary>
    private void SearchCommandAdd_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SearchCommandAddButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var custom = new MenuItem { Header = "Commande personnalisée (nom et prompt à remplir)" };
        custom.Click += (_, _) => AddSearchCommand(new SearchCommand { Name = "commande", Prompt = "{texte}" });
        menu.Items.Add(custom);
        menu.Items.Add(new Separator());
        foreach (var (title, preset) in SearchBar.Catalog())
        {
            var already = SearchBarConfig.Commands.Any(c => preset.Correction ? c.Correction : !c.Correction && c.Prompt == preset.Prompt);
            var item = new MenuItem { Header = $"{title}   /{preset.Name}", IsEnabled = !already };
            item.Click += (_, _) => AddSearchCommand(preset);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void AddSearchCommand(SearchCommand command)
    {
        command.Name = SearchBar.UniqueName(SearchBarConfig.Commands, command.Name);
        SearchBarConfig.Commands.Add(command);
        SearchBarConfig.CommandsChanged();
        BuildSearchCommandRows();
    }

    private void SearchBarLookReset_Click(object sender, RoutedEventArgs e) => SearchBarConfig.ResetLook();
}
