using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Oxyn.Controls;

namespace Oxyn;

// Sections repliables des pages de réglages : chaque titre de section (style SectionHeader) devient un bouton qui
// replie ou déplie les éléments qui le suivent, jusqu'au titre suivant. Rien n'est déplacé dans l'arbre (la recherche
// s'appuie sur le titre placé juste avant chaque carte) : une section repliée a seulement une hauteur nulle, ce qui
// laisse intacte la Visibility que le code règle par ailleurs. Les sections ouvertes sont mémorisées (configuration) ;
// par défaut, seule la première section de chaque page est ouverte.
public partial class SettingsWindow
{
    private readonly Dictionary<ContentControl, (string Key, List<FrameworkElement> Items)> _sections = new();
    private readonly Dictionary<FrameworkElement, ContentControl> _sectionOf = new();
    private readonly Dictionary<FrameworkElement, (double MaxHeight, Thickness Margin)> _sectionSaved = new();
    private HashSet<string> _openSections = new();

    private void InitCollapsibleSections()
    {
        var headerStyle = (Style)FindResource("SectionHeader");
        var saved = _widgetManager.Config.SettingsOpenSections;
        _openSections = saved is null ? new HashSet<string>() : new HashSet<string>(saved);

        foreach (var (page, pageKey) in new (FrameworkElement, string)[]
        {
            (PageProfiles, "profils"), (PageAlerts, "alertes"), (SwitchSettingsPanel, "switch"), (PageConsole, "console"),
            (PageIntegrations, "connexions"), (PageTools, "outils"), (PageGeneral, "general"), (PageDock, "dock"),
        })
        {
            var first = true;
            foreach (var panel in SectionPanels(page))
            {
                ContentControl? current = null;
                foreach (var child in panel.Children.OfType<FrameworkElement>().ToList())
                {
                    if (child is ContentControl header && ReferenceEquals(header.Style, headerStyle) && header.Content is string title)
                    {
                        var key = $"{pageKey}:{title}";
                        _sections[header] = (key, new List<FrameworkElement>());
                        header.Cursor = System.Windows.Input.Cursors.Hand;
                        header.MouseLeftButtonUp += (_, e) => { ToggleSection(header); e.Handled = true; };
                        // Par défaut (aucun choix enregistré) : la première section de la page est ouverte.
                        if (saved is null && first) _openSections.Add(key);
                        first = false;
                        current = header;
                    }
                    else if (current is not null)
                    {
                        _sections[current].Items.Add(child);
                        _sectionOf[child] = current;
                    }
                }
            }
        }
        foreach (var header in _sections.Keys) ApplySection(header, _openSections.Contains(_sections[header].Key));
    }

    /// <summary>Panneaux d'une page qui contiennent des titres de section (sans descendre dans les listes ni les éditeurs de style).</summary>
    private static IEnumerable<Panel> SectionPanels(DependencyObject root)
    {
        if (root is Panel panel) yield return panel;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is ItemsControl or ThemeEditor) continue;
            foreach (var inner in SectionPanels(child)) yield return inner;
        }
    }

    private void ToggleSection(ContentControl header)
    {
        var (key, _) = _sections[header];
        var open = !_openSections.Contains(key);
        if (open) _openSections.Add(key); else _openSections.Remove(key);
        ApplySection(header, open);
        _widgetManager.Config.SettingsOpenSections = _openSections.OrderBy(k => k, StringComparer.Ordinal).ToList();
        ScheduleSave();
    }

    private void ApplySection(ContentControl header, bool open)
    {
        header.Tag = open ? "open" : "closed";
        foreach (var item in _sections[header].Items)
        {
            if (open)
            {
                if (_sectionSaved.Remove(item, out var s))
                {
                    item.MaxHeight = s.MaxHeight;
                    item.Margin = s.Margin;
                }
            }
            else if (!_sectionSaved.ContainsKey(item))
            {
                _sectionSaved[item] = (item.MaxHeight, item.Margin);
                item.MaxHeight = 0;
                item.Margin = new Thickness(0);
            }
        }
    }

    /// <summary>
    /// Ouvre tout ce qui cache <paramref name="target"/> : sections repliées et listes déroulantes (Expander) qui le
    /// contiennent. Utilisé par la recherche et par les liens qui mènent à un réglage précis.
    /// </summary>
    private void RevealSectionsOf(FrameworkElement target)
    {
        for (DependencyObject? node = target; node is not null; node = LogicalTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement element && _sectionOf.TryGetValue(element, out var header)
                && !_openSections.Contains(_sections[header].Key))
                ToggleSection(header);
            if (node is Expander { IsExpanded: false } expander) expander.IsExpanded = true;
        }
    }
}
