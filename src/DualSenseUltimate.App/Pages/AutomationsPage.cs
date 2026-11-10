using Avalonia.Controls;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Automation;

namespace DualSenseUltimate.App.Pages;

public sealed class AutomationsPage : PageBase
{
    public override string Title => "Automatizaciones";
    public override string Subtitle => "Reglas «si ocurre esto, haz aquello»";

    private static string TriggerName(RuleTrigger t) => t switch
    {
        RuleTrigger.BatteryBelow => "La batería baja de…", RuleTrigger.ChargingStarted => "El mando empieza a cargar", RuleTrigger.ControllerConnected => "Se conecta un mando",
        RuleTrigger.ControllerDisconnected => "Se desconecta un mando", RuleTrigger.ProcessStarted => "Se inicia una aplicación", RuleTrigger.ProcessExited => "Se cierra una aplicación",
        _ => "Llega una notificación",
    };

    private static string ActionName(RuleActionType t) => t switch
    {
        RuleActionType.SetLightbar => "Cambiar color de la barra", RuleActionType.FlashLightbar => "Destello de color", RuleActionType.ClearOverrides => "Restaurar luces y gatillos del perfil",
        RuleActionType.ApplyProfile => "Aplicar perfil", RuleActionType.PlayHaptic => "Reproducir patrón háptico", RuleActionType.StopEffects => "Detener efectos",
        _ => "Aplicar preset de gatillos",
    };

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var rules = rt.Automation.Rules.ToList();
        void Save() { rt.Automation.Rules = rules; rt.SaveRules(); }
        var root = new StackPanel();
        root.Children.Add(Card(null,
            Ui.Text("Las reglas se evalúan en segundo plano aunque la ventana esté cerrada. Solo pueden ejecutar las acciones de esta lista: no lanzan programas ni scripts.", "muted"),
            new Border { Height = 8 },
            Ui.Row(10, Ui.Btn("+ Nueva regla", () =>
                {
                    rules.Add(new AutomationRule { Actions = [new RuleAction { Type = RuleActionType.SetLightbar, Value = "#FF2D20" }] });
                    Save(); Rebuild();
                }, true))));

        foreach (var r in rules.ToList())
        {
            var rule = r;
            var name = new TextBox { Text = rule.Name, Width = 340, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontWeight = Avalonia.Media.FontWeight.SemiBold };
            name.LostFocus += (_, _) => { rule.Name = name.Text ?? ""; Save(); };
            var conditions = new WrapPanel();
            conditions.Children.Add(EnumCombo("Cuando…", rule.Trigger, v => { rule.Trigger = v; Save(); Rebuild(); }, TriggerName, 250));
            if (rule.Trigger == RuleTrigger.BatteryBelow)
            { conditions.Children.Add(new Border { Width = 14 }); conditions.Children.Add(SliderRow("Umbral", 5, 95, rule.Threshold, v => { rule.Threshold = (int)v; Save(); }, "0' %'", 5)); }
            if (rule.Trigger is RuleTrigger.ProcessStarted or RuleTrigger.ProcessExited)
            {
                var proc = new TextBox { Text = rule.Process, Watermark = "juego.exe", Width = 220 };
                proc.LostFocus += (_, _) => { rule.Process = proc.Text?.Trim() ?? ""; Save(); };
                conditions.Children.Add(new Border { Width = 14 }); conditions.Children.Add(Labeled("Ejecutable", proc));
            }
            var sessions = rt.Sessions;
            var scope = new ComboBox { Width = 230, ItemsSource = new[] { "Todos los mandos" }.Concat(sessions.Select(s => rt.DisplayName(s) + " · " + s.Id)).ToList(),
                SelectedIndex = rule.ControllerId == null ? 0 : Math.Max(0, sessions.ToList().FindIndex(s => s.Id == rule.ControllerId) + 1) };
            scope.SelectionChanged += (_, _) => { rule.ControllerId = scope.SelectedIndex <= 0 ? null : sessions[scope.SelectedIndex - 1].Id; Save(); };
            conditions.Children.Add(new Border { Width = 14 }); conditions.Children.Add(Labeled("Mando", scope));

            var actions = new StackPanel { Spacing = 6 };
            foreach (var a in rule.Actions.ToList())
            {
                var action = a;
                var types = Enum.GetValues<RuleActionType>();
                var type = new ComboBox { ItemsSource = types.Select(ActionName).ToList(), SelectedIndex = Array.IndexOf(types, action.Type), Width = 270 };
                type.SelectionChanged += (_, _) =>
                {
                    action.Type = types[type.SelectedIndex];
                    action.Value = action.Type switch
                    {
                        RuleActionType.SetLightbar or RuleActionType.FlashLightbar => "#FF2D20", RuleActionType.ApplyProfile => rt.Profiles.Profiles[0].Name,
                        RuleActionType.PlayHaptic => "Notificación", RuleActionType.SetTriggerPreset => "Resistencia media", _ => "",
                    };
                    Save(); Rebuild();
                };
                var row = Ui.Row(8, type);
                IEnumerable<string>? options = action.Type switch
                {
                    RuleActionType.ApplyProfile => rt.Profiles.Profiles.Select(p => p.Name),
                    RuleActionType.PlayHaptic => rt.Profiles.AllHapticPatterns.Select(p => p.Name),
                    RuleActionType.SetTriggerPreset => rt.Profiles.AllTriggerEffects.Select(p => p.Name),
                    _ => null,
                };
                if (options != null) row.Children.Add(Ui.Combo(options, action.Value, v => { action.Value = v; Save(); }, 220));
                else if (action.Type is RuleActionType.SetLightbar or RuleActionType.FlashLightbar)
                {
                    var picker = new ColorPicker { Color = Ui.ToColor(Core.Lighting.Rgb.Parse(action.Value, new Core.Lighting.Rgb(255, 45, 32))), IsAlphaEnabled = false, IsAlphaVisible = false, Width = 90, Height = 32 };
                    picker.ColorChanged += (_, e) => { action.Value = Ui.ToRgb(e.NewColor).ToHex(); Save(); };
                    row.Children.Add(picker);
                }
                row.Children.Add(Ui.Btn("✕", () => { rule.Actions.Remove(action); Save(); Rebuild(); }));
                actions.Children.Add(row);
            }

            var header = new DockPanel();
            var enabled = Toggle("", rule.Enabled, v => { rule.Enabled = v; Save(); });
            DockPanel.SetDock(enabled, Dock.Right);
            header.Children.Add(enabled); header.Children.Add(name);
            root.Children.Add(Card(null, header, conditions, Ui.Text("Entonces…", "label"), actions, new Border { Height = 6 },
                Ui.Row(8, Ui.Btn("+ Acción", () => { if (rule.Actions.Count < 8) { rule.Actions.Add(new RuleAction { Type = RuleActionType.PlayHaptic, Value = "Notificación" }); Save(); Rebuild(); } }),
                    Ui.Btn("Eliminar regla", () => { rules.Remove(rule); Save(); Rebuild(); }))));
        }
        return root;
    }
}
