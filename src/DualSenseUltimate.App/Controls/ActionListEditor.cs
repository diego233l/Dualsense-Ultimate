using Avalonia.Controls;
using Avalonia.Layout;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.App.Controls;

/// <summary>Editor for a list of <see cref="MapAction"/>: each row is a type and a target picked from fixed lists.</summary>
public sealed class ActionListEditor : StackPanel
{
    private readonly List<MapAction> _actions;
    private readonly Func<IEnumerable<string>> _macroNames;
    private readonly Action _changed;

    private static readonly Dictionary<ActionType, string> TypeNames = new()
    {
        [ActionType.PadButton] = "Botón de mando", [ActionType.Key] = "Tecla", [ActionType.MouseButton] = "Botón de ratón",
        [ActionType.MouseWheel] = "Rueda", [ActionType.Macro] = "Macro", [ActionType.Special] = "Acción especial",
    };

    public ActionListEditor(List<MapAction> actions, Func<IEnumerable<string>> macroNames, Action changed)
    {
        _actions = actions; _macroNames = macroNames; _changed = changed;
        Spacing = 6;
        Rebuild();
    }

    private IEnumerable<string> Targets(ActionType t) => t switch
    {
        ActionType.PadButton => Sources.Buttons.Where(b => (b & Sources.PassThrough) != 0).Select(b => b.ToString()),
        ActionType.Key => KeyNames.VirtualKeys.Keys.OrderBy(k => k.Length > 1).ThenBy(k => k),
        ActionType.MouseButton => ["Left", "Right", "Middle", "X1", "X2"],
        ActionType.MouseWheel => ["Up", "Down"],
        ActionType.Macro => _macroNames(),
        ActionType.Special => SpecialActions.All,
        _ => [],
    };

    private void Rebuild()
    {
        Children.Clear();
        foreach (var action in _actions.ToList())
        {
            var a = action;
            var target = new ComboBox { Width = 170, ItemsSource = Targets(a.Type).ToList(), SelectedItem = a.Target };
            target.SelectionChanged += (_, _) => { if (target.SelectedItem is string s) { a.Target = s; _changed(); } };
            var type = new ComboBox { Width = 150, ItemsSource = TypeNames.Values.ToList(), SelectedItem = TypeNames.GetValueOrDefault(a.Type) };
            type.SelectionChanged += (_, _) =>
            {
                var t = TypeNames.FirstOrDefault(kv => kv.Value == type.SelectedItem as string).Key;
                if (t == a.Type) return;
                a.Type = t; a.Target = Targets(t).FirstOrDefault() ?? "";
                _changed(); Rebuild();
            };
            Children.Add(Ui.Row(6, type, target, Ui.Btn("✕", () => { _actions.Remove(a); _changed(); Rebuild(); })));
        }
        if (_actions.Count == 0) Children.Add(Ui.Text("Sin acciones (el botón queda desactivado)", "muted"));
        if (_actions.Count < 8)
        {
            var add = Ui.Btn("+ Añadir acción", () => { _actions.Add(new MapAction(ActionType.Key, "Space")); _changed(); Rebuild(); });
            add.HorizontalAlignment = HorizontalAlignment.Left;
            Children.Add(add);
        }
    }
}
