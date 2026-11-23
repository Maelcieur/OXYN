using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Oxyn.Services;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Oxyn.Widgets;

/// <summary>Qualité de l'air : indice européen dans un anneau coloré selon l'échelle officielle, niveau, polluants principaux.</summary>
public sealed class AirQualityWidgetContent : UserControl
{
    private readonly TextBlock _place, _aqi, _level, _message;
    private readonly Ellipse _ring = new() { Width = 78, Height = 78, StrokeThickness = 6 };
    private readonly TextBlock[] _values = new TextBlock[4];
    private readonly FrameworkElement _body;

    public AirQualityWidgetContent()
    {
        Width = 270;
        _place = WidgetUi.Caption("QUALITÉ DE L'AIR", "title");
        _aqi = WidgetUi.Text("aqi", 28, number: true);
        _aqi.HorizontalAlignment = HorizontalAlignment.Center;
        _aqi.VerticalAlignment = VerticalAlignment.Center;
        _level = WidgetUi.Text("level", 16, weight: FontWeights.SemiBold);
        var scale = WidgetUi.Text("level", 11, sub: true);
        scale.Text = "Indice européen (0 = excellent)";
        _message = WidgetUi.Text("message", 12, sub: true);
        _message.TextWrapping = TextWrapping.Wrap;
        _message.Margin = new Thickness(0, 8, 0, 0);

        var now = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var gauge = new Grid { Children = { _ring, _aqi } };
        DockPanel.SetDock(gauge, Dock.Left);
        now.Children.Add(gauge);
        now.Children.Add(new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { _level, scale } });

        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
        var names = new[] { "PM2.5", "PM10", "NO₂", "O₃" };
        for (var i = 0; i < 4; i++)
        {
            var name = WidgetUi.Text("pollutant", 10.5, sub: true, weight: FontWeights.SemiBold);
            name.Text = names[i];
            name.HorizontalAlignment = HorizontalAlignment.Center;
            _values[i] = WidgetUi.Text("pollutant", 14, number: true);
            _values[i].HorizontalAlignment = HorizontalAlignment.Center;
            var unit = WidgetUi.Text("pollutant", 9, sub: true);
            unit.Text = "µg/m³";
            unit.HorizontalAlignment = HorizontalAlignment.Center;
            grid.Children.Add(new StackPanel { Children = { name, _values[i], unit } });
        }
        _body = new StackPanel { Children = { now, grid } };
        Content = new StackPanel { Children = { _place, _body, _message } };
        Update(null, "Recherche de la qualité de l'air…");
    }

    public void Update(AirQualitySnapshot? air, string? error)
    {
        _message.Text = air is null ? error ?? "Qualité de l'air indisponible pour cette ville." : "";
        _message.Visibility = air is null ? Visibility.Visible : Visibility.Collapsed;
        _body.Visibility = air is null ? Visibility.Collapsed : Visibility.Visible;
        if (air is null) return;

        _place.Text = string.IsNullOrEmpty(air.Place) ? "QUALITÉ DE L'AIR" : "AIR · " + air.Place.ToUpper(WidgetUi.Fr);
        var (label, color) = WeatherService.AirLevel(air.Aqi);
        _aqi.Text = air.Aqi.ToString(WidgetUi.Fr);
        _level.Text = label;
        _ring.Stroke = WidgetUi.Brush("#FF" + color[1..], Colors.Gray);
        double?[] values = { air.Pm25, air.Pm10, air.No2, air.O3 };
        for (var i = 0; i < 4; i++) _values[i].Text = values[i] is { } v ? Math.Round(v).ToString(WidgetUi.Fr) : "--";
    }
}
