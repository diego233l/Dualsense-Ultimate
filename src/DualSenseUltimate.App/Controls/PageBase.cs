using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DualSenseUltimate.App.Controls;

/// <summary>Base class for pages. A page builds its content in code from the profile being edited and is
/// simply rebuilt when another profile (or controller) becomes active.</summary>
public abstract class PageBase : UserControl
{
    private readonly List<Action> _live = [], _content = [];
    private bool _attached;
    protected virtual bool RebuildOnControllers => true;
    public abstract string Title { get; }
    public virtual string Subtitle => "";

    protected abstract Control Build();

    /// <summary>Registers a callback run ~30 times per second while the page is visible.</summary>
    protected void Live(Action a) => _live.Add(a);
    /// <summary>Registers a callback run when the edited profile's content changes.</summary>
    protected void OnContent(Action a) => _content.Add(a);

    public void Rebuild()
    {
        _live.Clear(); _content.Clear();
        try { Content = Build(); }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error($"page {GetType().Name} failed to build", ex);
            Content = Ui.Text("No se pudo mostrar esta sección: " + ex.Message, "muted");
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_attached) return;
        _attached = true;
        AppState.EditProfileChanged += Rebuild;
        AppState.ControllersChanged += OnControllers;
        AppState.LiveTick += RunLive;
        AppState.ProfileContentChanged += RunContent;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        AppState.EditProfileChanged -= Rebuild;
        AppState.ControllersChanged -= OnControllers;
        AppState.LiveTick -= RunLive;
        AppState.ProfileContentChanged -= RunContent;
    }

    private void OnControllers() { if (RebuildOnControllers) Rebuild(); }
    private void RunLive() { foreach (var a in _live.ToArray()) { try { a(); } catch { } } }
    private void RunContent() { foreach (var a in _content.ToArray()) { try { a(); } catch { } } }

    // ---- building blocks

    protected static Border Card(string? title, params Control[] children)
    {
        var sp = new StackPanel { Spacing = 4 };
        if (title != null) sp.Children.Add(Ui.Text(title, "h2"));
        foreach (var c in children) sp.Children.Add(c);
        var b = new Border { Child = sp };
        b.Classes.Add("card");
        return b;
    }

    protected static Border Notice(string text)
    {
        var b = new Border { Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 } };
        b.Classes.Add("notice");
        return b;
    }

    protected static WrapPanel Wrap(params Control[] children)
    {
        var w = new WrapPanel();
        foreach (var c in children) w.Children.Add(c);
        return w;
    }

    protected static StackPanel Stack(params Control[] children)
    {
        var s = new StackPanel();
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    protected static Control Labeled(string label, Control c) => Stack(Ui.Text(label, "label"), c);

    protected static Control SliderRow(string label, double min, double max, double value, Action<double> set, string format = "0", double step = 1)
    {
        var val = Ui.Text(value.ToString(format), "muted"); val.Width = 52; val.TextAlignment = TextAlignment.Right;
        var s = new Slider { Minimum = min, Maximum = max, Value = value, Width = 210, TickFrequency = step, IsSnapToTickEnabled = true };
        s.ValueChanged += (_, e) => { set(e.NewValue); val.Text = e.NewValue.ToString(format); };
        return Stack(Ui.Text(label, "label"), Ui.Row(8, s, val));
    }

    protected static ToggleSwitch Toggle(string text, bool value, Action<bool> set)
    {
        var t = new ToggleSwitch { Content = text, IsChecked = value, OnContent = "Sí", OffContent = "No" };
        t.IsCheckedChanged += (_, _) => set(t.IsChecked == true);
        return t;
    }

    protected static Control EnumCombo<T>(string label, T value, Action<T> set, Func<T, string>? name = null, double width = 200) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var names = values.Select(v => name?.Invoke(v) ?? v.ToString()).ToList();
        var c = new ComboBox { ItemsSource = names, SelectedIndex = Array.IndexOf(values, value), Width = width };
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) set(values[c.SelectedIndex]); };
        return Labeled(label, c);
    }

    protected static Control NoController() =>
        Notice("No hay ningún mando conectado. Puedes editar el perfil igualmente; los cambios se aplicarán cuando conectes un mando.");
}
