using System.Runtime.InteropServices;
using System.Windows;

namespace Oxyn.ConsoleMode;

/// <summary>
/// « Plein écran fenêtré » forcé (option du jeu) : retire la barre de titre et les bords de la fenêtre du jeu et
/// l'étend sur toute la TV. Pour les jeux qui n'ont que « plein écran » (exclusif) et « fenêtré » : en plein écran
/// exclusif, le jeu se réduit dès qu'il perd le focus (menu Guide) et la TV change de mode.
/// </summary>
public static class BorderlessWindow
{
    private const int GwlStyle = -16, GwlExStyle = -20;
    private const int WsCaption = 0x00C00000, WsThickFrame = 0x00040000, WsSysMenu = 0x00080000,
        WsMinimizeBox = 0x00020000, WsMaximizeBox = 0x00010000;
    private const int FrameStyles = WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox;
    private const int WsExDlgModalFrame = 0x1, WsExWindowEdge = 0x100, WsExClientEdge = 0x200, WsExStaticEdge = 0x20000;
    private const int FrameExStyles = WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge;
    private const uint SwpNoActivate = 0x10, SwpFrameChanged = 0x20, SwpShowWindow = 0x40, SwpNoOwnerZOrder = 0x200;

    /// <summary>
    /// Met <paramref name="hwnd"/> sans bordure sur <paramref name="boundsPx"/> (pixels physiques) s'il ne l'est pas
    /// déjà. Renvoie vrai si la fenêtre a été modifiée. Ne fait rien sur une fenêtre réduite.
    /// </summary>
    public static bool Apply(IntPtr hwnd, Rect boundsPx)
    {
        if (hwnd == IntPtr.Zero || IsIconic(hwnd) || !IsWindow(hwnd)) return false;
        var style = GetWindowLong(hwnd, GwlStyle);
        var ex = GetWindowLong(hwnd, GwlExStyle);
        int x = (int)boundsPx.X, y = (int)boundsPx.Y, w = (int)boundsPx.Width, h = (int)boundsPx.Height;
        var framed = (style & FrameStyles) != 0 || (ex & FrameExStyles) != 0;
        var placed = GetWindowRect(hwnd, out var r) && r.Left == x && r.Top == y && r.Right - r.Left == w && r.Bottom - r.Top == h;
        if (!framed && placed) return false;
        if (framed)
        {
            SetWindowLong(hwnd, GwlStyle, style & ~FrameStyles);
            SetWindowLong(hwnd, GwlExStyle, ex & ~FrameExStyles);
        }
        SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, SwpFrameChanged | SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
