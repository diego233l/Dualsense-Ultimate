using DualSenseUltimate.Core.Mapping;

namespace DualSenseUltimate.Core.Virtual;

public enum EmulationMode
{
    /// <summary>No virtual pad. Games see the real DualSense (native triggers/haptics). Keyboard/mouse
    /// outputs from the mapping engine still work: this is the hybrid mode.</summary>
    None,
    /// <summary>Virtual Xbox 360 pad (XInput). Xbox One/Series pads look identical to XInput games.</summary>
    Xbox360,
    /// <summary>Virtual DualShock 4 (keeps touchpad, gyro and light bar semantics).</summary>
    DualShock4,
}

public sealed class EmulationConfig
{
    public EmulationMode Mode { get; set; } = EmulationMode.None;
    /// <summary>Hide the physical controller from games with HidHide to prevent double input.</summary>
    public bool HideOriginal { get; set; } = true;
    /// <summary>Send rumble requested by the game on the virtual pad to the real controller.</summary>
    public bool ForwardRumble { get; set; } = true;
    /// <summary>Hybrid: leave the adaptive triggers to the game (the app never writes them).</summary>
    public bool NativeTriggers { get; set; }
    /// <summary>Hybrid: leave rumble/haptics to the game.</summary>
    public bool NativeRumble { get; set; }
    /// <summary>Hybrid: leave the light bar and player LEDs to the game.</summary>
    public bool NativeLighting { get; set; }
    /// <summary>Hybrid: suppress light effects written by the game (e.g. police lights) by re-sending this
    /// profile's lighting continuously. The game keeps its adaptive triggers and haptics.</summary>
    public bool BlockGameLighting { get; set; }
    public EmulationConfig Clone() => (EmulationConfig)MemberwiseClone();
}

public interface IVirtualPad : IDisposable
{
    void Submit(OutputFrame frame);
    /// <summary>Rumble requested by a game on the virtual pad (large/heavy motor, small/light motor).</summary>
    event Action<byte, byte>? Rumble;
}

public interface IVirtualPadFactory
{
    bool IsAvailable { get; }
    /// <summary>Human readable driver status.</summary>
    string Status { get; }
    IVirtualPad Create(EmulationMode mode);
}

public interface IInputInjector
{
    bool IsAvailable { get; }
    void Key(string keyName, bool down);
    void MouseMove(int dx, int dy);
    void MouseButton(MouseButtons button, bool down);
    void Wheel(int notches);
}

public interface IDeviceHider
{
    bool IsAvailable { get; }
    string Status { get; }
    /// <summary>Hides the device behind the given HID path from other applications. Returns false on failure.</summary>
    bool Hide(string hidPath);
    void Unhide(string hidPath);
    /// <summary>Lets the given executable (full path) keep seeing hidden devices. Returns false on failure.</summary>
    bool AllowApplication(string exePath);
    void DisallowApplication(string exePath);
    /// <summary>Removes every device this instance hid (called on exit and on crash recovery).</summary>
    void UnhideAll();
}

/// <summary>Applies keyboard/mouse output by diffing consecutive frames, so keys are pressed and released exactly once.</summary>
public sealed class OutputApplier
{
    private readonly IInputInjector? _injector;
    private HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private MouseButtons _mouse;

    public OutputApplier(IInputInjector? injector) => _injector = injector;

    public void Apply(OutputFrame f)
    {
        if (_injector is not { IsAvailable: true }) return;
        foreach (var k in _keys) if (!f.Keys.Contains(k)) _injector.Key(k, false);
        foreach (var k in f.Keys) if (!_keys.Contains(k)) _injector.Key(k, true);
        _keys = new HashSet<string>(f.Keys, StringComparer.OrdinalIgnoreCase);
        if (f.Mouse != _mouse)
        {
            foreach (var b in new[] { MouseButtons.Left, MouseButtons.Right, MouseButtons.Middle, MouseButtons.X1, MouseButtons.X2 })
                if (((f.Mouse ^ _mouse) & b) != 0) _injector.MouseButton(b, (f.Mouse & b) != 0);
            _mouse = f.Mouse;
        }
        if (f.MouseDx != 0 || f.MouseDy != 0) _injector.MouseMove((int)f.MouseDx, (int)f.MouseDy);
        if (f.Wheel != 0) _injector.Wheel(f.Wheel);
    }

    /// <summary>Releases everything still held. Must be called when a profile changes or a controller disconnects
    /// so no key is ever left stuck.</summary>
    public void ReleaseAll()
    {
        if (_injector is { IsAvailable: true })
        {
            foreach (var k in _keys) _injector.Key(k, false);
            foreach (var b in new[] { MouseButtons.Left, MouseButtons.Right, MouseButtons.Middle, MouseButtons.X1, MouseButtons.X2 })
                if ((_mouse & b) != 0) _injector.MouseButton(b, false);
        }
        _keys.Clear(); _mouse = MouseButtons.None;
    }
}
