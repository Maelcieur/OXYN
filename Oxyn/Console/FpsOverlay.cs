using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Oxyn.ConsoleMode;

/// <summary>
/// Compteur d'images par seconde affiché par-dessus le jeu, en haut à droite de la TV (réglage du mode console).
/// Fenêtre transparente aux clics, qui ne prend jamais le premier plan ni le focus : le jeu n'est pas dérangé.
/// Ne s'affiche pas au-dessus d'un jeu en plein écran exclusif (il faut le mode fenêtré sans bordure).
/// </summary>
public sealed class FpsOverlay : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExLayered = 0x80000;
    private const uint SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly TextBlock _value = new() { FontSize = 30, FontWeight = FontWeights.Bold, VerticalAlignment = System.Windows.VerticalAlignment.Center };
    private readonly TextBlock _unit = new() { Text = " FPS", FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
    private readonly Rect _bounds;

    public FpsOverlay(Rect boundsPx)
    {
        _bounds = boundsPx;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Bahnschrift SemiBold, Segoe UI");
        _unit.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_value);
        row.Children.Add(_unit);
        Content = new Border
        {
            Child = row, Padding = new Thickness(14, 6, 14, 6), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x05, 0x07, 0x0B)),
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExLayered);
        };
        SizeChanged += (_, _) => Place();
    }

    /// <summary>Affiche une valeur (null = rien de mesuré : « — »), ou un court message (autorisation manquante...).</summary>
    public void SetFps(double? fps, string? message = null)
    {
        if (message != null)
        {
            _value.Text = message;
            _value.FontSize = 18;
            _value.Foreground = Brushes.White;
            _unit.Visibility = Visibility.Collapsed;
        }
        else
        {
            _value.FontSize = 30;
            _unit.Visibility = Visibility.Visible;
            _value.Text = fps is { } f ? Math.Round(f).ToString("0") : "—";
            _value.Foreground = new SolidColorBrush(fps switch
            {
                null => Color.FromRgb(0xB8, 0xC0, 0xCE),
                >= 55 => Color.FromRgb(0x4A, 0xDE, 0x80),   // fluide
                >= 30 => Color.FromRgb(0xFA, 0xCC, 0x15),   // correct
                _ => Color.FromRgb(0xF8, 0x71, 0x71),       // saccadé
            });
        }
        if (!IsVisible) Show();
        Place();
    }

    /// <summary>En haut à droite de la TV, toujours au-dessus (un jeu peut repasser devant une fenêtre « topmost »).</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var margin = (int)(24 * dpi.DpiScaleX);
        SetWindowPos(hwnd, HwndTopmost, (int)(_bounds.Right - w - margin), (int)_bounds.Top + margin, 0, 0, SwpNoActivate | SwpShowWindow | 0x0001);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
