using System.IO;
using System.Runtime.CompilerServices;

namespace Oxyn.Tests;

internal static class TestEnvironment
{
    /// <summary>
    /// Avant tout test : les données d'OXYN (journal, icônes, fonds…) vont dans un dossier temporaire,
    /// jamais dans le vrai %AppData%\Oxyn de l'utilisateur. Doit précéder le premier accès à AppPaths.
    /// </summary>
#pragma warning disable CA2255 // initialiseur de module voulu : xUnit 2 n'a pas de fixture d'assemblage
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void IsolateDataDir()
        => Environment.SetEnvironmentVariable("OXYN_DATA_DIR", Path.Combine(Path.GetTempPath(), "oxyn-tests-data"));
}
