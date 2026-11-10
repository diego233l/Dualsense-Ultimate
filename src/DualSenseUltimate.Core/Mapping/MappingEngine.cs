using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Mapping;

/// <summary>Turns physical input into an <see cref="OutputFrame"/>. Deterministic and time-driven (the
/// caller supplies elapsed milliseconds), so every behaviour is unit-testable without hardware.</summary>
public sealed class MappingEngine
{
    private const int TapPulseMs = 60;
    private MappingConfig _cfg;
    private readonly Dictionary<string, SourceState> _states = new();
    private readonly List<(List<MapAction> actions, double remainingMs)> _pulses = new();
    private readonly List<MacroRun> _macros = new();
    private double _gyroSmX, _gyroSmY, _mouseRemX, _mouseRemY, _wheelRem, _shakeCooldown;
    private TouchPoint _prevT0, _prevT1;
    private double _touchDownMs, _touchTravel, _touchStartX, _touchStartY;
    private bool _touchTracking;
    private readonly HashSet<string> _virtualActive = new();
    public bool GyroEnabled { get; set; } = true;
    public bool Enabled { get; set; } = true;

    private sealed class SourceState
    {
        public bool Down, Toggled, HoldFired, WaitingSecondTap;
        public double DownMs, SinceReleaseMs, TurboPhaseMs;
    }

    private sealed class MacroRun
    {
        public required Macro Macro; public required string Source;
        public int Step; public double WaitMs; public readonly List<MapAction> Held = new(); public MapAction? TapAction; public bool Done;
    }

    public MappingEngine(MappingConfig? config = null) => _cfg = config ?? new MappingConfig();

    public MappingConfig Config
    {
        get => _cfg;
        set { _cfg = value ?? new MappingConfig(); _states.Clear(); _pulses.Clear(); _macros.Clear(); }
    }

    public OutputFrame Process(in InputState s, double dtMs, ImuCalibration? calibration = null)
    {
        var f = new OutputFrame { Touch0 = s.Touch0, Touch1 = s.Touch1, GyroX = s.GyroX, GyroY = s.GyroY, GyroZ = s.GyroZ, AccelX = s.AccelX, AccelY = s.AccelY, AccelZ = s.AccelZ };
        dtMs = Math.Clamp(dtMs, 0, 250);
        if (!Enabled)
        {
            // Mapping suspended: plain pass-through.
            f.Buttons = s.Buttons & Sources.PassThrough;
            (f.LeftX, f.LeftY) = (StickProcessor.Normalize(s.LeftX), -StickProcessor.Normalize(s.LeftY));
            (f.RightX, f.RightY) = (StickProcessor.Normalize(s.RightX), -StickProcessor.Normalize(s.RightY));
            f.L2 = s.L2 / 255.0; f.R2 = s.R2 / 255.0;
            return f;
        }

        // ---- virtual sources (touch regions, swipes, shake)
        _virtualActive.Clear();
        ProcessTouchpad(s, dtMs, f);
        ProcessShake(s, dtMs, calibration);

        // ---- which sources are physically down
        var down = new HashSet<string>(_virtualActive);
        foreach (var b in Sources.Buttons) if (s.IsDown(b)) down.Add(b.ToString());
        bool touchClickConsumed = _cfg.Touchpad.Mode is TouchpadMode.TwoRegions or TouchpadMode.FourRegions or TouchpadMode.Mouse or TouchpadMode.Disabled;
        if (touchClickConsumed) down.Remove(nameof(PadButtons.Touchpad));

        bool shift = _cfg.ShiftButton != null && down.Contains(_cfg.ShiftButton);
        var suppressed = new HashSet<string>();
        if (_cfg.ShiftButton != null) suppressed.Add(_cfg.ShiftButton);

        // ---- chords
        foreach (var chord in _cfg.Chords)
        {
            if (chord.Buttons.Count < 2 || !chord.Buttons.All(down.Contains)) continue;
            foreach (var b in chord.Buttons) suppressed.Add(b);
            Emit(chord.Actions, f, "chord:" + string.Join("+", chord.Buttons), true);
        }

        // ---- per source bindings
        foreach (var name in Sources.All)
        {
            bool isDown = down.Contains(name) && !suppressed.Contains(name);
            if (!_states.TryGetValue(name, out var st)) { if (!isDown) continue; _states[name] = st = new SourceState(); }
            _cfg.Bindings.TryGetValue(name, out var binding);
            if (binding == null)
            {
                st.Down = isDown;
                // Unbound: pass through real pad buttons.
                if (isDown && Enum.TryParse<PadButtons>(name, out var pb) && (pb & Sources.PassThrough) != 0) f.Buttons |= pb;
                continue;
            }
            RunBinding(name, binding, st, isDown, shift, dtMs, f);
        }

        // ---- timed pulses and macros
        for (int i = _pulses.Count - 1; i >= 0; i--)
        {
            var (actions, rem) = _pulses[i];
            Emit(actions, f, "pulse", false);
            rem -= dtMs;
            if (rem <= 0) _pulses.RemoveAt(i); else _pulses[i] = (actions, rem);
        }
        RunMacros(dtMs, f, down);

        // ---- analog
        var (lx, ly) = StickProcessor.Apply(_cfg.LeftStick, s.LeftX, s.LeftY);
        var (rx, ry) = StickProcessor.Apply(_cfg.RightStick, s.RightX, s.RightY);
        if (_cfg.SwapSticks) (lx, ly, rx, ry) = (rx, ry, lx, ly);
        ApplyStick(_cfg.LeftStick, lx, ly, dtMs, f, left: true);
        ApplyStick(_cfg.RightStick, rx, ry, dtMs, f, left: false);
        f.L2 = Math.Max(f.L2, _cfg.Bindings.ContainsKey(nameof(PadButtons.L2)) ? 0 : StickProcessor.ApplyTrigger(_cfg.L2, s.L2));
        f.R2 = Math.Max(f.R2, _cfg.Bindings.ContainsKey(nameof(PadButtons.R2)) ? 0 : StickProcessor.ApplyTrigger(_cfg.R2, s.R2));
        if (f.Buttons.HasFlag(PadButtons.L2) && f.L2 <= 0) f.L2 = 1;
        if (f.Buttons.HasFlag(PadButtons.R2) && f.R2 <= 0) f.R2 = 1;

        ProcessGyro(s, dtMs, calibration, f, down);

        // ---- integer mouse output with sub-pixel accumulation
        _mouseRemX += f.MouseDx; _mouseRemY += f.MouseDy;
        f.MouseDx = Math.Truncate(_mouseRemX); f.MouseDy = Math.Truncate(_mouseRemY);
        _mouseRemX -= f.MouseDx; _mouseRemY -= f.MouseDy;
        return f;
    }

    private void RunBinding(string name, ButtonBinding b, SourceState st, bool isDown, bool shift, double dtMs, OutputFrame f)
    {
        bool pressed = isDown && !st.Down, released = !isDown && st.Down;
        var press = shift && b.Shift is { Count: > 0 } ? b.Shift : b.Press;
        bool hasHold = b.Hold is { Count: > 0 }, hasDouble = b.DoubleTap is { Count: > 0 };
        if (isDown) st.DownMs = pressed ? 0 : st.DownMs + dtMs;
        if (!isDown) st.SinceReleaseMs += dtMs;

        if (!hasHold && !hasDouble)
        {
            switch (b.Mode)
            {
                case PressMode.Toggle:
                    if (pressed) st.Toggled = !st.Toggled;
                    if (st.Toggled) Emit(press, f, name, pressed);
                    break;
                case PressMode.Turbo:
                    if (isDown)
                    {
                        double period = 1000.0 / Math.Clamp(b.TurboHz, 1, 30);
                        st.TurboPhaseMs = pressed ? 0 : (st.TurboPhaseMs + dtMs) % period;
                        if (st.TurboPhaseMs < period / 2) Emit(press, f, name, pressed);
                    }
                    break;
                default:
                    if (isDown) Emit(press, f, name, pressed);
                    break;
            }
        }
        else
        {
            // Layered press: short press taps Press, long press holds Hold, double press taps DoubleTap.
            if (pressed)
            {
                if (hasDouble && st.WaitingSecondTap && st.SinceReleaseMs <= b.DoubleTapMs)
                {
                    st.WaitingSecondTap = false; st.HoldFired = true; // consume this press
                    Pulse(b.DoubleTap!, f);
                }
                else st.HoldFired = false;
            }
            if (isDown && hasHold && st.DownMs >= b.HoldMs && (!st.HoldFired || st.Toggled))
            {
                bool first = !st.Toggled;
                st.HoldFired = true; st.Toggled = true; // Toggled doubles as "hold active"
                Emit(b.Hold!, f, name + ":hold", first);
            }
            if (released)
            {
                bool wasHold = st.Toggled;
                st.Toggled = false;
                if (!wasHold && !st.HoldFired)
                {
                    if (hasDouble) { st.WaitingSecondTap = true; st.SinceReleaseMs = 0; }
                    else Pulse(press, f);
                }
            }
            if (!isDown && st.WaitingSecondTap && st.SinceReleaseMs > b.DoubleTapMs)
            {
                st.WaitingSecondTap = false;
                Pulse(press, f);
            }
        }
        st.Down = isDown;
    }

    private void Pulse(List<MapAction> actions, OutputFrame f)
    {
        if (actions.Count == 0) return;
        Emit(actions, f, "pulse", true);
        _pulses.Add((actions, TapPulseMs));
    }

    /// <summary>Applies actions to the frame. <paramref name="edge"/> is true on the frame the source became active.</summary>
    private void Emit(List<MapAction> actions, OutputFrame f, string source, bool edge)
    {
        foreach (var a in actions)
        {
            switch (a.Type)
            {
                case ActionType.PadButton: if (Enum.TryParse<PadButtons>(a.Target, out var pb)) f.Buttons |= pb; break;
                case ActionType.Key: if (KeyNames.VirtualKeys.ContainsKey(a.Target)) f.Keys.Add(a.Target); break;
                case ActionType.MouseButton: if (Enum.TryParse<MouseButtons>(a.Target, out var mb)) f.Mouse |= mb; break;
                case ActionType.MouseWheel: if (edge) f.Wheel += a.Target.Equals("Up", StringComparison.OrdinalIgnoreCase) ? 1 : -1; break;
                case ActionType.Special: if (edge) f.Specials.Add(a.Target); break;
                case ActionType.Macro:
                    if (edge && _cfg.Macros.FirstOrDefault(m => m.Name == a.Target) is { } macro && !_macros.Any(r => r.Macro == macro && !r.Done) && _macros.Count < 16)
                        _macros.Add(new MacroRun { Macro = macro, Source = source });
                    break;
            }
        }
    }

    private void RunMacros(double dtMs, OutputFrame f, HashSet<string> down)
    {
        foreach (var run in _macros)
        {
            double budget = dtMs; int guard = 0;
            while (!run.Done && guard++ < 256)
            {
                if (run.WaitMs > 0)
                {
                    double used = Math.Min(run.WaitMs, budget);
                    run.WaitMs -= used; budget -= used;
                    if (run.WaitMs > 0) break;
                    run.TapAction = null;
                }
                if (run.Step >= run.Macro.Steps.Count)
                {
                    if (run.Macro.RepeatWhileHeld && down.Contains(run.Source.Split(':')[0]) && run.Macro.Steps.Any(s => s.Kind is MacroStepKind.Delay or MacroStepKind.Tap))
                    { run.Step = 0; run.Held.Clear(); continue; }
                    run.Done = true; break;
                }
                var step = run.Macro.Steps[run.Step++];
                switch (step.Kind)
                {
                    case MacroStepKind.Delay: run.WaitMs = Math.Clamp(step.Ms, 1, 60000); break;
                    case MacroStepKind.Down: if (step.Action != null && step.Action.Type != ActionType.Macro) run.Held.Add(step.Action); break;
                    case MacroStepKind.Up: if (step.Action != null) run.Held.RemoveAll(h => h.Type == step.Action.Type && h.Target == step.Action.Target); break;
                    case MacroStepKind.Tap:
                        if (step.Action != null && step.Action.Type != ActionType.Macro) { run.TapAction = step.Action; run.WaitMs = Math.Clamp(step.Ms, 10, 5000); }
                        break;
                }
            }
            if (run.Held.Count > 0) Emit(run.Held, f, "macro", false);
            if (run.TapAction != null) Emit([run.TapAction], f, "macro", false);
        }
        _macros.RemoveAll(r => r.Done);
    }

    private void ApplyStick(StickConfig c, double x, double y, double dtMs, OutputFrame f, bool left)
    {
        switch (c.Output)
        {
            case StickOutput.Stick:
                if (left) { f.LeftX = x; f.LeftY = y; } else { f.RightX = x; f.RightY = y; }
                break;
            case StickOutput.Mouse:
                f.MouseDx += x * c.MouseSpeed * dtMs / 1000.0;
                f.MouseDy += -y * c.MouseSpeed * dtMs / 1000.0;
                break;
            case StickOutput.Keys:
                if (c.Keys.Length >= 4)
                {
                    if (y > 0.5) f.Keys.Add(c.Keys[0]); if (y < -0.5) f.Keys.Add(c.Keys[1]);
                    if (x < -0.5) f.Keys.Add(c.Keys[2]); if (x > 0.5) f.Keys.Add(c.Keys[3]);
                }
                break;
        }
    }

    private void ProcessGyro(in InputState s, double dtMs, ImuCalibration? cal, OutputFrame f, HashSet<string> down)
    {
        var g = _cfg.Gyro;
        if (g.Mode == GyroMode.Off || !GyroEnabled) { _gyroSmX = _gyroSmY = 0; return; }
        if (g.ActivationButton != null)
        {
            bool held = down.Contains(g.ActivationButton);
            if (held == g.ActivationDisables) { _gyroSmX = _gyroSmY = 0; return; }
        }
        var (pitch, yaw, roll) = (cal ?? ImuCalibration.Identity).GyroDps(s);
        pitch -= g.BiasX; yaw -= g.BiasY; roll -= g.BiasZ;
        double hx = -(g.UseRollForX ? roll : yaw); // turning the controller right moves right
        double vy = -pitch;                        // tilting up moves up (negative screen Y)
        if (Math.Sqrt(hx * hx + vy * vy) < g.DeadZone) { hx = 0; vy = 0; }
        double k = Math.Clamp(g.Smoothing, 0, 0.98);
        _gyroSmX = _gyroSmX * k + hx * (1 - k);
        _gyroSmY = _gyroSmY * k + vy * (1 - k);
        double x = g.InvertX ? -_gyroSmX : _gyroSmX, y = g.InvertY ? -_gyroSmY : _gyroSmY;
        if (g.Mode == GyroMode.Mouse)
        {
            f.MouseDx += x * g.SensitivityX * dtMs / 1000.0;
            f.MouseDy += y * g.SensitivityY * dtMs / 1000.0;
        }
        else
        {
            double sx = Math.Clamp(x * g.SensitivityX / 6.0 / 180.0, -1, 1), sy = Math.Clamp(-y * g.SensitivityY / 6.0 / 180.0, -1, 1);
            if (g.Mode == GyroMode.LeftStick) { f.LeftX = Math.Clamp(f.LeftX + sx, -1, 1); f.LeftY = Math.Clamp(f.LeftY + sy, -1, 1); }
            else { f.RightX = Math.Clamp(f.RightX + sx, -1, 1); f.RightY = Math.Clamp(f.RightY + sy, -1, 1); }
        }
    }

    private void ProcessShake(in InputState s, double dtMs, ImuCalibration? cal)
    {
        if (!_cfg.Gyro.ShakeGesture) return;
        _shakeCooldown = Math.Max(0, _shakeCooldown - dtMs);
        var (ax, ay, az) = (cal ?? ImuCalibration.Identity).AccelG(s);
        double excess = Math.Abs(Math.Sqrt(ax * ax + ay * ay + az * az) - 1.0);
        if (excess > _cfg.Gyro.ShakeThresholdG && _shakeCooldown <= 0) _shakeCooldown = 500;
        if (_shakeCooldown > 380) _virtualActive.Add(Sources.GyroShake); // active for ~120 ms
    }

    private void ProcessTouchpad(in InputState s, double dtMs, OutputFrame f)
    {
        var tc = _cfg.Touchpad;
        var t0 = s.Touch0; var t1 = s.Touch1;
        bool click = s.IsDown(PadButtons.Touchpad);

        if (tc.Mode == TouchpadMode.Disabled) { f.Touch0 = f.Touch1 = new TouchPoint(false, 0, 0, 0); }
        else if (tc.Mode is TouchpadMode.TwoRegions or TouchpadMode.FourRegions)
        {
            if (click)
            {
                // Region is decided by the finger position; with no finger data fall back to the left / top-left region.
                bool right = t0.Active && t0.X >= DualSenseProtocol.TouchpadWidth / 2;
                bool bottom = t0.Active && t0.Y >= DualSenseProtocol.TouchpadHeight / 2;
                _virtualActive.Add(tc.Mode == TouchpadMode.TwoRegions
                    ? (right ? Sources.TouchRight : Sources.TouchLeft)
                    : (bottom ? (right ? Sources.TouchBottomRight : Sources.TouchBottomLeft) : (right ? Sources.TouchTopRight : Sources.TouchTopLeft)));
            }
        }
        else if (tc.Mode == TouchpadMode.Mouse)
        {
            double sens = Math.Max(0.05, tc.Sensitivity);
            bool two = t0.Active && t1.Active;
            if (two && tc.TwoFingerScroll && _prevT0.Active && _prevT1.Active && _prevT0.Id == t0.Id)
            {
                _wheelRem += -(t0.Y - _prevT0.Y) / 90.0;
                int notches = (int)Math.Truncate(_wheelRem);
                f.Wheel += notches; _wheelRem -= notches;
            }
            else if (t0.Active && _prevT0.Active && _prevT0.Id == t0.Id && !two)
            {
                f.MouseDx += (t0.X - _prevT0.X) * 0.6 * sens;
                f.MouseDy += (t0.Y - _prevT0.Y) * 0.6 * sens;
            }
            if (click) f.Mouse |= two ? MouseButtons.Right : MouseButtons.Left;
            f.Touch0 = f.Touch1 = new TouchPoint(false, 0, 0, 0);
        }

        // Tap and swipe detection on the primary finger.
        if (t0.Active && !_prevT0.Active) { _touchTracking = true; _touchDownMs = 0; _touchTravel = 0; _touchStartX = t0.X; _touchStartY = t0.Y; }
        else if (t0.Active && _touchTracking)
        {
            _touchDownMs += dtMs;
            _touchTravel += Math.Abs(t0.X - _prevT0.X) + Math.Abs(t0.Y - _prevT0.Y);
        }
        else if (!t0.Active && _prevT0.Active && _touchTracking)
        {
            _touchTracking = false;
            double dx = _prevT0.X - _touchStartX, dy = _prevT0.Y - _touchStartY;
            if (tc.Mode == TouchpadMode.Mouse && tc.TapToClick && _touchDownMs < 180 && _touchTravel < 40)
                _pulses.Add(([new MapAction(ActionType.MouseButton, nameof(MouseButtons.Left))], TapPulseMs));
            else if (tc.SwipeGestures && _touchDownMs < 500 && (Math.Abs(dx) > 500 || Math.Abs(dy) > 300))
            {
                string src = Math.Abs(dx) / 1920.0 > Math.Abs(dy) / 1080.0 ? (dx > 0 ? Sources.SwipeRight : Sources.SwipeLeft) : (dy > 0 ? Sources.SwipeDown : Sources.SwipeUp);
                _swipe = (src, 100);
            }
        }
        if (_swipe.remaining > 0) { _virtualActive.Add(_swipe.source); _swipe.remaining -= dtMs; }
        _prevT0 = t0; _prevT1 = t1;
    }

    private (string source, double remaining) _swipe = ("", 0);
}
