using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Runtime;

namespace DualSenseUltimate.Core.Automation;

public enum RuleTrigger { BatteryBelow, ChargingStarted, ControllerConnected, ControllerDisconnected, ProcessStarted, ProcessExited, Notification }
public enum RuleActionType { SetLightbar, FlashLightbar, ClearOverrides, ApplyProfile, PlayHaptic, StopEffects, SetTriggerPreset }

public sealed class RuleAction
{
    public RuleActionType Type { get; set; }
    /// <summary>Colour (#RRGGBB), profile name, haptic pattern name or trigger preset name depending on the type.</summary>
    public string Value { get; set; } = "";
    /// <summary>For SetLightbar: 0 = until cleared.</summary>
    public int DurationMs { get; set; }
}

public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Nueva regla";
    public bool Enabled { get; set; } = true;
    public RuleTrigger Trigger { get; set; }
    /// <summary>Battery percentage for BatteryBelow.</summary>
    public int Threshold { get; set; } = 20;
    /// <summary>Executable name for process triggers.</summary>
    public string Process { get; set; } = "";
    /// <summary>Limit to one controller (null = all).</summary>
    public string? ControllerId { get; set; }
    public List<RuleAction> Actions { get; set; } = [];
    public int CooldownSeconds { get; set; } = 5;
}

public sealed class RuleSet { public List<AutomationRule> Rules { get; set; } = []; }

/// <summary>What rules are allowed to do. Implemented by the runtime.</summary>
public interface IAutomationTarget
{
    IReadOnlyList<string> ControllerIds { get; }
    void SetLightbarOverride(string controllerId, byte r, byte g, byte b, int durationMs);
    void ClearOverrides(string controllerId);
    bool ApplyProfileByName(string controllerId, string profileName);
    void PlayHapticByName(string controllerId, string patternName);
    void StopEffects(string controllerId);
    void SetTriggerPreset(string controllerId, string side, string presetName);
}

/// <summary>Evaluates "if event then actions" rules. Rules are data: they can only invoke <see cref="IAutomationTarget"/>.</summary>
public sealed class AutomationEngine
{
    private readonly IAutomationTarget _target;
    private readonly Dictionary<string, DateTime> _lastFired = new();
    private readonly HashSet<string> _batteryLatched = new(); // rule|controller pairs already fired
    private List<AutomationRule> _rules = [];
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    public event Action<AutomationRule, string?>? RuleFired;

    public AutomationEngine(IAutomationTarget target) => _target = target;

    public IReadOnlyList<AutomationRule> Rules { get => _rules; set => _rules = value.ToList(); }

    public void Handle(AppEvent e)
    {
        foreach (var rule in _rules)
        {
            if (!rule.Enabled) continue;
            string? controller = e.ControllerId;
            if (rule.ControllerId != null && controller != null && rule.ControllerId != controller) continue;
            bool match = rule.Trigger switch
            {
                RuleTrigger.BatteryBelow => e is BatteryChangedEvent b && BatteryMatch(rule, b),
                RuleTrigger.ChargingStarted => e is BatteryChangedEvent { Status: BatteryStatus.Charging, PreviousStatus: not BatteryStatus.Charging },
                RuleTrigger.ControllerConnected => e is ControllerConnectedEvent,
                RuleTrigger.ControllerDisconnected => e is ControllerDisconnectedEvent,
                RuleTrigger.ProcessStarted => e is ProcessStartedEvent p && SameProcess(p.Name, rule.Process),
                RuleTrigger.ProcessExited => e is ProcessExitedEvent p && SameProcess(p.Name, rule.Process),
                RuleTrigger.Notification => e is NotificationEvent,
                _ => false,
            };
            if (!match) continue;
            string key = rule.Id + "|" + controller;
            var now = Clock();
            if (_lastFired.TryGetValue(key, out var last) && (now - last).TotalSeconds < rule.CooldownSeconds) continue;
            _lastFired[key] = now;
            Execute(rule, controller);
        }
    }

    // Fires once when the level drops below the threshold; re-arms after charging or rising 5 points above it.
    private bool BatteryMatch(AutomationRule rule, BatteryChangedEvent b)
    {
        string key = rule.Id + "|" + b.ControllerId;
        bool below = b.Percent < rule.Threshold && b.Status == BatteryStatus.Discharging;
        if (!below)
        {
            if (b.Status != BatteryStatus.Discharging || b.Percent >= rule.Threshold + 5) _batteryLatched.Remove(key);
            return false;
        }
        return _batteryLatched.Add(key);
    }

    public static bool SameProcess(string a, string b)
    {
        static string N(string s) => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
        return a.Length > 0 && string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
    }

    private void Execute(AutomationRule rule, string? controllerId)
    {
        var targets = controllerId != null ? [controllerId] : (rule.ControllerId != null ? [rule.ControllerId] : _target.ControllerIds.ToArray());
        foreach (var id in targets)
        {
            foreach (var a in rule.Actions)
            {
                try
                {
                    switch (a.Type)
                    {
                        case RuleActionType.SetLightbar: { var c = Lighting.Rgb.Parse(a.Value); _target.SetLightbarOverride(id, c.R, c.G, c.B, Math.Max(0, a.DurationMs)); break; }
                        case RuleActionType.FlashLightbar: { var c = Lighting.Rgb.Parse(a.Value, new Lighting.Rgb(255, 255, 255)); _target.SetLightbarOverride(id, c.R, c.G, c.B, a.DurationMs > 0 ? a.DurationMs : 800); break; }
                        case RuleActionType.ClearOverrides: _target.ClearOverrides(id); break;
                        case RuleActionType.ApplyProfile: _target.ApplyProfileByName(id, a.Value); break;
                        case RuleActionType.PlayHaptic: _target.PlayHapticByName(id, a.Value); break;
                        case RuleActionType.StopEffects: _target.StopEffects(id); break;
                        case RuleActionType.SetTriggerPreset: _target.SetTriggerPreset(id, "both", a.Value); break;
                    }
                }
                catch (Exception ex) { Log.Error($"rule '{rule.Name}' action {a.Type} failed", ex); }
            }
        }
        try { RuleFired?.Invoke(rule, controllerId); } catch { }
    }

    public static List<AutomationRule> Defaults() =>
    [
        new() { Name = "Batería baja → LED rojo", Trigger = RuleTrigger.BatteryBelow, Threshold = 20, Enabled = false,
            Actions = [new RuleAction { Type = RuleActionType.SetLightbar, Value = "#FF2D20" }, new RuleAction { Type = RuleActionType.PlayHaptic, Value = "Doble clic" }] },
        new() { Name = "Notificación → patrón háptico", Trigger = RuleTrigger.Notification, Enabled = false,
            Actions = [new RuleAction { Type = RuleActionType.PlayHaptic, Value = "Notificación" }] },
        new() { Name = "Mando desconectado → detener efectos", Trigger = RuleTrigger.ControllerDisconnected, Enabled = false,
            Actions = [new RuleAction { Type = RuleActionType.StopEffects }] },
    ];
}
