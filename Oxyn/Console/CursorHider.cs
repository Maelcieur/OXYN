using System.IO;
using System.Runtime.InteropServices;
using Oxyn.Infrastructure;

namespace Oxyn.ConsoleMode;

/// <summary>
/// Masque le curseur de la souris pendant un jeu du mode console, quand la souris ne bouge plus depuis 2 s
/// (les émulateurs ne le cachent pas tous en plein écran). Windows n'a pas de « masquer le curseur » global :
/// les curseurs système sont remplacés par un curseur transparent, puis Windows recharge les vrais
/// (<c>SPI_SETCURSORS</c>) dès que la souris bouge, à la sortie du jeu, ou au démarrage suivant si OXYN
/// avait planté pendant que le curseur était masqué (fichier témoin).
/// </summary>
public static class CursorHider
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    // Curseurs standard de Windows (OCR_*) : flèche, texte, attente, croix, flèche haut, redimensionnements, interdit, main, démarrage.
    private static readonly uint[] SystemCursors = { 32512, 32513, 32514, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649, 32650, 32651 };
    private static string MarkerFile => Path.Combine(AppPaths.DataDir, "cursor-hidden.flag");

    private static bool _hidden;
    private static POINT _lastPos;
    private static DateTime _lastMove = DateTime.Now;

    /// <summary>À appeler régulièrement (≈ 2 fois par seconde) : <paramref name="active"/> = un jeu est affiché.</summary>
    public static void Update(bool active)
    {
        if (!active)
        {
            Show();
            return;
        }
        GetCursorPos(out var pos);
        if (pos.X != _lastPos.X || pos.Y != _lastPos.Y)
        {
            _lastPos = pos;
            _lastMove = DateTime.Now;
            Show();   // la souris bouge : curseur visible tout de suite
            return;
        }
        if (!_hidden && DateTime.Now - _lastMove >= IdleDelay) Hide();
    }

    private static void Hide()
    {
        try
        {
            File.WriteAllText(MarkerFile, DateTime.Now.ToString("O"));   // témoin : à réparer au prochain démarrage en cas de plantage
        }
        catch (Exception ex)
        {
            Log.WarnOnce("cursor-marker", "Curseur : fichier témoin impossible à écrire", ex);
        }
        var andMask = new byte[32 * 32 / 8];
        var xorMask = new byte[32 * 32 / 8];
        for (var i = 0; i < andMask.Length; i++) andMask[i] = 0xFF;   // AND = 1, XOR = 0 : pixel transparent
        foreach (var id in SystemCursors)
        {
            // SetSystemCursor détruit le curseur reçu : un nouveau pour chaque curseur système.
            var blank = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, andMask, xorMask);
            if (blank != IntPtr.Zero && !SetSystemCursor(blank, id)) DestroyCursor(blank);
        }
        _hidden = true;
    }

    /// <summary>Remet les vrais curseurs de Windows (sans effet s'ils sont déjà visibles).</summary>
    public static void Show()
    {
        if (!_hidden) return;
        Restore();
    }

    /// <summary>Au démarrage d'OXYN : remet les curseurs si un plantage les a laissés invisibles.</summary>
    public static void RepairAfterCrash()
    {
        if (File.Exists(MarkerFile))
        {
            Log.Warn("Curseur : resté masqué après un arrêt brutal, curseurs de Windows rechargés.");
            Restore();
        }
    }

    private static void Restore()
    {
        SystemParametersInfo(0x0057 /* SPI_SETCURSORS */, 0, IntPtr.Zero, 0);
        _hidden = false;
        try { File.Delete(MarkerFile); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] andPlane, byte[] xorPlane);
    [DllImport("user32.dll")] private static extern bool SetSystemCursor(IntPtr hcur, uint id);
    [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr hCursor);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint winIni);
}
