using System.Windows;
using System.Windows.Controls;
using Oxyn.Services;

namespace Oxyn.Controls;

public sealed record ModulePreviewPart(string Key, string Label, string Section, Rect Bounds);

// Disposition libre des éléments déplaçables (Movable) et mode édition avec leurs cadres.
public partial class ModuleCard
{
    /// <summary>Clés des éléments déplaçables du module, dans l'ordre d'affichage.</summary>
    public IReadOnlyList<string> MovableKeys
        => Movables.Select(e => Movable.GetKey(e)!).Distinct().ToList();

    /// <summary>Éléments déplaçables du module (édition depuis l'aperçu de la Switch).</summary>
    public IReadOnlyList<FrameworkElement> MovableElements => Movables.ToList();

    public IReadOnlyList<ModulePreviewPart> GetPreviewParts()
    {
        if (!IsLoaded) return Array.Empty<ModulePreviewPart>();

        var parts = new List<ModulePreviewPart>();
        foreach (var element in Movables)
        {
            if (Movable.GetKey(element) is not { } key || !Movable.IsDisplayed(element)) continue;
            AddPreviewPart(parts, key, Movable.LabelFor(key), "elements", element);
        }

        if (TextPart.ByModule.TryGetValue(_id, out var textInfos))
        {
            foreach (var text in FindVisualChildren<TextBlock>(ContentHost))
            {
                if (TextPart.GetKey(text) is not { } key || textInfos.FirstOrDefault(part => part.Key == key) is not { } info)
                    continue;
                AddPreviewPart(parts, key, info.Label, "texts", text);
            }
        }

        if (ButtonPart.ByModule.TryGetValue(_id, out var buttonInfos))
        {
            foreach (var button in FindVisualChildren<Button>(ContentHost))
            {
                if (ButtonPart.GetKey(button) is not { } key || buttonInfos.FirstOrDefault(part => part.Key == key) is not { } info)
                    continue;
                AddPreviewPart(parts, key, info.Label, "buttons", button);
            }
        }

        return parts;
    }

    private void AddPreviewPart(List<ModulePreviewPart> parts, string key, string label, string section, FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
        // Cadre = ce qui est réellement dessiné (texte, anneau…), pas toute la place réservée à l'élément.
        var bounds = element.TransformToAncestor(this).TransformBounds(Movable.ContentBounds(element));
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

        var existing = parts.FindIndex(part => part.Key == key && part.Section == section);
        if (existing < 0)
            parts.Add(new ModulePreviewPart(key, label, section, bounds));
        else
            parts[existing] = parts[existing] with { Bounds = Rect.Union(parts[existing].Bounds, bounds) };
    }

    /// <summary>Taille actuelle de la carte, pour initialiser la taille manuelle à partir de l'automatique.</summary>
    public Size CurrentCardSize => new(Chrome.ActualWidth, Chrome.ActualHeight);

    // ---------- Disposition libre des éléments ----------

    private ModuleAppearance? _appearance;
    private List<FrameworkElement>? _movables;
    private readonly HashSet<System.Windows.Controls.ItemsControl> _watchedLists = new();

    // Mis en cache une fois la carte chargée (l'arbre visuel existe) ; vidé par ElementsChanged et à chaque
    // chargement/déchargement. Avant le chargement, ou si rien n'est encore trouvé (modèles pas générés),
    // on ne retient rien : un cache vide figerait le résultat.
    private List<FrameworkElement> Movables
    {
        get
        {
            if (_movables is not null) return _movables;
            var found = Movable.FindAll(ContentHost);
            if (IsLoaded && found.Count > 0) _movables = found;
            return found;
        }
    }

    /// <summary>Recherche à nouveau les éléments déplaçables (le contenu du module a changé).</summary>
    private void RefreshMovables()
    {
        _movables = null;
        ApplyElementLayouts(); // nouveaux éléments à leur place
    }

    public void ApplyElementLayouts()
    {
        if (!IsLoaded || _appearance is null) return;
        foreach (var element in Movables)
            ApplyElementLayout(element);
        UpdateFit();
    }

    private void ApplyElementLayout(FrameworkElement element)
    {
        var key = Movable.GetKey(element)!;
        _appearance!.Elements.TryGetValue(key, out var layout);
        Movable.Apply(element, layout);
        // Lignes de liste (sorties audio, applications) déplacées une à une : réappliquées à chaque régénération.
        foreach (var list in Movable.ApplyItems(element, key, _appearance.Elements))
            if (_watchedLists.Add(list))
                list.ItemContainerGenerator.StatusChanged += (_, _) =>
                {
                    if (list.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated
                        && _appearance is not null && Movable.GetKey(element) is { } k)
                        Movable.ApplyItems(element, k, _appearance.Elements);
                };
        UpdateFit(); // un déplacement (transformation) ne relance pas la mise en page : on recadre ici
    }

    /// <summary>Clés des éléments actuellement masqués du module.</summary>
    public IReadOnlyList<string> HiddenKeys()
        => _appearance is null ? Array.Empty<string>()
            : MovableKeys.Where(k => _appearance.Elements.TryGetValue(k, out var l) && l.Hidden).ToList();
}
