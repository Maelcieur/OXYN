using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Oxyn.ConsoleMode;

/// <summary>
/// Notification de batterie de manette affichée par-dessus le jeu, en haut à gauche de la TV (« Manette 1 · 50 % »).
/// Même principe que le compteur FPS : fenêtre transparente aux clics, qui ne prend jamais le premier plan ni le focus,
/// donc le jeu n'est pas dérangé. Apparaît en fondu, reste 5 s puis disparaît.
/// </summary>
public sealed class BatteryToast : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExLayered = 0x80000;
    private const uint SwpNoActivate = 0x0010, SwpShowWindow = 0x0040, SwpNoSize = 0x0001;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly Color Low = Color.FromRgb(0xF8, 0x71, 0x71);

    private readonly Rect _bounds;
    private readonly Border _box = new()
    {
        Padding = new Thickness(18, 10, 20, 10), CornerRadius = new CornerRadius(12),
        Background = new SolidColorBrush(Color.FromArgb(0xB3, 0x05, 0x07, 0x0B)),
        BorderThickness = new Thickness(1.5),
    };
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(5) };

    public BatteryToast(Rect boundsPx)
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
        Content = _box;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExTransparent | WsExToolWindow | WsExNoActivate | WsExLayered);
        };
        SizeChanged += (_, _) => Place();
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
            fade.Completed += (_, _) => { if (!_hide.IsEnabled) Hide(); };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    /// <summary>Affiche « Manette N · xx % » (rouge et « à recharger » si <paramref name="low"/>).</summary>
    public void Notify(int number, string kind, int percent, bool low)
    {
        var ink = low ? Low : Colors.White;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Battery(percent, ink));
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
        var title = new TextBlock { FontSize = 22, Foreground = Brushes.White };
        title.Inlines.Add(new System.Windows.Documents.Run($"Manette {number}"));
        title.Inlines.Add(new System.Windows.Documents.Run($"  ·  {percent} %") { Foreground = new SolidColorBrush(ink), FontWeight = FontWeights.Bold });
        text.Children.Add(title);
        text.Children.Add(new TextBlock
        {
            Text = low ? $"{kind} · batterie faible, à recharger" : $"{kind} · batterie à moitié",
            FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 1, 0, 0),
        });
        row.Children.Add(text);
        _box.Child = row;
        _box.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, ink.R, ink.G, ink.B));

        if (!IsVisible) Show();
        Place();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        _hide.Stop();
        _hide.Start();
    }

    private static FrameworkElement Battery(int percent, Color ink)
    {
        var brush = new SolidColorBrush(ink);
        var body = new Border
        {
            Width = 40, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2), BorderBrush = brush,
            Padding = new Thickness(2.5), VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Child = new Border
            {
                CornerRadius = new CornerRadius(2), Background = brush, HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Width = Math.Max(3, 31 * Math.Clamp(percent, 0, 100) / 100.0),
            },
        };
        var tip = new Border { Width = 3, Height = 9, CornerRadius = new CornerRadius(0, 2, 2, 0), Background = brush, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center, Children = { body, tip } };
    }

    /// <summary>En haut à gauche de la TV, toujours au-dessus (un jeu peut repasser devant une fenêtre « topmost »).</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var margin = (int)(24 * dpi.DpiScaleX);
        SetWindowPos(hwnd, HwndTopmost, (int)_bounds.Left + margin, (int)_bounds.Top + margin, 0, 0, SwpNoActivate | SwpShowWindow | SwpNoSize);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
