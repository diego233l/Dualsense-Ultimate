using DualSenseUltimate.Core.Haptics;
using DualSenseUltimate.Core.Lighting;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Virtual;

namespace DualSenseUltimate.Tests;

public class LightingTests
{
    private static readonly LightingContext Ctx = LightingContext.Default;

    [Fact] public void Static_color_with_brightness() =>
        Assert.Equal(new Rgb(100, 50, 0), LightingEngine.Compute(new LightingConfig { Mode = LightingMode.Static, Color = "#C86400", Brightness = 50 }, 3, Ctx));

    [Fact]
    public void Breathing_goes_dark_then_full()
    {
        var c = new LightingConfig { Mode = LightingMode.Breathing, Color = "#FF0000", PeriodSeconds = 4 };
        Assert.Equal(Rgb.Black, LightingEngine.Compute(c, 0, Ctx));
        Assert.Equal(new Rgb(255, 0, 0), LightingEngine.Compute(c, 2, Ctx));
    }

    [Fact]
    public void Rainbow_cycles_through_hues()
    {
        var c = new LightingConfig { Mode = LightingMode.Rainbow, PeriodSeconds = 6 };
        Assert.Equal(new Rgb(255, 0, 0), LightingEngine.Compute(c, 0, Ctx));
        Assert.Equal(new Rgb(0, 255, 0), LightingEngine.Compute(c, 2, Ctx));
        Assert.Equal(new Rgb(0, 0, 255), LightingEngine.Compute(c, 4, Ctx));
    }

    [Fact]
    public void Battery_mode_is_red_when_low_green_when_full()
    {
        var c = new LightingConfig { Mode = LightingMode.Battery };
        var low = LightingEngine.Compute(c, 0.1, new LightingContext(20, false, 0, double.MaxValue));
        var full = LightingEngine.Compute(c, 0.1, new LightingContext(100, false, 0, double.MaxValue));
        Assert.True(low.R > low.G); Assert.True(full.G > full.R);
    }

    [Fact]
    public void Audio_reactive_follows_level()
    {
        var c = new LightingConfig { Mode = LightingMode.AudioReactive, Color = "#FFFFFF", Color2 = "#000000" };
        Assert.True(LightingEngine.Compute(c, 0, Ctx with { AudioLevel = 1 }).R > LightingEngine.Compute(c, 0, Ctx with { AudioLevel = 0.1 }).R);
    }

    [Fact]
    public void Custom_keyframes_interpolate_and_loop()
    {
        var c = new LightingConfig { Mode = LightingMode.Custom, PeriodSeconds = 10, Keyframes = [new() { Time = 0, Color = "#000000" }, new() { Time = 0.5, Color = "#FFFFFF" }] };
        Assert.Equal(new Rgb(128, 128, 128), LightingEngine.Compute(c, 2.5, Ctx));
        Assert.Equal(new Rgb(128, 128, 128), LightingEngine.Compute(c, 7.5, Ctx)); // wrapping back to the first key
    }

    [Fact]
    public void Notification_flash_overrides_briefly()
    {
        var c = new LightingConfig { Mode = LightingMode.Static, Color = "#000000", NotificationColor = "#FFFFFF" };
        Assert.NotEqual(Rgb.Black, LightingEngine.Compute(c, 0, Ctx with { SecondsSinceNotification = 0.05 }));
        Assert.Equal(Rgb.Black, LightingEngine.Compute(c, 0, Ctx with { SecondsSinceNotification = 5 }));
    }

    [Theory]
    [InlineData(PlayerLedMode.Player1, 100, 0b00100)]
    [InlineData(PlayerLedMode.Player4, 100, 0b11011)]
    [InlineData(PlayerLedMode.Battery, 100, 0b11111)]
    [InlineData(PlayerLedMode.Battery, 45, 0b00111)]
    [InlineData(PlayerLedMode.Battery, 5, 0b00001)]
    [InlineData(PlayerLedMode.Off, 100, 0)]
    public void Player_leds(PlayerLedMode mode, int battery, int expected) =>
        Assert.Equal(expected, LightingEngine.PlayerLedBits(new LightingConfig { PlayerLeds = mode }, new LightingContext(battery, false, 0, 99)));

    [Fact] public void Rgb_parse_handles_bad_input() => Assert.Equal(new Rgb(1, 2, 3), Rgb.Parse("not a colour", new Rgb(1, 2, 3)));
}

public class HapticTests
{
    [Fact]
    public void Pattern_plays_steps_in_time_and_stops()
    {
        var p = new HapticPlayer();
        p.Play(HapticPattern.Make("t", 1, (100, 10, 20), (50, 0, 0), (100, 30, 40)));
        Assert.Equal((10, 20), p.Advance(10));
        Assert.Equal((0, 0), p.Advance(100));   // t=110: silent step
        Assert.Equal((30, 40), p.Advance(50));  // t=160
        Assert.Equal((0, 0), p.Advance(200));   // finished
        Assert.False(p.IsPlaying);
    }

    [Fact]
    public void Pattern_loops_until_stopped()
    {
        var p = new HapticPlayer();
        p.Play(HapticPattern.Make("loop", 0, (10, 5, 5)));
        for (int i = 0; i < 100; i++) Assert.Equal((5, 5), p.Advance(7));
        p.Stop();
        Assert.Equal((0, 0), p.Advance(7));
    }

    [Fact]
    public void Mixer_takes_strongest_source_and_scales()
    {
        var m = new RumbleMixer();
        m.Set("a", 100, 10); m.Set("b", 20, 200);
        Assert.Equal((100, 200), m.Mix(100));
        Assert.Equal((50, 100), m.Mix(50));
        m.Set("a", 0, 0);
        Assert.Equal((20, 200), m.Mix(100));
    }

    [Fact]
    public void Audio_processor_low_pass_attenuates_treble_and_tracks_envelope()
    {
        var cfg = new HapticsConfig { AudioCutoffHz = 150 };
        var bass = HapticAudioProcessor.TestTone(48000, 0.5, 60, 60);
        var treble = HapticAudioProcessor.TestTone(48000, 0.5, 4000, 4000);
        var p1 = new HapticAudioProcessor(48000, cfg); p1.Process(bass.AsSpan(0, 20000));
        var p2 = new HapticAudioProcessor(48000, cfg); p2.Process(treble.AsSpan(0, 20000));
        Assert.True(p1.Envelope > 0.2, $"bass envelope {p1.Envelope}");
        Assert.True(p2.Envelope < 0.02, $"treble envelope {p2.Envelope}");
        Assert.True(p1.EnvelopeToRumble().Left > 0);
        Assert.Equal((0, 0), p2.EnvelopeToRumble());
    }

    [Fact]
    public void Test_tone_stays_in_range()
    {
        var t = HapticAudioProcessor.TestTone(48000, 0.2, 40, 300);
        Assert.Equal(9600, t.Length);
        Assert.All(t, v => Assert.InRange(v, -0.5001f, 0.5001f));
    }
}

public class StickTests
{
    [Fact] public void Centre_is_zero() => Assert.Equal((0, 0), StickProcessor.Apply(new StickConfig(), 128, 128));

    [Fact]
    public void Dead_zone_and_full_deflection()
    {
        var c = new StickConfig { DeadZone = 0.2 };
        Assert.Equal((0, 0), StickProcessor.Apply(c, 140, 128));           // 0.094 < dz
        var (x, y) = StickProcessor.Apply(c, 255, 128);
        Assert.Equal(1, x, 3); Assert.Equal(0, y, 3);
        var (hx, _) = StickProcessor.Apply(c, 204, 128);                    // 0.598 -> (0.598-0.2)/0.8
        Assert.Equal(0.498, hx, 2);
    }

    [Fact] public void Raw_y_down_maps_to_negative_y() => Assert.True(StickProcessor.Apply(new StickConfig(), 128, 255).Y < -0.9);

    [Fact]
    public void Inversion_and_saturation()
    {
        var c = new StickConfig { InvertX = true, InvertY = true, MaxZone = 0.5, DeadZone = 0 };
        var (x, y) = StickProcessor.Apply(c, 192, 64); // ~0.5 right, ~0.5 up
        Assert.True(x < -0.69 && y < -0.69);           // saturated diagonal, both inverted
    }

    [Fact]
    public void Anti_dead_zone_lifts_small_values()
    {
        var c = new StickConfig { DeadZone = 0.05, AntiDeadZone = 0.3 };
        Assert.True(StickProcessor.Apply(c, 137, 128).X >= 0.3);
    }

    [Fact]
    public void Curves_reduce_mid_range()
    {
        double lin = StickProcessor.Apply(new StickConfig { DeadZone = 0 }, 192, 128).X;
        double quad = StickProcessor.Apply(new StickConfig { DeadZone = 0, Curve = ResponseCurve.Quadratic }, 192, 128).X;
        double ease = StickProcessor.Apply(new StickConfig { DeadZone = 0, Curve = ResponseCurve.EaseOut }, 192, 128).X;
        Assert.True(quad < lin && lin < ease);
    }

    [Fact]
    public void Calibration_removes_drift()
    {
        var cal = new StickCalibrator();
        var rnd = new Random(1);
        for (int i = 0; i < 200; i++) cal.AddSample((byte)(140 + rnd.Next(-1, 2)), (byte)(120 + rnd.Next(-1, 2)));
        var (cx, cy, dz) = cal.Compute();
        Assert.Equal(12 / 127.0, cx, 2); Assert.Equal(-8 / 127.0, cy, 2);
        var c = new StickConfig { CenterX = cx, CenterY = cy, DeadZone = dz };
        Assert.Equal((0, 0), StickProcessor.Apply(c, 140, 120)); // drifting rest position now reads as centre
        Assert.Equal((0, 0), StickProcessor.Apply(c, 141, 119));
    }

    [Fact]
    public void Trigger_dead_zone()
    {
        var c = new TriggerAxisConfig { DeadZone = 0.1, MaxZone = 0.9 };
        Assert.Equal(0, StickProcessor.ApplyTrigger(c, 20));
        Assert.Equal(1, StickProcessor.ApplyTrigger(c, 250));
    }
}

public class MappingTests
{
    private static InputState Press(params PadButtons[] b) { var s = InputState.Neutral; foreach (var x in b) s.Buttons |= x; return s; }
    private static ButtonBinding Bind(ActionType t, string target) => new() { Press = [new MapAction(t, target)] };

    [Fact]
    public void Unbound_buttons_pass_through()
    {
        var f = new MappingEngine().Process(Press(PadButtons.Cross, PadButtons.DpadLeft, PadButtons.Mute), 4);
        Assert.Equal(PadButtons.Cross | PadButtons.DpadLeft, f.Buttons); // Mute does not exist on virtual pads
        Assert.Empty(f.Keys);
    }

    [Fact]
    public void Button_remapped_to_other_button_and_key()
    {
        var cfg = new MappingConfig();
        cfg.Bindings["Cross"] = new ButtonBinding { Press = [new(ActionType.PadButton, "Circle"), new(ActionType.Key, "Space")] };
        var f = new MappingEngine(cfg).Process(Press(PadButtons.Cross), 4);
        Assert.Equal(PadButtons.Circle, f.Buttons);
        Assert.Contains("Space", f.Keys);
    }

    [Fact]
    public void Disabled_binding_swallows_button()
    {
        var cfg = new MappingConfig(); cfg.Bindings["Circle"] = new ButtonBinding();
        Assert.Equal(PadButtons.None, new MappingEngine(cfg).Process(Press(PadButtons.Circle), 4).Buttons);
    }

    [Fact]
    public void Toggle_latches_between_presses()
    {
        var cfg = new MappingConfig(); cfg.Bindings["L3"] = new ButtonBinding { Press = [new(ActionType.Key, "Shift")], Mode = PressMode.Toggle };
        var e = new MappingEngine(cfg);
        Assert.Contains("Shift", e.Process(Press(PadButtons.L3), 4).Keys);
        Assert.Contains("Shift", e.Process(InputState.Neutral, 4).Keys);
        Assert.Empty(e.Process(Press(PadButtons.L3), 4).Keys);
        Assert.Empty(e.Process(InputState.Neutral, 4).Keys);
    }

    [Fact]
    public void Turbo_pulses_at_configured_rate()
    {
        var cfg = new MappingConfig(); cfg.Bindings["Square"] = new ButtonBinding { Press = [new(ActionType.PadButton, "Square")], Mode = PressMode.Turbo, TurboHz = 10 };
        var e = new MappingEngine(cfg);
        int transitions = 0; bool prev = false;
        for (int i = 0; i < 250; i++) // 1 second at 4 ms
        {
            bool on = e.Process(Press(PadButtons.Square), 4).Buttons.HasFlag(PadButtons.Square);
            if (on && !prev) transitions++;
            prev = on;
        }
        Assert.InRange(transitions, 9, 11);
    }

    [Fact]
    public void Short_long_and_double_press_are_distinguished()
    {
        var cfg = new MappingConfig();
        cfg.Bindings["Triangle"] = new ButtonBinding
        {
            Press = [new(ActionType.Key, "A")], Hold = [new(ActionType.Key, "B")], DoubleTap = [new(ActionType.Key, "C")], HoldMs = 300, DoubleTapMs = 200,
        };
        var e = new MappingEngine(cfg);
        HashSet<string> Run(InputState s, int ms) { var all = new HashSet<string>(); for (int t = 0; t < ms; t += 4) all.UnionWith(e.Process(s, 4).Keys); return all; }

        // long press -> only B, while held
        Assert.Equal(["B"], Run(Press(PadButtons.Triangle), 500));
        Assert.Empty(Run(InputState.Neutral, 400));
        // short press -> A, after the double-tap window expires
        Assert.Empty(Run(Press(PadButtons.Triangle), 80));
        Assert.Equal(["A"], Run(InputState.Neutral, 400));
        // double press -> only C
        Run(Press(PadButtons.Triangle), 60); Run(InputState.Neutral, 60);
        var keys = Run(Press(PadButtons.Triangle), 60); keys.UnionWith(Run(InputState.Neutral, 400));
        Assert.Equal(["C"], keys);
    }

    [Fact]
    public void Chord_suppresses_member_bindings()
    {
        var cfg = new MappingConfig();
        cfg.Chords.Add(new ChordBinding { Buttons = ["L1", "R1"], Actions = [new(ActionType.Key, "F5")] });
        var e = new MappingEngine(cfg);
        var f = e.Process(Press(PadButtons.L1, PadButtons.R1, PadButtons.Cross), 4);
        Assert.Contains("F5", f.Keys);
        Assert.Equal(PadButtons.Cross, f.Buttons);
        Assert.Equal(PadButtons.L1, e.Process(Press(PadButtons.L1), 4).Buttons);
    }

    [Fact]
    public void Shift_layer_selects_alternate_actions()
    {
        var cfg = new MappingConfig { ShiftButton = "L1" };
        cfg.Bindings["Cross"] = new ButtonBinding { Press = [new(ActionType.Key, "A")], Shift = [new(ActionType.Key, "B")] };
        var e = new MappingEngine(cfg);
        Assert.Equal(["A"], e.Process(Press(PadButtons.Cross), 4).Keys);
        var f = e.Process(Press(PadButtons.Cross, PadButtons.L1), 4);
        Assert.Equal(["B"], f.Keys);
        Assert.False(f.Buttons.HasFlag(PadButtons.L1)); // the modifier itself is consumed
    }

    [Fact]
    public void Macro_runs_timed_sequence()
    {
        var cfg = new MappingConfig();
        cfg.Macros.Add(new Macro
        {
            Name = "combo",
            Steps =
            [
                new() { Kind = MacroStepKind.Tap, Action = new(ActionType.Key, "A"), Ms = 40 },
                new() { Kind = MacroStepKind.Delay, Ms = 100 },
                new() { Kind = MacroStepKind.Down, Action = new(ActionType.Key, "B") },
                new() { Kind = MacroStepKind.Delay, Ms = 40 },
                new() { Kind = MacroStepKind.Up, Action = new(ActionType.Key, "B") },
            ],
        });
        cfg.Bindings["R3"] = Bind(ActionType.Macro, "combo");
        var e = new MappingEngine(cfg);
        var timeline = new List<string>();
        for (int t = 0; t < 300; t += 4)
        {
            var f = e.Process(t < 20 ? Press(PadButtons.R3) : InputState.Neutral, 4);
            timeline.Add(string.Join("", f.Keys.OrderBy(k => k)));
        }
        string joined = string.Join(",", timeline.Select(k => k.Length == 0 ? "-" : k).Distinct());
        Assert.Equal("A,-,B", joined[..Math.Min(5, joined.Length)]);
        Assert.Equal("", timeline[^1]); // everything released at the end
    }

    [Fact]
    public void Special_actions_fire_once_per_press()
    {
        var cfg = new MappingConfig(); cfg.Bindings["Mute"] = Bind(ActionType.Special, SpecialActions.ProfileNext);
        var e = new MappingEngine(cfg);
        Assert.Equal([SpecialActions.ProfileNext], e.Process(Press(PadButtons.Mute), 4).Specials);
        Assert.Empty(e.Process(Press(PadButtons.Mute), 4).Specials);
    }

    [Fact]
    public void Touchpad_four_regions_map_independently()
    {
        var cfg = new MappingConfig(); cfg.Touchpad.Mode = TouchpadMode.FourRegions;
        cfg.Bindings[Sources.TouchBottomRight] = Bind(ActionType.Key, "M");
        cfg.Bindings[Sources.TouchTopLeft] = Bind(ActionType.Key, "I");
        var e = new MappingEngine(cfg);
        var s = Press(PadButtons.Touchpad); s.Touch0 = new TouchPoint(true, 1, 1700, 900);
        var f = e.Process(s, 4);
        Assert.Equal(["M"], f.Keys);
        Assert.False(f.Buttons.HasFlag(PadButtons.Touchpad));
        s.Touch0 = new TouchPoint(true, 1, 100, 100);
        Assert.Equal(["I"], e.Process(s, 4).Keys);
    }

    [Fact]
    public void Touchpad_mouse_moves_clicks_and_scrolls()
    {
        var cfg = new MappingConfig(); cfg.Touchpad.Mode = TouchpadMode.Mouse;
        var e = new MappingEngine(cfg);
        var s = InputState.Neutral; s.Touch0 = new TouchPoint(true, 1, 500, 500);
        e.Process(s, 4);
        s.Touch0 = new TouchPoint(true, 1, 600, 450);
        var f = e.Process(s, 4);
        Assert.True(f.MouseDx > 0 && f.MouseDy < 0);
        s.Buttons = PadButtons.Touchpad;
        Assert.Equal(MouseButtons.Left, e.Process(s, 4).Mouse);
        // two fingers: scroll + right click
        s.Touch1 = new TouchPoint(true, 2, 900, 500);
        e.Process(s, 4);
        s.Touch0 = new TouchPoint(true, 1, 600, 150); s.Touch1 = new TouchPoint(true, 2, 900, 200);
        var f2 = e.Process(s, 4);
        Assert.True(f2.Wheel > 0); Assert.Equal(MouseButtons.Right, f2.Mouse);
    }

    [Fact]
    public void Tap_to_click_emits_left_click()
    {
        var cfg = new MappingConfig(); cfg.Touchpad.Mode = TouchpadMode.Mouse;
        var e = new MappingEngine(cfg);
        var s = InputState.Neutral; s.Touch0 = new TouchPoint(true, 1, 500, 500);
        for (int i = 0; i < 10; i++) e.Process(s, 4);
        s.Touch0 = new TouchPoint(false, 1, 500, 500);
        e.Process(s, 4);
        Assert.Equal(MouseButtons.Left, e.Process(s, 4).Mouse);
    }

    [Fact]
    public void Swipe_gesture_raises_source()
    {
        var cfg = new MappingConfig(); cfg.Touchpad.SwipeGestures = true;
        cfg.Bindings[Sources.SwipeRight] = Bind(ActionType.Key, "Right");
        var e = new MappingEngine(cfg);
        var s = InputState.Neutral; bool fired = false;
        for (int x = 200; x <= 1400; x += 100) { s.Touch0 = new TouchPoint(true, 1, x, 500); e.Process(s, 8); }
        s.Touch0 = new TouchPoint(false, 1, 1400, 500);
        for (int i = 0; i < 10; i++) fired |= e.Process(s, 8).Keys.Contains("Right");
        Assert.True(fired);
    }

    [Fact]
    public void Gyro_as_mouse_respects_dead_zone_and_activation()
    {
        var cfg = new MappingConfig();
        cfg.Gyro = new GyroConfig { Mode = GyroMode.Mouse, SensitivityX = 10, SensitivityY = 10, Smoothing = 0, DeadZone = 2, ActivationButton = "L2" };
        var e = new MappingEngine(cfg);
        var s = InputState.Neutral;
        s.GyroY = -20480; // -20 deg/s yaw -> move right
        Assert.Equal(0, e.Process(s, 100).MouseDx);       // activation button not held
        s.Buttons = PadButtons.L2;
        var f = e.Process(s, 100);
        Assert.Equal(20, f.MouseDx);                       // 20 deg/s * 10 px/deg * 0.1 s
        s.GyroY = -1024;                                   // 1 deg/s: inside dead zone
        Assert.Equal(0, e.Process(s, 100).MouseDx);
    }

    [Fact]
    public void Gyro_as_stick_adds_deflection()
    {
        var cfg = new MappingConfig(); cfg.Gyro = new GyroConfig { Mode = GyroMode.RightStick, Smoothing = 0, SensitivityX = 6 };
        var s = InputState.Neutral; s.GyroY = -30000;
        Assert.True(new MappingEngine(cfg).Process(s, 4).RightX > 0.1);
    }

    [Fact]
    public void Shake_gesture_triggers_binding()
    {
        var cfg = new MappingConfig(); cfg.Gyro.ShakeGesture = true; cfg.Bindings[Sources.GyroShake] = Bind(ActionType.Key, "R");
        var e = new MappingEngine(cfg);
        var s = InputState.Neutral; s.AccelY = 8192;
        Assert.Empty(e.Process(s, 4).Keys);
        s.AccelY = 8192 * 3;
        Assert.Contains("R", e.Process(s, 4).Keys);
    }

    [Fact]
    public void Stick_as_mouse_and_keys()
    {
        var cfg = new MappingConfig(); cfg.RightStick.Output = StickOutput.Mouse; cfg.RightStick.MouseSpeed = 1000; cfg.LeftStick.Output = StickOutput.Keys;
        var s = InputState.Neutral; s.RightX = 255; s.LeftY = 0;
        var f = new MappingEngine(cfg).Process(s, 100);
        Assert.Equal(100, f.MouseDx); Assert.Equal(0, f.RightX);
        Assert.Equal(["W"], f.Keys);
    }

    [Fact]
    public void Triggers_report_analog_and_digital()
    {
        var s = InputState.Neutral; s.L2 = 128;
        Assert.Equal(128 / 255.0, new MappingEngine().Process(s, 4).L2, 3);
        var cfg = new MappingConfig(); cfg.Bindings["Cross"] = Bind(ActionType.PadButton, "R2");
        Assert.Equal(1, new MappingEngine(cfg).Process(Press(PadButtons.Cross), 4).R2);
    }

    private sealed class FakeInjector : IInputInjector
    {
        public List<string> Log = [];
        public bool IsAvailable => true;
        public void Key(string k, bool down) => Log.Add((down ? "+" : "-") + k);
        public void MouseMove(int dx, int dy) => Log.Add($"move {dx},{dy}");
        public void MouseButton(MouseButtons b, bool down) => Log.Add((down ? "+" : "-") + b);
        public void Wheel(int n) => Log.Add("wheel " + n);
    }

    [Fact]
    public void Output_applier_sends_each_transition_once_and_releases_all()
    {
        var inj = new FakeInjector(); var a = new OutputApplier(inj);
        var f = new OutputFrame(); f.Keys.Add("A"); f.Mouse = MouseButtons.Left; f.MouseDx = 3;
        a.Apply(f); a.Apply(f);
        Assert.Equal(["+A", "+Left", "move 3,0", "move 3,0"], inj.Log);
        inj.Log.Clear();
        a.ReleaseAll();
        Assert.Equal(["-A", "-Left"], inj.Log);
    }
}
