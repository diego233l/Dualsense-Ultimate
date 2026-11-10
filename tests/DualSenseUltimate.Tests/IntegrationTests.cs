using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DualSenseUltimate.Api;
using DualSenseUltimate.Core.Devices;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Runtime;
using DualSenseUltimate.Core.Virtual;
using DualSenseUltimate.Platform;

namespace DualSenseUltimate.Tests;

/// <summary>End-to-end tests of the whole stack (device manager, sessions, profiles, automation, API)
/// against simulated controllers that speak the real wire protocol. These validate the software, not
/// physical hardware.</summary>
public class IntegrationTests
{
    private sealed class RecordingInjector : IInputInjector
    {
        public List<string> Log = [];
        public bool IsAvailable => true;
        public void Key(string k, bool down) { lock (Log) Log.Add((down ? "+" : "-") + k); }
        public void MouseMove(int dx, int dy) { }
        public void MouseButton(MouseButtons b, bool down) { }
        public void Wheel(int n) { }
    }

    private sealed class Harness : IDisposable
    {
        public TempDir Tmp = new();
        public FakeProcesses Processes = new();
        public RecordingInjector Injector = new();
        public AppRuntime Runtime;
        public Harness()
        {
            Runtime = new AppRuntime(Tmp.Paths, new PlatformServices { ProcessSource = Processes, Injector = Injector });
            Runtime.Start();
        }
        public SimulatedController Plug(string name, ConnectionType conn, string mac, int pid = SonyIds.DualSense)
        {
            var c = new SimulatedController(name, conn, pid, mac, autoReportHz: 100);
            Runtime.Simulator.Plug(c);
            Runtime.Devices.Scan();
            return c;
        }
        public void Dispose() { Runtime.Dispose(); Tmp.Dispose(); }
    }

    private static void WaitFor(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) { if (condition()) return; Thread.Sleep(15); }
        Assert.Fail("timed out waiting for: " + what);
    }

    [Theory]
    [InlineData(ConnectionType.Usb)]
    [InlineData(ConnectionType.Bluetooth)]
    public void Controller_is_detected_identified_and_driven(ConnectionType conn)
    {
        using var h = new Harness();
        var p = h.Runtime.Profiles.Profiles[0];
        p.Lighting.Color = "#112233"; p.Lighting.PlayerLeds = Core.Lighting.PlayerLedMode.Player2;
        p.R2 = new TriggerEffect { Type = TriggerEffectType.Weapon, Start = 2, End = 5, Strength = 8 };
        h.Runtime.Profiles.Save(p);
        var sim = h.Plug("a", conn, "AA:BB:CC:DD:EE:01");
        sim.Input.BatteryPercent = 45; sim.Input.BatteryStatus = BatteryStatus.Charging;

        WaitFor(() => h.Runtime.Sessions.Count == 1, "session");
        var s = h.Runtime.Sessions[0];
        Assert.Equal("AA:BB:CC:DD:EE:01", s.Id);                    // identity from feature report 0x09
        Assert.Equal(conn, s.Device.Connection);
        Assert.NotNull(s.Device.FirmwareVersion);
        WaitFor(() => s.Device.State.BatteryPercent == 45 && s.Device.State.BatteryStatus == BatteryStatus.Charging, "battery");
        WaitFor(() => sim.LastOutput is { Red: 0x11, Green: 0x22, Blue: 0x33 }, "lightbar written");
        Assert.Equal(0b01010, sim.LastOutput!.PlayerLeds);
        Assert.Equal(new byte[] { 0x25, 0x24, 0x00, 0x07 }, sim.LastOutput.RightTrigger[..4]);
        Assert.Equal(0x05, sim.LastOutput.LeftTrigger[0]);
    }

    [Fact]
    public void Hot_unplug_and_reconnect_are_handled()
    {
        using var h = new Harness();
        var events = new List<string>();
        h.Runtime.Events.Subscribe(e => { lock (events) events.Add(e.GetType().Name); });
        var sim = h.Plug("a", ConnectionType.Usb, "AA:BB:CC:DD:EE:02");
        WaitFor(() => h.Runtime.Sessions.Count == 1, "connect");
        h.Runtime.Simulator.Unplug(sim);
        WaitFor(() => h.Runtime.Sessions.Count == 0, "disconnect");
        h.Plug("a", ConnectionType.Bluetooth, "AA:BB:CC:DD:EE:02");
        WaitFor(() => h.Runtime.Sessions.Count == 1, "reconnect");
        Assert.Equal(ConnectionType.Bluetooth, h.Runtime.Sessions[0].Device.Connection);
        lock (events)
        {
            Assert.Equal(2, events.Count(e => e == nameof(ControllerConnectedEvent)));
            Assert.Equal(1, events.Count(e => e == nameof(ControllerDisconnectedEvent)));
        }
    }

    [Fact]
    public void Same_controller_on_usb_and_bluetooth_is_not_duplicated()
    {
        using var h = new Harness();
        h.Runtime.Simulator.Plug(new SimulatedController("bt", ConnectionType.Bluetooth, mac: "AA:BB:CC:DD:EE:03", autoReportHz: 50));
        h.Runtime.Simulator.Plug(new SimulatedController("usb", ConnectionType.Usb, mac: "AA:BB:CC:DD:EE:03", autoReportHz: 50));
        h.Runtime.Devices.Scan();
        WaitFor(() => h.Runtime.Sessions.Count >= 1, "session");
        Thread.Sleep(100); h.Runtime.Devices.Scan();
        Assert.Single(h.Runtime.Sessions);
        Assert.Equal(ConnectionType.Usb, h.Runtime.Sessions[0].Device.Connection); // wired wins
        // The rejected second path must never receive a "release" report: it is the same physical controller.
        var bt = h.Runtime.Simulator.Controllers.First(c => c.Connection == ConnectionType.Bluetooth);
        lock (bt.RawWrites) Assert.Empty(bt.RawWrites);
    }

    [Fact]
    public void Multiple_controllers_have_independent_profiles_and_outputs()
    {
        using var h = new Harness();
        var red = new Profile { Name = "Red" }; red.Lighting.Color = "#FF0000";
        var blue = new Profile { Name = "Blue" }; blue.Lighting.Color = "#0000FF"; blue.L2 = new TriggerEffect { Type = TriggerEffectType.Feedback, Strength = 8 };
        h.Runtime.Profiles.Save(red); h.Runtime.Profiles.Save(blue);
        var a = h.Plug("a", ConnectionType.Usb, "AA:00:00:00:00:01");
        var b = h.Plug("b", ConnectionType.Bluetooth, "AA:00:00:00:00:02", SonyIds.DualSenseEdge);
        WaitFor(() => h.Runtime.Sessions.Count == 2, "two sessions");
        h.Runtime.AssignProfile("AA:00:00:00:00:01", red.Id);
        h.Runtime.AssignProfile("AA:00:00:00:00:02", blue.Id);
        WaitFor(() => a.LastOutput is { Red: 255, Blue: 0 } && b.LastOutput is { Red: 0, Blue: 255 }, "independent lightbars");
        Assert.Equal(0x05, a.LastOutput!.LeftTrigger[0]);
        Assert.Equal(0x21, b.LastOutput!.LeftTrigger[0]);
        Assert.Equal(ControllerModel.DualSenseEdge, h.Runtime.Session("AA:00:00:00:00:02")!.Device.Model);
    }

    [Fact]
    public void Game_profile_is_applied_on_launch_and_previous_profile_restored_on_exit()
    {
        using var h = new Harness();
        var game = new Profile { Name = "Game" }; game.Lighting.Color = "#00FF00"; game.Processes.Add("mygame.exe");
        h.Runtime.Profiles.Save(game);
        var sim = h.Plug("a", ConnectionType.Usb, "AA:00:00:00:00:03");
        WaitFor(() => h.Runtime.Sessions.Count == 1, "session");
        var s = h.Runtime.Sessions[0];
        string original = s.Profile.Name;
        h.Processes.Names.Add("MyGame");
        WaitFor(() => s.Profile.Name == "Game" && s.ProfileReason == "game:MyGame", "auto profile", 8000);
        WaitFor(() => sim.LastOutput is { Green: 255 }, "game lighting");
        h.Processes.Names.Remove("MyGame");
        WaitFor(() => s.Profile.Name == original, "restore", 8000);
    }

    [Fact]
    public void Remapping_reaches_the_injector_and_keys_are_released_on_disconnect()
    {
        using var h = new Harness();
        var p = h.Runtime.Profiles.Profiles[0];
        p.Mapping.Bindings["Cross"] = new ButtonBinding { Press = [new(ActionType.Key, "Space")] };
        h.Runtime.Profiles.Save(p);
        var sim = h.Plug("a", ConnectionType.Usb, "AA:00:00:00:00:04");
        WaitFor(() => h.Runtime.Sessions.Count == 1, "session");
        sim.Input.Buttons = PadButtons.Cross;
        WaitFor(() => { lock (h.Injector.Log) return h.Injector.Log.Contains("+Space"); }, "key down");
        h.Runtime.Simulator.Unplug(sim);
        WaitFor(() => { lock (h.Injector.Log) return h.Injector.Log.Contains("-Space"); }, "key released on disconnect");
        lock (h.Injector.Log) Assert.Equal(1, h.Injector.Log.Count(l => l == "+Space"));
    }

    [Fact]
    public void Only_the_first_report_carries_lightbar_setup_and_static_state_is_not_spammed()
    {
        using var h = new Harness();
        var sim = h.Plug("a", ConnectionType.Usb, "AA:00:00:00:00:0A");
        WaitFor(() => sim.LastOutput != null, "output");
        Thread.Sleep(700);
        List<byte[]> writes; lock (sim.RawWrites) writes = sim.RawWrites.ToList();
        Assert.Equal(1, writes.Count(w => w[1 + 41] == 0x02));
        Assert.InRange(writes.Count, 1, 4); // static profile: no continuous rewriting by default
    }

    [Fact]
    public void Blocking_game_lighting_restores_the_profile_lights_within_milliseconds_without_touching_triggers()
    {
        using var h = new Harness();
        var p = h.Runtime.Profiles.Profiles[0];
        p.Lighting.Color = "#00FF00"; p.Emulation.BlockGameLighting = true; p.Emulation.NativeTriggers = true; p.Emulation.NativeRumble = true;
        h.Runtime.Profiles.Save(p);
        var sim = h.Plug("a", ConnectionType.Usb, "AA:00:00:00:00:0C");
        WaitFor(() => sim.LastOutput is { Green: 255, Red: 0 }, "profile lights");
        var game = new OutputState { Red = 255, Blue = 255, Ownership = OutputOwnership.Lightbar | OutputOwnership.RightTrigger,
            RightTrigger = TriggerEffectEncoder.Encode(new TriggerEffect { Type = TriggerEffectType.Weapon, Start = 2, End = 5, Strength = 8 }) };
        for (int i = 0; i < 20; i++)
        {
            sim.Write(DualSenseProtocol.BuildOutput(game, ConnectionType.Usb));   // the "game" sets police lights + a trigger effect
            WaitFor(() => sim.LastOutput is { Green: 255, Red: 0, Blue: 0 }, "lights restored", 100);
        }
        Assert.Equal(0x25, sim.LastOutput!.RightTrigger[0]);                      // the game's trigger effect is untouched
        byte[] last; lock (sim.RawWrites) last = sim.RawWrites[^1];
        Assert.Equal(0, last[1] & 0x0F);                                          // our reports carry lights only
    }

    [Fact]
    public void Without_blocking_a_static_profile_does_not_fight_the_game_over_the_lights()
    {
        using var h = new Harness();
        var p = h.Runtime.Profiles.Profiles[0];
        p.Lighting.Color = "#00FF00"; p.R2 = new TriggerEffect { Type = TriggerEffectType.Feedback, Strength = 4 };
        h.Runtime.Profiles.Save(p);
        var sim = h.Plug("a", ConnectionType.Usb, "AA:00:00:00:00:0D");
        WaitFor(() => sim.LastOutput is { Green: 255 }, "profile lights");
        sim.Write(DualSenseProtocol.BuildOutput(new OutputState { Red = 255, Ownership = OutputOwnership.Lightbar }, ConnectionType.Usb));
        int before; lock (sim.RawWrites) before = sim.RawWrites.Count;
        WaitFor(() => { lock (sim.RawWrites) return sim.RawWrites.Count > before; }, "keep-alive refresh", 4000);
        Assert.True(sim.LastOutput is { Red: 255, Green: 0 }, "the game's colour stays: the refresh did not rewrite the lights");
        byte[] refresh; lock (sim.RawWrites) refresh = sim.RawWrites[^1];
        Assert.Equal(0, refresh[2] & 0x14);                                       // no lightbar / player LED flags
        Assert.NotEqual(0, refresh[1] & 0x04);                                    // triggers are still refreshed
    }

    [Fact]
    public void Hybrid_mode_leaves_triggers_and_rumble_to_the_game()
    {
        using var h = new Harness();
        var p = h.Runtime.Profiles.Profiles[0];
        p.Emulation.NativeTriggers = true; p.Emulation.NativeRumble = true; p.Lighting.Color = "#010203";
        h.Runtime.Profiles.Save(p);
        var sim = h.Plug("a", ConnectionType.Usb, "AA:00:00:00:00:05");
        WaitFor(() => sim.LastOutput is { Red: 1 }, "output");
        byte[] last; lock (sim.RawWrites) last = sim.RawWrites[^1];
        Assert.Equal(0, last[1] & 0x0F); // no rumble / trigger valid flags in the report
        Assert.NotEqual(0, last[2] & 0x04);
    }

    [Fact]
    public void Automation_rule_changes_light_when_battery_is_low()
    {
        using var h = new Harness();
        h.Runtime.Automation.Rules =
        [
            new Core.Automation.AutomationRule
            {
                Trigger = Core.Automation.RuleTrigger.BatteryBelow, Threshold = 20,
                Actions = [new Core.Automation.RuleAction { Type = Core.Automation.RuleActionType.SetLightbar, Value = "#FF0000" }],
            },
        ];
        var sim = h.Plug("a", ConnectionType.Usb, "AA:00:00:00:00:06");
        WaitFor(() => sim.LastOutput != null && sim.LastOutput.Red != 255, "normal light");
        sim.Input.BatteryPercent = 15;
        WaitFor(() => sim.LastOutput is { Red: 255, Green: 0, Blue: 0 }, "red light bar on low battery");
    }

    [Fact]
    public void Closing_the_app_releases_the_controller()
    {
        var h = new Harness();
        var sim = h.Plug("a", ConnectionType.Bluetooth, "AA:00:00:00:00:07");
        WaitFor(() => sim.LastOutput != null, "output");
        h.Runtime.Session("AA:00:00:00:00:07")!.Rumble(255, 255, 10000);
        WaitFor(() => sim.LastOutput!.MotorLeft == 255, "rumble on");
        h.Dispose();
        byte[] last; lock (sim.RawWrites) last = sim.RawWrites[^1];
        Assert.True((last[4] & 0x08) != 0, "release LEDs flag");
        Assert.Equal(0, last[3 + 3]);            // motors stopped
        Assert.Equal(0x05, last[3 + 10]);        // triggers off
        Assert.True(Crc32.Verify(Crc32.SeedOutput, last.AsSpan(0, 78)));
    }

    [Fact]
    public void Stability_soak_many_reports_without_errors()
    {
        using var h = new Harness();
        var sim = new SimulatedController("soak", ConnectionType.Bluetooth, mac: "AA:00:00:00:00:08");
        h.Runtime.Simulator.Plug(sim); h.Runtime.Devices.Scan();
        WaitFor(() => h.Runtime.Sessions.Count == 1, "session");
        var dev = h.Runtime.Sessions[0].Device;
        var rnd = new Random(7);
        for (int i = 0; i < 20000; i++)
        {
            sim.Input.LeftX = (byte)rnd.Next(256); sim.Input.Buttons = (PadButtons)rnd.Next(1 << 19); sim.Input.GyroX = (short)rnd.Next(-30000, 30000);
            sim.Push();
        }
        WaitFor(() => dev.ReportCount >= 20000, "all reports processed", 20000);
        Assert.Equal(0, dev.ErrorCount);
        Assert.Equal(DeviceHealth.Ok, dev.Health);
    }

    [Fact]
    public async Task Local_api_requires_token_and_controls_the_controller()
    {
        using var h = new Harness();
        var sim = h.Plug("a", ConnectionType.Usb, "AA:00:00:00:00:09");
        WaitFor(() => h.Runtime.Sessions.Count == 1, "session");
        await using var api = new LocalApiServer(h.Runtime);
        int port = 40000 + Random.Shared.Next(20000);
        await api.StartAsync(port);
        Assert.True(api.Running, api.Error);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/v1/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/controllers")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/controllers")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", h.Runtime.Settings.ApiToken);

        var list = await http.GetFromJsonAsync<JsonElement>("/api/v1/controllers");
        Assert.Equal("AA:00:00:00:00:09", list[0].GetProperty("id").GetString());
        Assert.True(list[0].GetProperty("simulated").GetBoolean());
        var battery = await http.GetFromJsonAsync<JsonElement>("/api/v1/controllers/first/battery");
        Assert.Equal(75, battery.GetProperty("percent").GetInt32());

        Assert.True((await http.PostAsJsonAsync("/api/v1/controllers/first/lightbar", new { color = "#00FFAA" })).IsSuccessStatusCode);
        WaitFor(() => sim.LastOutput is { Red: 0, Green: 255, Blue: 0xAA }, "API lightbar");
        Assert.True((await http.PostAsJsonAsync("/api/v1/controllers/first/triggers", new { side = "L2", preset = "Pistola" })).IsSuccessStatusCode);
        WaitFor(() => sim.LastOutput!.LeftTrigger[0] == 0x25 && sim.LastOutput.RightTrigger[0] == 0x05, "API trigger");
        Assert.True((await http.PostAsJsonAsync("/api/v1/controllers/first/rumble", new { left = 200, right = 50, durationMs = 2000 })).IsSuccessStatusCode);
        WaitFor(() => sim.LastOutput!.MotorLeft == 200 && sim.LastOutput.MotorRight == 50, "API rumble");
        Assert.True((await http.PostAsJsonAsync("/api/v1/controllers/first/reset", new { })).IsSuccessStatusCode);
        WaitFor(() => sim.LastOutput!.MotorLeft == 0 && sim.LastOutput.LeftTrigger[0] == 0x05, "API reset");

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/controllers/first/triggers", new { preset = "does-not-exist" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/controllers/ZZ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/controllers/first/profile", new { profile = "nope" })).StatusCode);
        var profiles = await http.GetFromJsonAsync<JsonElement>("/api/v1/profiles");
        Assert.True(profiles.GetArrayLength() >= 1);
    }

    private sealed class FakeHider : Core.Virtual.IDeviceHider
    {
        public List<string> Hidden = [], Allowed = [];
        public bool IsAvailable => true;
        public string Status => "fake";
        public bool Hide(string p) { lock (Hidden) Hidden.Add(p); return true; }
        public void Unhide(string p) { lock (Hidden) Hidden.Remove(p); }
        public bool AllowApplication(string e) { lock (Allowed) Allowed.Add(e); return true; }
        public void DisallowApplication(string e) { lock (Allowed) Allowed.Remove(e); }
        public void UnhideAll() { lock (Hidden) Hidden.Clear(); }
    }

    private sealed class PathProcesses : IProcessSource
    {
        public HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyCollection<string> RunningProcessNames() { lock (Names) return Names.ToList(); }
        public IReadOnlyList<string> ExecutablePaths(string n) => [$@"C:\Games\{n}\{n}.exe"];
    }

    [Fact]
    public void Isolation_hides_real_controllers_allows_listed_games_and_is_undone_on_exit()
    {
        using var tmp = new TempDir();
        var hider = new FakeHider(); var procs = new PathProcesses();
        var rt = new AppRuntime(tmp.Paths, new PlatformServices { Hider = hider, ProcessSource = procs });
        var game = new Profile { Name = "Native game" }; game.Processes.Add("elden.exe");
        var emulated = new Profile { Name = "Emulated game" }; emulated.Processes.Add("oldgame.exe"); emulated.Emulation.Mode = Core.Virtual.EmulationMode.Xbox360;
        rt.Profiles.Save(game); rt.Profiles.Save(emulated);
        rt.Start();
        // A "real" (non simulated-flagged) controller: reuse the simulator transport behind a plain descriptor.
        var sim = new SimulatedController("real", ConnectionType.Usb, mac: "AA:00:00:00:00:0B", autoReportHz: 100);
        var real = new RealLookingEnumerator(sim);
        using var devices = new DeviceManager(real);
        ControllerSession? session = null;
        devices.Connected += d => { session = new ControllerSession(d, rt.Platform); session.ApplyProfile(game, "manual"); };
        devices.Scan();
        Assert.NotNull(session);

        Assert.Empty(hider.Hidden);
        session!.SetIsolated(true);
        Assert.Single(hider.Hidden);                              // hidden even though no emulation is active
        Assert.True(session.IsHidden);
        session.SetIsolated(false);
        Assert.Empty(hider.Hidden);

        // Allowed list and automatic whitelisting of native games only.
        rt.Settings.IsolateControllers = true;
        Assert.True(rt.AllowApplication(@"C:\Games\a.exe"));
        Assert.False(rt.AllowApplication("not-an-exe.txt"));
        procs.Names.Add("elden"); procs.Names.Add("oldgame");
        WaitFor(() => { lock (hider.Allowed) return hider.Allowed.Any(a => a.Contains("elden")); }, "native game whitelisted", 8000);
        lock (hider.Allowed) Assert.DoesNotContain(hider.Allowed, a => a.Contains("oldgame"));

        session.SetIsolated(true);
        session.Dispose();
        Assert.Empty(hider.Hidden);                               // never left hidden
        rt.Dispose();
    }

    private sealed class RealLookingEnumerator(SimulatedController sim) : IHidEnumerator
    {
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<HidDeviceDescriptor> Enumerate() => [sim.Descriptor with { IsSimulated = false }];
        public IHidTransport Open(HidDeviceDescriptor d) => sim;
    }

    [Fact]
    public void HidHide_instance_path_conversion()
    {
        Assert.Equal(@"HID\VID_054C&PID_0CE6&MI_03\8&1A2B3C&0&0000",
            HidHideController.ToInstancePath(@"\\?\hid#vid_054c&pid_0ce6&mi_03#8&1a2b3c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
        Assert.Equal(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\9&2F0B&0&0000",
            HidHideController.ToInstancePath(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#9&2f0b&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
        Assert.Null(HidHideController.ToInstancePath("/dev/hidraw3"));
    }
}
