using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Mapping;

public enum ActionType { None, PadButton, Key, MouseButton, MouseWheel, Macro, Special }

/// <summary>One output action. Target is a name: a <see cref="PadButtons"/> member, a key from
/// <see cref="KeyNames"/>, a mouse button (Left/Right/Middle/X1/X2), a wheel direction (Up/Down),
/// a macro name or a special action from <see cref="SpecialActions"/>. Data only - never code.</summary>
public sealed class MapAction
{
    public ActionType Type { get; set; }
    public string Target { get; set; } = "";
    public MapAction() { }
    public MapAction(ActionType type, string target) { Type = type; Target = target; }
    public MapAction Clone() => new(Type, Target);
    public override string ToString() => Type == ActionType.None ? "Desactivado" : $"{Type}:{Target}";
}

public static class SpecialActions
{
    public const string ProfileNext = "ProfileNext", ProfilePrevious = "ProfilePrevious", ToggleGyro = "ToggleGyro",
        BatteryFlash = "BatteryFlash", ToggleMapping = "ToggleMapping", StopEffects = "StopEffects";
    public static readonly string[] All = [ProfileNext, ProfilePrevious, ToggleGyro, BatteryFlash, ToggleMapping, StopEffects];
}

public enum PressMode { Normal, Toggle, Turbo }

public sealed class ButtonBinding
{
    /// <summary>Actions while pressed (or on a short press when Hold/DoubleTap are defined).</summary>
    public List<MapAction> Press { get; set; } = [];
    /// <summary>Actions held after a long press.</summary>
    public List<MapAction>? Hold { get; set; }
    /// <summary>Actions tapped on a double press.</summary>
    public List<MapAction>? DoubleTap { get; set; }
    /// <summary>Actions used instead of Press while the shift (modifier) button is held.</summary>
    public List<MapAction>? Shift { get; set; }
    public PressMode Mode { get; set; } = PressMode.Normal;
    public double TurboHz { get; set; } = 10;
    public int HoldMs { get; set; } = 400;
    public int DoubleTapMs { get; set; } = 250;

    public ButtonBinding Clone() => new()
    {
        Press = Press.Select(a => a.Clone()).ToList(), Hold = Hold?.Select(a => a.Clone()).ToList(),
        DoubleTap = DoubleTap?.Select(a => a.Clone()).ToList(), Shift = Shift?.Select(a => a.Clone()).ToList(),
        Mode = Mode, TurboHz = TurboHz, HoldMs = HoldMs, DoubleTapMs = DoubleTapMs,
    };
}

/// <summary>Several buttons pressed together trigger the actions; their individual bindings are suppressed.</summary>
public sealed class ChordBinding
{
    public List<string> Buttons { get; set; } = [];
    public List<MapAction> Actions { get; set; } = [];
}

public enum MacroStepKind { Down, Up, Tap, Delay }

public sealed class MacroStep
{
    public MacroStepKind Kind { get; set; }
    public MapAction? Action { get; set; }
    /// <summary>Delay length, or tap duration.</summary>
    public int Ms { get; set; } = 50;
}

public sealed class Macro
{
    public string Name { get; set; } = "";
    public List<MacroStep> Steps { get; set; } = [];
    /// <summary>Restart while the trigger is still held.</summary>
    public bool RepeatWhileHeld { get; set; }
}

public enum ResponseCurve { Linear, Quadratic, Cubic, EaseOut, Custom }
public enum StickOutput { Stick, Mouse, Keys, Disabled }

public sealed class StickConfig
{
    public double DeadZone { get; set; } = 0.06;
    public double AntiDeadZone { get; set; }
    /// <summary>Physical deflection treated as 100 % (saturation).</summary>
    public double MaxZone { get; set; } = 1.0;
    public ResponseCurve Curve { get; set; } = ResponseCurve.Linear;
    public double CurveExponent { get; set; } = 1.5;
    public double Sensitivity { get; set; } = 1.0;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    /// <summary>Resting position offset (-1..1) measured by calibration; removes drift.</summary>
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public StickOutput Output { get; set; } = StickOutput.Stick;
    /// <summary>Pixels per second at full deflection in Mouse mode.</summary>
    public double MouseSpeed { get; set; } = 1200;
    /// <summary>Keys for Up, Down, Left, Right in Keys mode.</summary>
    public string[] Keys { get; set; } = ["W", "S", "A", "D"];
    public StickConfig Clone() { var c = (StickConfig)MemberwiseClone(); c.Keys = (string[])Keys.Clone(); return c; }
}

public sealed class TriggerAxisConfig
{
    public double DeadZone { get; set; }
    public double MaxZone { get; set; } = 1.0;
    public TriggerAxisConfig Clone() => (TriggerAxisConfig)MemberwiseClone();
}

public enum GyroMode { Off, Mouse, LeftStick, RightStick }

public sealed class GyroConfig
{
    public GyroMode Mode { get; set; } = GyroMode.Off;
    /// <summary>Mouse: pixels per degree. Stick: 1.0 = full deflection at 180 deg/s.</summary>
    public double SensitivityX { get; set; } = 6;
    public double SensitivityY { get; set; } = 6;
    /// <summary>Angular speed (deg/s) below which motion is ignored.</summary>
    public double DeadZone { get; set; } = 1.0;
    /// <summary>0 = raw, 0.95 = heavy smoothing (exponential moving average).</summary>
    public double Smoothing { get; set; } = 0.3;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    /// <summary>Use roll instead of yaw for the horizontal axis (controller held flat vs. upright).</summary>
    public bool UseRollForX { get; set; }
    /// <summary>Button that must be held to enable gyro (null = always on).</summary>
    public string? ActivationButton { get; set; }
    /// <summary>When true the activation button disables gyro while held instead of enabling it.</summary>
    public bool ActivationDisables { get; set; }
    /// <summary>User measured resting bias in deg/s (pitch, yaw, roll).</summary>
    public double BiasX { get; set; }
    public double BiasY { get; set; }
    public double BiasZ { get; set; }
    /// <summary>Raise the "GyroShake" source when the controller is shaken.</summary>
    public bool ShakeGesture { get; set; }
    /// <summary>Acceleration (g) above rest that counts as a shake.</summary>
    public double ShakeThresholdG { get; set; } = 1.5;
    public GyroConfig Clone() => (GyroConfig)MemberwiseClone();
}

public enum TouchpadMode { Passthrough, Mouse, TwoRegions, FourRegions, Disabled }

public sealed class TouchpadConfig
{
    public TouchpadMode Mode { get; set; } = TouchpadMode.Passthrough;
    public double Sensitivity { get; set; } = 1.0;
    public bool TapToClick { get; set; } = true;
    public bool TwoFingerScroll { get; set; } = true;
    /// <summary>Raise SwipeUp/Down/Left/Right sources.</summary>
    public bool SwipeGestures { get; set; }
    public TouchpadConfig Clone() => (TouchpadConfig)MemberwiseClone();
}

/// <summary>Complete input mapping. Sources without a binding pass through unchanged.</summary>
public sealed class MappingConfig
{
    /// <summary>Key = source name (see <see cref="Sources"/>).</summary>
    public Dictionary<string, ButtonBinding> Bindings { get; set; } = new();
    public List<ChordBinding> Chords { get; set; } = [];
    /// <summary>Button acting as a modifier that selects the Shift actions.</summary>
    public string? ShiftButton { get; set; }
    public StickConfig LeftStick { get; set; } = new();
    public StickConfig RightStick { get; set; } = new();
    public TriggerAxisConfig L2 { get; set; } = new();
    public TriggerAxisConfig R2 { get; set; } = new();
    public GyroConfig Gyro { get; set; } = new();
    public TouchpadConfig Touchpad { get; set; } = new();
    public List<Macro> Macros { get; set; } = [];
    public bool SwapSticks { get; set; }
}

/// <summary>Names of every remappable source.</summary>
public static class Sources
{
    public const string TouchLeft = "TouchLeft", TouchRight = "TouchRight", TouchTopLeft = "TouchTopLeft", TouchTopRight = "TouchTopRight",
        TouchBottomLeft = "TouchBottomLeft", TouchBottomRight = "TouchBottomRight", SwipeUp = "SwipeUp", SwipeDown = "SwipeDown",
        SwipeLeft = "SwipeLeft", SwipeRight = "SwipeRight", GyroShake = "GyroShake";

    public static readonly PadButtons[] Buttons =
    [
        PadButtons.Cross, PadButtons.Circle, PadButtons.Square, PadButtons.Triangle, PadButtons.L1, PadButtons.R1, PadButtons.L2, PadButtons.R2,
        PadButtons.Create, PadButtons.Options, PadButtons.L3, PadButtons.R3, PadButtons.PS, PadButtons.Touchpad, PadButtons.Mute,
        PadButtons.DpadUp, PadButtons.DpadDown, PadButtons.DpadLeft, PadButtons.DpadRight,
        PadButtons.FnLeft, PadButtons.FnRight, PadButtons.PaddleLeft, PadButtons.PaddleRight,
    ];

    /// <summary>Buttons that exist on a virtual pad and therefore pass through by default.</summary>
    public const PadButtons PassThrough = PadButtons.Cross | PadButtons.Circle | PadButtons.Square | PadButtons.Triangle | PadButtons.L1 | PadButtons.R1 |
        PadButtons.L2 | PadButtons.R2 | PadButtons.Create | PadButtons.Options | PadButtons.L3 | PadButtons.R3 | PadButtons.PS | PadButtons.Touchpad |
        PadButtons.DpadUp | PadButtons.DpadDown | PadButtons.DpadLeft | PadButtons.DpadRight;

    public static readonly string[] Virtual =
        [TouchLeft, TouchRight, TouchTopLeft, TouchTopRight, TouchBottomLeft, TouchBottomRight, SwipeUp, SwipeDown, SwipeLeft, SwipeRight, GyroShake];

    public static IEnumerable<string> All => Buttons.Select(b => b.ToString()).Concat(Virtual);
    public static bool IsValid(string s) => All.Contains(s);
}

/// <summary>Keyboard key names understood by the injector, with their Windows virtual key codes.</summary>
public static class KeyNames
{
    public static readonly IReadOnlyDictionary<string, ushort> VirtualKeys = Build();

    private static Dictionary<string, ushort> Build()
    {
        var d = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) d[c.ToString()] = c;
        for (int i = 1; i <= 24; i++) d["F" + i] = (ushort)(0x70 + i - 1);
        for (int i = 0; i <= 9; i++) d["Num" + i] = (ushort)(0x60 + i);
        foreach (var (k, v) in new (string, ushort)[]
        {
            ("Space", 0x20), ("Enter", 0x0D), ("Escape", 0x1B), ("Tab", 0x09), ("Backspace", 0x08), ("Delete", 0x2E), ("Insert", 0x2D),
            ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22), ("Up", 0x26), ("Down", 0x28), ("Left", 0x25), ("Right", 0x27),
            ("Shift", 0xA0), ("RShift", 0xA1), ("Ctrl", 0xA2), ("RCtrl", 0xA3), ("Alt", 0xA4), ("RAlt", 0xA5), ("Win", 0x5B),
            ("CapsLock", 0x14), ("PrintScreen", 0x2C), ("Pause", 0x13), ("Minus", 0xBD), ("Plus", 0xBB), ("Comma", 0xBC), ("Period", 0xBE),
            ("VolumeUp", 0xAF), ("VolumeDown", 0xAE), ("VolumeMute", 0xAD), ("MediaNext", 0xB0), ("MediaPrevious", 0xB1), ("MediaPlayPause", 0xB3),
        }) d[k] = v;
        return d;
    }
}
