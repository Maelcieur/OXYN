using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Oxyn.Controls;
using Oxyn.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Oxyn;

// Personnaliser › Températures : courbes (donnée, libellé, couleur), autant que l'on veut, et apparence du graphe
// (épaisseur, remplissage, échelle). Réglages communs au bureau et à la Switch.
public partial class SettingsWindow
{
    private static readonly string[] CurveColors =
        { "#FF00E0FF", "#FFFF5A36", "#FFFFC107", "#FF7CFC00", "#FFB388FF", "#FFFF4FA3", "#FF4FC3F7", "#FFFFFFFF" };

    private void BuildTemperatureEditor()
    {
        var settings = _widgetManager.Config.WidgetExtras;
        var host = TemperatureSeriesHost;
        host.Children.Clear();

        // Première modification : la liste part des courbes d'origine (processeur, carte graphique).
        List<TemperatureSeries> Series()
        {
            if (settings.TemperatureSeries.Count == 0)
                settings.TemperatureSeries = settings.EffectiveTemperatureSeries().Select(s => new TemperatureSeries
                    { Source = s.Source, Label = s.Label, Color = s.Color }).ToList();
            return settings.TemperatureSeries;
        }
        void Changed(bool rebuild = false)
        {
            settings.NotifySeriesChanged();
            ScheduleSave();
            if (rebuild) BuildTemperatureEditor();
        }

        var temperatures = _widgetManager.DesignValues().Values.Where(r => r.Unit == "°C")
            .OrderBy(r => r.Group, StringComparer.CurrentCulture).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList();

        var current = settings.EffectiveTemperatureSeries();
        for (var i = 0; i < current.Count; i++)
        {
            var index = i;
            var serie = current[i];

            var source = new ComboBox { Width = 300, Margin = new Thickness(0, 0, 8, 0) };
            if (temperatures.All(r => r.Id != serie.Source))
                source.Items.Add(new ComboBoxItem { Content = $"{serie.Source} (pas encore lue)", Tag = serie.Source });
            foreach (var r in temperatures)
                source.Items.Add(new ComboBoxItem { Content = $"{r.Group} · {r.Name}", Tag = r.Id });
            source.SelectedItem = source.Items.OfType<ComboBoxItem>().FirstOrDefault(it => (string)it.Tag == serie.Source);
            source.SelectionChanged += (_, _) =>
            {
                if (source.SelectedItem is ComboBoxItem { Tag: string id }) { Series()[index].Source = id; Changed(); }
            };

            var label = new TextBox { Text = serie.Label, Width = 90, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Libellé dans la légende" };
            label.TextChanged += (_, _) => { Series()[index].Label = label.Text; Changed(); };

            var color = new ColorSwatch { Hex = serie.Color, Content = "", Margin = new Thickness(0, 0, 8, 0) };
            color.SetResourceReference(StyleProperty, "ColorDot");
            DependencyPropertyDescriptor.FromProperty(ColorSwatch.HexProperty, typeof(ColorSwatch))
                .AddValueChanged(color, (_, _) => { Series()[index].Color = color.Hex; Changed(); });

            var remove = new Button { Content = "Retirer", Style = (Style)FindResource("LinkButton"), IsEnabled = current.Count > 1 };
            remove.Click += (_, _) => { Series().RemoveAt(index); Changed(rebuild: true); };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(color);
            row.Children.Add(source);
            row.Children.Add(label);
            row.Children.Add(remove);
            host.Children.Add(row);
        }

        var add = new Button { Content = "+ Ajouter une température", Style = (Style)FindResource("LinkButton"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 10) };
        add.Click += (_, _) =>
        {
            var list = Series();
            var next = temperatures.FirstOrDefault(r => list.All(s => s.Source != r.Id));
            list.Add(new TemperatureSeries
            {
                Source = next?.Id ?? SensorCatalog.CpuTemp,
                Label = next is null ? "Temp." : ShortLabel(next.Name),
                Color = CurveColors[list.Count % CurveColors.Length]
            });
            Changed(rebuild: true);
        };
        host.Children.Add(add);

        // Apparence du graphe
        host.Children.Add(new SettingRow { Label = "Épaisseur des courbes",
            Content = TempSlider(0.5, 6, settings.TempLineThickness, v => settings.TempLineThickness = v, "0.0 px", 0.5) });
        var fill = new CheckBox { IsChecked = settings.TempFill, Margin = new Thickness(0) };
        fill.SetResourceReference(StyleProperty, "ToggleSwitch");
        fill.Checked += (_, _) => { settings.TempFill = true; ScheduleSave(); };
        fill.Unchecked += (_, _) => { settings.TempFill = false; ScheduleSave(); };
        host.Children.Add(new SettingRow { Label = "Dégradé sous les courbes", Content = fill });
        host.Children.Add(new SettingRow { Label = "Bas de l'échelle",
            Content = TempSlider(0, 80, settings.TempMin, v => settings.TempMin = v, "0 °C", 5) });
        host.Children.Add(new SettingRow { Label = "Haut de l'échelle",
            Content = TempSlider(40, 130, settings.TempMax, v => settings.TempMax = v, "0 °C", 5) });
    }

    /// <summary>Libellé court pour la légende (« Température · CCD1 (Tdie) » -> « CCD1 (Tdie) »).</summary>
    private static string ShortLabel(string name)
    {
        var i = name.LastIndexOf('·');
        var label = (i >= 0 ? name[(i + 1)..] : name).Trim();
        return label.Length > 14 ? label[..14] : label;
    }

    private FrameworkElement TempSlider(double min, double max, double value, Action<double> set, string format, double tick)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), IsSnapToTickEnabled = true, TickFrequency = tick };
        var text = new TextBlock { Style = (Style)FindResource("FieldValue"), Text = slider.Value.ToString(format, Fr) };
        slider.ValueChanged += (_, e) =>
        {
            text.Text = e.NewValue.ToString(format, Fr);
            set(e.NewValue);
            ScheduleSave();
        };
        var dock = new DockPanel { Width = 260 };
        DockPanel.SetDock(text, Dock.Right);
        dock.Children.Add(text);
        dock.Children.Add(slider);
        return dock;
    }
}
