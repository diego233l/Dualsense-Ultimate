using System.Text.Json;
using DualSenseUltimate.Core.Haptics;
using DualSenseUltimate.Core.Lighting;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.Core.Profiles;

public sealed class ValidationResult
{
    public bool Ok => Errors.Count == 0;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
}

/// <summary>Sanitises untrusted profiles and packages. Imported data can only select from fixed sets of
/// actions; anything unknown is dropped and every number is clamped, so a profile can never execute
/// code, start programs or leave the controller unusable.</summary>
public static class ProfileValidator
{
    public const int MaxImportBytes = 1024 * 1024;
    private const int MaxName = 120, MaxText = 2000, MaxMacros = 64, MaxSteps = 256, MaxBindingActions = 16;

    public static (SharePackage? Package, ValidationResult Result) ParsePackage(string json)
    {
        var r = new ValidationResult();
        if (json.Length > MaxImportBytes) { r.Errors.Add("El archivo supera el tamaño máximo (1 MB)."); return (null, r); }
        SharePackage? pkg;
        try { pkg = JsonFile.Deserialize<SharePackage>(json); }
        catch (JsonException ex) { r.Errors.Add("JSON no válido: " + ex.Message); return (null, r); }
        if (pkg == null || pkg.Format != SharePackage.FormatId) { r.Errors.Add("No es un paquete de DualSense Ultimate."); return (null, r); }
        if (pkg.SchemaVersion > Profile.CurrentSchema) { r.Errors.Add($"El paquete usa el formato v{pkg.SchemaVersion}; esta versión admite hasta v{Profile.CurrentSchema}. Actualiza la aplicación."); return (null, r); }
        if (!PackageKinds.All.Contains(pkg.Kind)) { r.Errors.Add($"Tipo de paquete desconocido: {pkg.Kind}"); return (null, r); }
        if (pkg.Payload.ValueKind != JsonValueKind.Object) { r.Errors.Add("El paquete no contiene datos."); return (null, r); }
        pkg.Name = Text(pkg.Name, MaxName); pkg.Author = Text(pkg.Author, MaxName); pkg.Description = Text(pkg.Description, MaxText);
        return (pkg, r);
    }

    public static T? Payload<T>(SharePackage pkg, ValidationResult r) where T : class
    {
        try { return pkg.Payload.Deserialize<T>(JsonFile.Options); }
        catch (JsonException ex) { r.Errors.Add("Contenido no válido: " + ex.Message); return null; }
    }

    private static string Text(string? s, int max)
    {
        s ??= "";
        s = new string(s.Where(c => !char.IsControl(c) || c == '\n').ToArray());
        return s.Length > max ? s[..max] : s;
    }

    public static ValidationResult Sanitize(Profile p)
    {
        var r = new ValidationResult();
        p.SchemaVersion = Profile.CurrentSchema;
        if (string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 64 || !p.Id.All(c => char.IsLetterOrDigit(c) || c == '-')) p.Id = Guid.NewGuid().ToString("N");
        p.Name = Text(p.Name, MaxName); if (p.Name.Length == 0) p.Name = "Perfil importado";
        p.Description = Text(p.Description, MaxText); p.Author = Text(p.Author, MaxName);
        p.Lighting ??= new(); p.Haptics ??= new(); p.Mapping ??= new(); p.Emulation ??= new();
        p.L2 = Sanitize(p.L2); p.R2 = Sanitize(p.R2);
        Sanitize(p.Lighting); Sanitize(p.Haptics); Sanitize(p.Mapping, r);
        p.Processes = (p.Processes ?? []).Select(x => Text((x ?? "").Split('/', '\\')[^1].Trim(), 100)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
        if (p.ControllerId is { Length: > 64 }) p.ControllerId = null;
        return r;
    }

    public static TriggerEffect Sanitize(TriggerEffect? e)
    {
        e ??= TriggerEffect.OffEffect;
        if (!Enum.IsDefined(e.Type)) e.Type = TriggerEffectType.Off;
        e.Name = Text(e.Name, MaxName);
        e.Start = Math.Clamp(e.Start, 0, 9); e.End = Math.Clamp(e.End, 0, 9);
        e.Strength = Math.Clamp(e.Strength, 0, 8); e.Strength2 = Math.Clamp(e.Strength2, 0, 8);
        e.Frequency = Math.Clamp(e.Frequency, 1, 255); e.Period = Math.Clamp(e.Period, 0, 255);
        e.FirstFoot = Math.Clamp(e.FirstFoot, 0, 6); e.SecondFoot = Math.Clamp(e.SecondFoot, 1, 7);
        var z = new int[10]; if (e.Zones != null) for (int i = 0; i < Math.Min(10, e.Zones.Length); i++) z[i] = Math.Clamp(e.Zones[i], 0, 8);
        e.Zones = z;
        var raw = new byte[11]; (e.RawBytes ?? []).AsSpan(0, Math.Min(11, e.RawBytes?.Length ?? 0)).CopyTo(raw);
        e.RawBytes = raw;
        return e;
    }

    public static void Sanitize(LightingConfig l)
    {
        if (!Enum.IsDefined(l.Mode)) l.Mode = LightingMode.Static;
        if (!Enum.IsDefined(l.PlayerLeds)) l.PlayerLeds = PlayerLedMode.Player1;
        if (!Enum.IsDefined(l.PlayerLedBrightness)) l.PlayerLedBrightness = LedBrightness.Medium;
        if (!Enum.IsDefined(l.MicLed)) l.MicLed = MicLedMode.Off;
        l.Color = Rgb.Parse(l.Color, new Rgb(10, 132, 255)).ToHex(); l.Color2 = Rgb.Parse(l.Color2).ToHex();
        l.NotificationColor = Rgb.Parse(l.NotificationColor, new Rgb(255, 255, 255)).ToHex();
        l.PeriodSeconds = double.IsFinite(l.PeriodSeconds) ? Math.Clamp(l.PeriodSeconds, 0.2, 120) : 4;
        l.Brightness = Math.Clamp(l.Brightness, 0, 100); l.PlayerLedMask &= 0x1F;
        l.Palette = (l.Palette ?? []).Take(16).Select(c => Rgb.Parse(c).ToHex()).ToList();
        l.Keyframes = (l.Keyframes ?? []).Take(32).Where(k => k != null && double.IsFinite(k.Time))
            .Select(k => new LightingKeyframe { Time = Math.Clamp(k.Time, 0, 1), Color = Rgb.Parse(k.Color).ToHex() }).ToList();
    }

    public static void Sanitize(HapticsConfig h)
    {
        h.Intensity = Math.Clamp(h.Intensity, 0, 100); h.AudioReactiveGain = Math.Clamp(h.AudioReactiveGain, 0, 400);
        h.AudioCutoffHz = Math.Clamp(h.AudioCutoffHz, 20, 2000);
        h.BassGainDb = Math.Clamp(h.BassGainDb, -24, 24); h.MidGainDb = Math.Clamp(h.MidGainDb, -24, 24); h.TrebleGainDb = Math.Clamp(h.TrebleGainDb, -24, 24);
    }

    public static HapticPattern Sanitize(HapticPattern p)
    {
        p.Name = Text(p.Name, MaxName); p.Repeat = Math.Clamp(p.Repeat, 0, 1000);
        p.Steps = (p.Steps ?? []).Take(MaxSteps).Where(s => s != null)
            .Select(s => new HapticStep { DurationMs = Math.Clamp(s.DurationMs, 1, 10000), Left = Math.Clamp(s.Left, 0, 255), Right = Math.Clamp(s.Right, 0, 255) }).ToList();
        return p;
    }

    private static List<MapAction> Actions(List<MapAction>? list, ValidationResult r) =>
        (list ?? []).Where(a => a != null).Take(MaxBindingActions).Where(a =>
        {
            bool ok = a.Type switch
            {
                ActionType.None => true,
                ActionType.PadButton => Enum.TryParse<PadButtons>(a.Target, out var b) && Enum.IsDefined(b),
                ActionType.Key => KeyNames.VirtualKeys.ContainsKey(a.Target ?? ""),
                ActionType.MouseButton => Enum.TryParse<MouseButtons>(a.Target, out var m) && Enum.IsDefined(m) && m != MouseButtons.None,
                ActionType.MouseWheel => a.Target is "Up" or "Down",
                ActionType.Macro => !string.IsNullOrEmpty(a.Target) && a.Target.Length <= MaxName,
                ActionType.Special => SpecialActions.All.Contains(a.Target),
                _ => false,
            };
            if (!ok) r.Warnings.Add($"Acción no válida descartada: {a.Type}:{Text(a.Target, 40)}");
            return ok;
        }).ToList();

    public static Macro Sanitize(Macro m, ValidationResult r)
    {
        m.Name = Text(m.Name, MaxName);
        m.Steps = (m.Steps ?? []).Take(MaxSteps).Where(s => s != null && Enum.IsDefined(s.Kind)).Select(s =>
        {
            var a = s.Action == null ? null : Actions([s.Action], r).FirstOrDefault();
            if (a?.Type == ActionType.Macro) a = null; // macros cannot call macros (no recursion)
            return new MacroStep { Kind = s.Kind, Action = a, Ms = Math.Clamp(s.Ms, 1, 60000) };
        }).Where(s => s.Kind == MacroStepKind.Delay || s.Action != null).ToList();
        return m;
    }

    public static void Sanitize(StickConfig s)
    {
        static double F(double v, double lo, double hi, double def) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : def;
        s.DeadZone = F(s.DeadZone, 0, 0.9, 0.06); s.AntiDeadZone = F(s.AntiDeadZone, 0, 0.9, 0); s.MaxZone = F(s.MaxZone, 0.2, 1.5, 1);
        s.CurveExponent = F(s.CurveExponent, 0.2, 5, 1.5); s.Sensitivity = F(s.Sensitivity, 0.1, 5, 1);
        s.CenterX = F(s.CenterX, -0.5, 0.5, 0); s.CenterY = F(s.CenterY, -0.5, 0.5, 0); s.MouseSpeed = F(s.MouseSpeed, 50, 10000, 1200);
        if (!Enum.IsDefined(s.Curve)) s.Curve = ResponseCurve.Linear;
        if (!Enum.IsDefined(s.Output)) s.Output = StickOutput.Stick;
        if (s.Keys == null || s.Keys.Length != 4 || s.Keys.Any(k => k == null || !KeyNames.VirtualKeys.ContainsKey(k))) s.Keys = ["W", "S", "A", "D"];
    }

    public static void Sanitize(GyroConfig g)
    {
        static double F(double v, double lo, double hi, double def) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : def;
        if (!Enum.IsDefined(g.Mode)) g.Mode = GyroMode.Off;
        g.SensitivityX = F(g.SensitivityX, 0.1, 100, 6); g.SensitivityY = F(g.SensitivityY, 0.1, 100, 6);
        g.DeadZone = F(g.DeadZone, 0, 50, 1); g.Smoothing = F(g.Smoothing, 0, 0.98, 0.3);
        g.BiasX = F(g.BiasX, -50, 50, 0); g.BiasY = F(g.BiasY, -50, 50, 0); g.BiasZ = F(g.BiasZ, -50, 50, 0);
        g.ShakeThresholdG = F(g.ShakeThresholdG, 0.3, 8, 1.5);
        if (g.ActivationButton != null && !Sources.IsValid(g.ActivationButton)) g.ActivationButton = null;
    }

    public static void Sanitize(MappingConfig m, ValidationResult r)
    {
        m.LeftStick ??= new(); m.RightStick ??= new(); m.L2 ??= new(); m.R2 ??= new(); m.Gyro ??= new(); m.Touchpad ??= new();
        Sanitize(m.LeftStick); Sanitize(m.RightStick); Sanitize(m.Gyro);
        foreach (var t in new[] { m.L2, m.R2 })
        {
            t.DeadZone = double.IsFinite(t.DeadZone) ? Math.Clamp(t.DeadZone, 0, 0.9) : 0;
            t.MaxZone = double.IsFinite(t.MaxZone) ? Math.Clamp(t.MaxZone, 0.1, 1) : 1;
        }
        if (!Enum.IsDefined(m.Touchpad.Mode)) m.Touchpad.Mode = TouchpadMode.Passthrough;
        m.Touchpad.Sensitivity = double.IsFinite(m.Touchpad.Sensitivity) ? Math.Clamp(m.Touchpad.Sensitivity, 0.05, 10) : 1;
        if (m.ShiftButton != null && !Sources.IsValid(m.ShiftButton)) m.ShiftButton = null;

        var bindings = new Dictionary<string, ButtonBinding>();
        foreach (var (key, b) in m.Bindings ?? new())
        {
            if (b == null || !Sources.IsValid(key)) { r.Warnings.Add($"Asignación descartada (origen desconocido): {Text(key, 40)}"); continue; }
            b.Press = Actions(b.Press, r);
            b.Hold = b.Hold == null ? null : Actions(b.Hold, r);
            b.DoubleTap = b.DoubleTap == null ? null : Actions(b.DoubleTap, r);
            b.Shift = b.Shift == null ? null : Actions(b.Shift, r);
            if (!Enum.IsDefined(b.Mode)) b.Mode = PressMode.Normal;
            b.TurboHz = double.IsFinite(b.TurboHz) ? Math.Clamp(b.TurboHz, 1, 30) : 10;
            b.HoldMs = Math.Clamp(b.HoldMs, 100, 5000); b.DoubleTapMs = Math.Clamp(b.DoubleTapMs, 80, 1000);
            bindings[key] = b;
        }
        m.Bindings = bindings;
        m.Chords = (m.Chords ?? []).Where(c => c?.Buttons != null).Take(32)
            .Select(c => new ChordBinding { Buttons = c.Buttons.Where(Sources.IsValid).Distinct().Take(4).ToList(), Actions = Actions(c.Actions, r) })
            .Where(c => c.Buttons.Count >= 2).ToList();
        m.Macros = (m.Macros ?? []).Where(x => x != null).Take(MaxMacros).Select(x => Sanitize(x, r)).ToList();

        // Safety net: never allow a profile that disables every way of using the controller AND the special
        // recovery action. If PS is remapped away with no ToggleMapping anywhere, keep a recovery chord.
        bool hasRecovery = m.Chords.Any(c => c.Actions.Any(a => a.Type == ActionType.Special && a.Target == SpecialActions.ToggleMapping));
        if (!hasRecovery && m.Bindings.Count > 0)
            m.Chords.Add(new ChordBinding
            {
                Buttons = [nameof(PadButtons.PS), nameof(PadButtons.Options), nameof(PadButtons.Create)],
                Actions = [new MapAction(ActionType.Special, SpecialActions.ToggleMapping)],
            });
    }
}
