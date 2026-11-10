using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DualSenseUltimate.Core.Lighting;

namespace DualSenseUltimate.App.Controls;

/// <summary>Small helpers for building UI in code and reading theme resources.</summary>
public static class Ui
{
    public static IBrush Brush(Visual v, string key) =>
        Application.Current!.TryGetResource(key, v.ActualThemeVariant, out var o) && o is IBrush b ? b : Brushes.Gray;

    public static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
    public static Rgb ToRgb(Color c) => new(c.R, c.G, c.B);

    public static TextBlock Text(string text, string? cls = null)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        if (cls != null) t.Classes.Add(cls);
        return t;
    }

    public static StackPanel Row(double spacing, params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static Button Btn(string text, Action onClick, bool accent = false)
    {
        var b = new Button { Content = text };
        if (accent) b.Classes.Add("accent");
        b.Click += (_, _) => { try { onClick(); } catch (Exception ex) { Core.Diagnostics.Log.Error("UI action failed", ex); } };
        return b;
    }

    public static ComboBox Combo<T>(IEnumerable<T> items, T? selected, Action<T> onChanged, double width = 160)
    {
        var c = new ComboBox { ItemsSource = items.ToList(), SelectedItem = selected, Width = width };
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is T v) onChanged(v); };
        return c;
    }

    public static string Duration(double minutes) => minutes >= 60 ? $"{(int)(minutes / 60)} h {(int)(minutes % 60):00} min" : $"{(int)minutes} min";

    public static string BatteryText(Core.Protocol.InputState s) => s.BatteryStatus switch
    {
        Core.Protocol.BatteryStatus.Charging => $"{s.BatteryPercent} % · cargando",
        Core.Protocol.BatteryStatus.Full => "100 % · carga completa",
        Core.Protocol.BatteryStatus.Error => "error de batería",
        Core.Protocol.BatteryStatus.Unknown => "desconocida",
        _ => $"{s.BatteryPercent} %",
    };
}
