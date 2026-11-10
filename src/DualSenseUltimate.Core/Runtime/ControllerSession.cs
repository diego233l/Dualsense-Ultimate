using System.Diagnostics;
using DualSenseUltimate.Core.Devices;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Haptics;
using DualSenseUltimate.Core.Lighting;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Virtual;

namespace DualSenseUltimate.Core.Runtime;

/// <summary>Everything attached to one connected controller. It is the single owner of the device's
/// output report: lighting, triggers, haptics and virtual pad rumble are merged here, so modules can
/// never send conflicting reports.</summary>
public sealed class ControllerSession : IDisposable
{
    private readonly PlatformServices _platform;
    private readonly MappingEngine _mapping = new();
    private readonly OutputApplier _applier;
    private readonly HapticPlayer _haptics = new();
    private readonly RumbleMixer _mixer = new();
    private readonly object _sync = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastInputTicks;
    private IVirtualPad? _pad;
    private bool _hidden;
    private Rgb? _overrideColor; private double _overrideUntil = double.MaxValue;
    private TriggerEffect? _overrideL2, _overrideR2;
    private double _rumbleUntil, _batteryFlashUntil, _notificationAt = double.MinValue;
    private int _lastBattery = -1; private BatteryStatus _lastStatus = BatteryStatus.Unknown;
    private OutputFrame _lastFrame = new();
    private bool _disposed;

    public ControllerDevice Device { get; }
    public string Id => Device.Id;
    public Profile Profile { get; private set; } = new();
    /// <summary>Why the current profile is active: "manual", "default" or "game:&lt;exe&gt;".</summary>
    public string ProfileReason { get; private set; } = "default";
    public OutputState LastOutput { get; private set; } = new();
    public OutputFrame LastFrame => _lastFrame;
    public double AudioLevel { get; set; }
    public string EmulationStatus { get; private set; } = "Sin emulación (DualSense nativo)";
    public bool MappingEnabled { get => _mapping.Enabled; set => _mapping.Enabled = value; }
    public StickCalibrator? ActiveCalibrator { get; set; }
    public bool CalibrateRightStick { get; set; }

    /// <summary>Special mapping actions (profile switching etc.) are forwarded to the runtime.</summary>
    public event Action<ControllerSession, string>? SpecialAction;
    public event Action<BatteryChangedEvent>? BatteryChanged;

    public ControllerSession(ControllerDevice device, PlatformServices platform)
    {
        Device = device; _platform = platform;
        _applier = new OutputApplier(platform.Injector);
        device.InputReceived += OnInput;
    }

    public void ApplyProfile(Profile profile, string reason)
    {
        lock (_sync)
        {
            _applier.ReleaseAll();
            Profile = profile; ProfileReason = reason;
            _mapping.Config = profile.Mapping;
            _mapping.Enabled = true;
            _overrideL2 = _overrideR2 = null;
            _haptics.Stop(); _mixer.Clear();
            ConfigureEmulation(profile.Emulation);
        }
    }

    private void ConfigureEmulation(EmulationConfig e)
    {
        _pad?.Dispose(); _pad = null;
        if (_hidden) { _platform.Hider?.Unhide(Device.Descriptor.Path); _hidden = false; }
        if (e.Mode == EmulationMode.None)
        {
            EmulationStatus = "Sin emulación (DualSense nativo, modo híbrido disponible)";
            return;
        }
        if (Device.IsSimulated) { EmulationStatus = "Emulación no disponible en mandos simulados"; return; }
        var factory = _platform.PadFactory;
        if (factory is not { IsAvailable: true }) { EmulationStatus = "Emulación no disponible: " + (factory?.Status ?? "plataforma no compatible"); return; }
        try
        {
            _pad = factory.Create(e.Mode);
            if (e.ForwardRumble) _pad.Rumble += (large, small) => _mixer.Set("virtual-pad", large, small);
            EmulationStatus = $"Emulando {e.Mode}";
            if (e.HideOriginal)
            {
                if (_platform.Hider is { IsAvailable: true } hider && hider.Hide(Device.Descriptor.Path)) { _hidden = true; EmulationStatus += " · mando físico oculto (HidHide)"; }
                else EmulationStatus += " · AVISO: mando físico visible (HidHide no disponible): posible doble entrada";
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[{Id}] virtual pad creation failed", ex);
            EmulationStatus = "Error al crear el mando virtual: " + ex.Message;
        }
    }

    private void OnInput(ControllerDevice dev, InputState s)
    {
        long now = Stopwatch.GetTimestamp();
        double dt = _lastInputTicks == 0 ? 4 : Stopwatch.GetElapsedTime(_lastInputTicks, now).TotalMilliseconds;
        _lastInputTicks = now;

        if (s.BatteryPercent != _lastBattery || s.BatteryStatus != _lastStatus)
        {
            var evt = new BatteryChangedEvent(Id, s.BatteryPercent, s.BatteryStatus, _lastStatus);
            _lastBattery = s.BatteryPercent; _lastStatus = s.BatteryStatus;
            BatteryChanged?.Invoke(evt);
        }
        if (ActiveCalibrator is { } cal) { if (CalibrateRightStick) cal.AddSample(s.RightX, s.RightY); else cal.AddSample(s.LeftX, s.LeftY); }

        OutputFrame frame;
        lock (_sync)
        {
            if (_disposed) return;
            frame = _mapping.Process(s, dt, dev.Calibration);
            _lastFrame = frame;
            _applier.Apply(frame);
            try { _pad?.Submit(frame); } catch (Exception ex) { Log.Warn($"[{Id}] virtual pad submit failed: {ex.Message}"); }
        }
        foreach (var sp in frame.Specials)
        {
            switch (sp)
            {
                case SpecialActions.ToggleGyro: _mapping.GyroEnabled = !_mapping.GyroEnabled; break;
                case SpecialActions.ToggleMapping: _mapping.Enabled = !_mapping.Enabled; lock (_sync) _applier.ReleaseAll(); break;
                case SpecialActions.BatteryFlash: _batteryFlashUntil = _clock.Elapsed.TotalSeconds + 3; break;
                case SpecialActions.StopEffects: StopEffects(); break;
                default: SpecialAction?.Invoke(this, sp); break;
            }
        }
    }

    // ---- control surface used by UI, API, automation and plugins

    public void SetLightbarOverride(Rgb color, int durationMs)
    {
        _overrideColor = color;
        _overrideUntil = durationMs > 0 ? _clock.Elapsed.TotalSeconds + durationMs / 1000.0 : double.MaxValue;
    }

    public void ClearOverrides() { _overrideColor = null; _overrideL2 = _overrideR2 = null; }

    public void SetTriggerOverride(bool left, bool right, TriggerEffect? effect)
    {
        if (left) _overrideL2 = effect;
        if (right) _overrideR2 = effect;
    }

    public void PlayHaptic(HapticPattern pattern) { lock (_haptics) _haptics.Play(pattern); }

    public void Rumble(byte left, byte right, int durationMs)
    {
        _mixer.Set("direct", left, right);
        _rumbleUntil = _clock.Elapsed.TotalSeconds + Math.Clamp(durationMs, 1, 30000) / 1000.0;
    }

    public void SetAudioRumble(byte left, byte right) => _mixer.Set("audio", left, right);

    public void NotifyFlash() => _notificationAt = _clock.Elapsed.TotalSeconds;

    public void StopEffects()
    {
        lock (_haptics) _haptics.Stop();
        _mixer.Clear();
        ClearOverrides();
    }

    /// <summary>Recomputes the output report. Called by the runtime at ~60 Hz.</summary>
    public void Tick(double dtMs)
    {
        if (_disposed) return;
        double t = _clock.Elapsed.TotalSeconds;
        var p = Profile; var st = Device.State;
        var ctx = new LightingContext(st.BatteryPercent, st.BatteryStatus == BatteryStatus.Charging, AudioLevel, t - _notificationAt);

        if (_overrideColor != null && t > _overrideUntil) _overrideColor = null;
        if (_rumbleUntil > 0 && t > _rumbleUntil) { _mixer.Set("direct", 0, 0); _rumbleUntil = 0; }
        (byte l, byte r) pattern;
        lock (_haptics) pattern = _haptics.Advance(dtMs);
        _mixer.Set("pattern", pattern.l, pattern.r);

        var o = new OutputState();
        Rgb color = _overrideColor ?? LightingEngine.Compute(p.Lighting, t, ctx);
        if (t < _batteryFlashUntil) color = LightingEngine.BatteryColor(ctx, t);
        (o.Red, o.Green, o.Blue) = (color.R, color.G, color.B);
        o.PlayerLeds = t < _batteryFlashUntil
            ? LightingEngine.PlayerLedBits(new LightingConfig { PlayerLeds = PlayerLedMode.Battery }, ctx)
            : LightingEngine.PlayerLedBits(p.Lighting, ctx);
        o.PlayerLedBrightness = p.Lighting.PlayerLedBrightness;
        o.MicLed = p.Lighting.MicLed;
        (o.MotorLeft, o.MotorRight) = _mixer.Mix(p.Haptics.Intensity);
        o.LeftTrigger = TriggerEffectEncoder.Encode(_overrideL2 ?? p.L2);
        o.RightTrigger = TriggerEffectEncoder.Encode(_overrideR2 ?? p.R2);

        var own = OutputOwnership.All;
        var e = p.Emulation;
        if (e.NativeTriggers && _overrideL2 == null && _overrideR2 == null) own &= ~(OutputOwnership.LeftTrigger | OutputOwnership.RightTrigger);
        if (e.NativeRumble) own &= ~OutputOwnership.Rumble;
        if (e.NativeLighting && _overrideColor == null) own &= ~(OutputOwnership.Lightbar | OutputOwnership.PlayerLeds | OutputOwnership.MicLed);
        if (!Device.IsDualSense) own &= OutputOwnership.Rumble | OutputOwnership.Lightbar;
        o.Ownership = own;

        LastOutput = o;
        Device.SetOutput(o);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            Device.InputReceived -= OnInput;
            _applier.ReleaseAll();
            try { _pad?.Dispose(); } catch { }
            _pad = null;
            if (_hidden) { try { _platform.Hider?.Unhide(Device.Descriptor.Path); } catch { } _hidden = false; }
        }
    }
}

/// <summary>OS specific services. Every member is optional: on unsupported platforms the feature is reported as unavailable.</summary>
public sealed class PlatformServices
{
    public IInputInjector? Injector { get; init; }
    public IVirtualPadFactory? PadFactory { get; init; }
    public IDeviceHider? Hider { get; init; }
    public IAudioLevelSource? AudioSource { get; init; }
    public IHapticAudioOutput? HapticAudio { get; init; }
    public IProcessSource ProcessSource { get; init; } = new SystemProcessSource();
    /// <summary>Names of running programs known to conflict (Steam, DS4Windows, DSX...).</summary>
    public Func<IReadOnlyList<string>>? ConflictDetector { get; init; }
}
