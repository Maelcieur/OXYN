using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Oxyn.Infrastructure;

namespace Oxyn.Services;

/// <summary>
/// Correction orthographique hors ligne avec le correcteur intégré à Windows (API ISpellChecker, Windows 8+) :
/// chaque faute est remplacée par la correction proposée ou, à défaut, par la première suggestion. Rien n'est envoyé sur Internet.
/// </summary>
public static class SpellCorrector
{
    /// <summary>Faute trouvée : position, longueur et texte de remplacement (vide = suppression, ex. mot répété).</summary>
    public readonly record struct Fix(int Start, int Length, string Replacement);

    private static readonly object Gate = new();
    private static ISpellChecker? _checker;
    private static string? _language;

    /// <summary>Langue utilisée (fr-FR en priorité, sinon la langue de Windows, sinon en-US), null si aucun dictionnaire.</summary>
    public static string? Language { get { lock (Gate) { EnsureChecker(); return _language; } } }

    /// <summary>Texte corrigé (identique s'il n'y a rien à corriger), null si le correcteur de Windows est indisponible.</summary>
    public static string? Correct(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        lock (Gate)
        {
            try
            {
                if (EnsureChecker() is not { } checker) return null;
                return Apply(text, FindFixes(checker, text));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                Log.Warn("Correcteur orthographique de Windows en échec", ex);
                _checker = null;
                return null;
            }
        }
    }

    /// <summary>Applique les corrections de la fin vers le début (les positions restent valables). Ignore celles qui se chevauchent.</summary>
    public static string Apply(string text, IEnumerable<Fix> fixes)
    {
        var result = text;
        var limit = text.Length;
        foreach (var fix in fixes.OrderByDescending(f => f.Start))
        {
            if (fix.Start < 0 || fix.Length < 0 || fix.Start + fix.Length > limit) continue;
            var replacement = fix.Replacement;
            // Mot répété supprimé : on retire aussi l'espace qui le précède (« le le chat » → « le chat »).
            var start = fix.Start;
            var length = fix.Length;
            if (replacement.Length == 0 && start > 0 && char.IsWhiteSpace(result[start - 1])) { start--; length++; }
            result = result.Remove(start, length).Insert(start, replacement);
            limit = start;
        }
        return result;
    }

    private static List<Fix> FindFixes(ISpellChecker checker, string text)
    {
        var fixes = new List<Fix>();
        checker.ComprehensiveCheck(text, out var errors);
        try
        {
            while (errors.Next(out var error) == 0 && error is not null)
            {
                try
                {
                    var start = (int)error.StartIndex;
                    var length = (int)error.Length;
                    var replacement = error.CorrectiveAction switch
                    {
                        CorrectiveAction.Replace => error.Replacement,
                        CorrectiveAction.Delete => "",
                        CorrectiveAction.GetSuggestions => FirstSuggestion(checker, text.Substring(start, length)),
                        _ => null,
                    };
                    if (replacement is not null) fixes.Add(new Fix(start, length, replacement));
                }
                finally { Marshal.ReleaseComObject(error); }
            }
        }
        finally { Marshal.ReleaseComObject(errors); }
        return fixes;
    }

    private static string? FirstSuggestion(ISpellChecker checker, string word)
    {
        checker.Suggest(word, out var suggestions);
        try
        {
            var item = new string[1];
            return suggestions.Next(1, item, IntPtr.Zero) == 0 ? item[0] : null;
        }
        finally { Marshal.ReleaseComObject(suggestions); }
    }

    private static ISpellChecker? EnsureChecker()
    {
        if (_checker is not null) return _checker;
        try
        {
            var factory = (ISpellCheckerFactory)new SpellCheckerFactoryClass();
            foreach (var tag in new[] { "fr-FR", CultureInfo.CurrentUICulture.Name, CultureInfo.CurrentCulture.Name, "en-US" })
            {
                if (string.IsNullOrEmpty(tag)) continue;
                factory.IsSupported(tag, out var supported);
                if (!supported) continue;
                factory.CreateSpellChecker(tag, out _checker);
                _language = tag;
                return _checker;
            }
            Log.WarnOnce("spellchecker-language", "Aucun dictionnaire du correcteur de Windows (français ou anglais) n'est installé.");
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Log.WarnOnce("spellchecker", "Correcteur orthographique de Windows indisponible", ex);
        }
        return null;
    }

    // ---------- Interop : spellcheck.h ----------

    private enum CorrectiveAction { None = 0, GetSuggestions = 1, Replace = 2, Delete = 3 }

    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
    private class SpellCheckerFactoryClass { }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        void get_SupportedLanguages(out IEnumString languages);
        void IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, [MarshalAs(UnmanagedType.Bool)] out bool supported);
        void CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker checker);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        void get_LanguageTag([MarshalAs(UnmanagedType.LPWStr)] out string tag);
        void Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError errors);
        void Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumString suggestions);
        void Add([MarshalAs(UnmanagedType.LPWStr)] string word);
        void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
        void AutoCorrect([MarshalAs(UnmanagedType.LPWStr)] string from, [MarshalAs(UnmanagedType.LPWStr)] string to);
        void GetOptionValue([MarshalAs(UnmanagedType.LPWStr)] string optionId, out byte value);
        void get_OptionIds(out IEnumString ids);
        void get_Id([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void get_LocalizedName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void add_SpellCheckerChanged(IntPtr handler, out uint cookie);
        void remove_SpellCheckerChanged(uint cookie);
        void GetOptionDescription([MarshalAs(UnmanagedType.LPWStr)] string optionId, out IntPtr description);
        void ComprehensiveCheck([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError errors);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig] int Next(out ISpellingError? error);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        uint StartIndex { get; }
        uint Length { get; }
        CorrectiveAction CorrectiveAction { get; }
        string Replacement { [return: MarshalAs(UnmanagedType.LPWStr)] get; }
    }
}
