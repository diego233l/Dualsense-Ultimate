using DualSenseUltimate.Core.Automation;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Runtime;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsu-test-" + Guid.NewGuid().ToString("N"));
    public AppPaths Paths { get; }
    public TempDir() { Paths = new AppPaths(Path); }
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class ProfileTests
{
    [Fact]
    public void Store_creates_default_profile_and_persists_changes()
    {
        using var tmp = new TempDir();
        var store = new ProfileStore(tmp.Paths);
        Assert.Single(store.Profiles);
        var p = new Profile { Name = "Racing" }; p.Lighting.Color = "#FF0000"; p.Processes.Add("forza.exe");
        p.R2 = new TriggerEffect { Type = TriggerEffectType.Feedback, Start = 3, Strength = 5 };
        store.Save(p);
        var reloaded = new ProfileStore(tmp.Paths);
        var back = reloaded.GetByName("racing")!;
        Assert.Equal("#FF0000", back.Lighting.Color);
        Assert.Equal(TriggerEffectType.Feedback, back.R2.Type);
        Assert.Equal(["forza.exe"], back.Processes);
    }

    [Fact]
    public void Last_profile_cannot_be_deleted()
    {
        using var tmp = new TempDir();
        var store = new ProfileStore(tmp.Paths);
        Assert.False(store.Delete(store.Profiles[0].Id));
    }

    [Fact]
    public void Export_then_import_round_trips_with_new_id()
    {
        using var tmp = new TempDir();
        var store = new ProfileStore(tmp.Paths);
        var p = new Profile { Name = "Shooter", Author = "me" };
        p.Mapping.Bindings["Cross"] = new ButtonBinding { Press = [new(ActionType.Key, "Space")] };
        store.Save(p);
        var (result, summary) = store.Import(store.Export(p));
        Assert.True(result.Ok); Assert.NotNull(summary);
        var imported = store.Profiles.Single(x => x.Name == "Shooter (importado)");
        Assert.NotEqual(p.Id, imported.Id);
        Assert.Equal("Space", imported.Mapping.Bindings["Cross"].Press[0].Target);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"format\":\"something-else\",\"kind\":\"profile\",\"payload\":{}}")]
    [InlineData("{\"format\":\"dualsense-ultimate\",\"schemaVersion\":999,\"kind\":\"profile\",\"payload\":{}}")]
    [InlineData("{\"format\":\"dualsense-ultimate\",\"kind\":\"executable\",\"payload\":{}}")]
    [InlineData("{\"format\":\"dualsense-ultimate\",\"kind\":\"profile\",\"payload\":[1,2]}")]
    public void Invalid_packages_are_rejected(string json)
    {
        using var tmp = new TempDir();
        var store = new ProfileStore(tmp.Paths);
        var (result, _) = store.Import(json);
        Assert.False(result.Ok);
        Assert.Single(store.Profiles);
    }

    [Fact]
    public void Oversized_import_is_rejected()
    {
        using var tmp = new TempDir();
        Assert.False(new ProfileStore(tmp.Paths).Import(new string(' ', ProfileValidator.MaxImportBytes + 1)).Result.Ok);
    }

    [Fact]
    public void Hostile_profile_is_neutralised()
    {
        const string json = """
        { "format":"dualsense-ultimate", "schemaVersion":1, "kind":"profile", "name":"evil",
          "payload": {
            "id":"../../../windows/system32/evil", "name":"x", "processes":["C:\\Windows\\System32\\cmd.exe","..\\..\\a.exe"],
            "lighting": { "mode":"Static", "color":"javascript:alert(1)", "brightness": 99999, "periodSeconds": -5 },
            "l2": { "type":"Weapon", "start": -100, "end": 4000, "strength": 77 },
            "haptics": { "intensity": 5000 },
            "mapping": {
              "bindings": {
                "Cross": { "press":[ {"type":"Key","target":"cmd.exe /c del *"}, {"type":"Key","target":"Space"}, {"type":"Special","target":"RunProgram"} ], "turboHz": 1e9 },
                "NotAButton": { "press":[ {"type":"Key","target":"A"} ] }
              },
              "leftStick": { "deadZone": 55, "sensitivity": -3 },
              "macros": [ { "name":"m", "steps":[ {"kind":"Tap","action":{"type":"Macro","target":"m"},"ms":1}, {"kind":"Delay","ms":99999999} ] } ]
            }
          } }
        """;
        using var tmp = new TempDir();
        var store = new ProfileStore(tmp.Paths);
        var (result, _) = store.Import(json);
        Assert.True(result.Ok);
        Assert.NotEmpty(result.Warnings);
        var p = store.Profiles.Single(x => x.Name == "x");
        Assert.DoesNotContain("..", p.Id); Assert.DoesNotContain("/", p.Id);
        Assert.Equal(["cmd.exe", "a.exe"], p.Processes);                       // names only, no paths
        Assert.Equal(100, p.Lighting.Brightness); Assert.Equal(0.2, p.Lighting.PeriodSeconds);
        Assert.Equal(100, p.Haptics.Intensity);
        Assert.Equal(8, p.L2.Strength);
        var press = p.Mapping.Bindings["Cross"].Press;
        Assert.Single(press); Assert.Equal("Space", press[0].Target);          // unknown key and unknown special dropped
        Assert.False(p.Mapping.Bindings.ContainsKey("NotAButton"));
        Assert.Equal(30, p.Mapping.Bindings["Cross"].TurboHz);
        Assert.InRange(p.Mapping.LeftStick.DeadZone, 0, 0.9); Assert.InRange(p.Mapping.LeftStick.Sensitivity, 0.1, 5);
        Assert.Single(p.Mapping.Macros[0].Steps);                              // recursive macro call removed
        Assert.Equal(60000, p.Mapping.Macros[0].Steps[0].Ms);
        // Recovery chord is always present when a profile remaps buttons.
        Assert.Contains(p.Mapping.Chords, c => c.Actions.Any(a => a.Target == SpecialActions.ToggleMapping));
        // All files stay inside the profile folder.
        Assert.All(Directory.GetFiles(tmp.Path, "*", SearchOption.AllDirectories), f => Assert.StartsWith(tmp.Path, System.IO.Path.GetFullPath(f)));
    }

    [Fact]
    public void Library_packages_import_into_library()
    {
        using var tmp = new TempDir();
        var store = new ProfileStore(tmp.Paths);
        var trigger = JsonFile.Serialize(SharePackage.Wrap(PackageKinds.Trigger, "Mi gatillo", new TriggerEffect { Type = TriggerEffectType.Bow, Start = 1, End = 6 }));
        var haptic = JsonFile.Serialize(SharePackage.Wrap(PackageKinds.Haptic, "Mi patrón", Core.Haptics.HapticPattern.Make("", 1, (50, 255, 0))));
        Assert.True(store.Import(trigger).Result.Ok); Assert.True(store.Import(haptic).Result.Ok);
        var reloaded = new ProfileStore(tmp.Paths);
        Assert.Equal("Mi gatillo", reloaded.Library.TriggerEffects.Single().Name);
        Assert.Equal("Mi patrón", reloaded.Library.HapticPatterns.Single().Name);
    }

    [Fact]
    public void Corrupt_file_recovers_from_backup()
    {
        using var tmp = new TempDir();
        string file = System.IO.Path.Combine(tmp.Path, "settings.json");
        JsonFile.Save(file, new AppSettings { ApiPort = 1111 });
        JsonFile.Save(file, new AppSettings { ApiPort = 2222 }); // previous version becomes .bak
        File.WriteAllText(file, "{ this is not json");
        Assert.Equal(1111, JsonFile.Load<AppSettings>(file).ApiPort);
        File.Delete(file + ".bak");
        Assert.Equal(new AppSettings().ApiPort, JsonFile.Load<AppSettings>(file).ApiPort); // defaults as last resort
    }

    [Fact]
    public void Backup_and_restore_configuration()
    {
        using var tmp = new TempDir();
        var store = new ProfileStore(tmp.Paths);
        store.Save(new Profile { Name = "Keep" });
        string zip = ConfigBackup.Create(tmp.Paths)!;
        store.Delete(store.GetByName("Keep")!.Id);
        Assert.Null(new ProfileStore(tmp.Paths).GetByName("Keep"));
        Assert.True(ConfigBackup.Restore(tmp.Paths, zip));
        Assert.NotNull(new ProfileStore(tmp.Paths).GetByName("Keep"));
    }
}

public class AutomationTests
{
    private sealed class Target : IAutomationTarget
    {
        public List<string> Calls = [];
        public IReadOnlyList<string> ControllerIds => ["A", "B"];
        public void SetLightbarOverride(string id, byte r, byte g, byte b, int ms) => Calls.Add($"light {id} {r},{g},{b} {ms}");
        public void ClearOverrides(string id) => Calls.Add($"clear {id}");
        public bool ApplyProfileByName(string id, string name) { Calls.Add($"profile {id} {name}"); return true; }
        public void PlayHapticByName(string id, string name) => Calls.Add($"haptic {id} {name}");
        public void StopEffects(string id) => Calls.Add($"stop {id}");
        public void SetTriggerPreset(string id, string side, string preset) => Calls.Add($"trigger {id} {side} {preset}");
    }

    [Fact]
    public void Battery_rule_fires_once_and_rearms_after_charging()
    {
        var t = new Target();
        var e = new AutomationEngine(t)
        {
            Rules = [new AutomationRule { Trigger = RuleTrigger.BatteryBelow, Threshold = 20, CooldownSeconds = 0, Actions = [new RuleAction { Type = RuleActionType.SetLightbar, Value = "#FF0000" }] }],
        };
        e.Handle(new BatteryChangedEvent("A", 25, BatteryStatus.Discharging, BatteryStatus.Discharging));
        Assert.Empty(t.Calls);
        e.Handle(new BatteryChangedEvent("A", 15, BatteryStatus.Discharging, BatteryStatus.Discharging));
        e.Handle(new BatteryChangedEvent("A", 5, BatteryStatus.Discharging, BatteryStatus.Discharging));
        Assert.Equal(["light A 255,0,0 0"], t.Calls);                       // only once, only for controller A
        e.Handle(new BatteryChangedEvent("A", 15, BatteryStatus.Charging, BatteryStatus.Discharging));
        e.Handle(new BatteryChangedEvent("A", 15, BatteryStatus.Discharging, BatteryStatus.Charging));
        Assert.Equal(2, t.Calls.Count);                                      // re-armed by charging
    }

    [Fact]
    public void Process_rule_matches_name_with_or_without_exe_and_applies_to_all_controllers()
    {
        var t = new Target();
        var e = new AutomationEngine(t)
        {
            Rules = [new AutomationRule { Trigger = RuleTrigger.ProcessStarted, Process = "Game.exe", Actions = [new RuleAction { Type = RuleActionType.ApplyProfile, Value = "Racing" }] }],
        };
        e.Handle(new ProcessStartedEvent("other"));
        e.Handle(new ProcessStartedEvent("game"));
        Assert.Equal(["profile A Racing", "profile B Racing"], t.Calls);
    }

    [Fact]
    public void Cooldown_and_disabled_rules_are_respected()
    {
        var t = new Target(); var now = DateTime.UtcNow;
        var e = new AutomationEngine(t)
        {
            Clock = () => now,
            Rules =
            [
                new AutomationRule { Trigger = RuleTrigger.Notification, CooldownSeconds = 10, Actions = [new RuleAction { Type = RuleActionType.PlayHaptic, Value = "Clic" }] },
                new AutomationRule { Trigger = RuleTrigger.Notification, Enabled = false, Actions = [new RuleAction { Type = RuleActionType.StopEffects }] },
            ],
        };
        e.Handle(new NotificationEvent("a")); e.Handle(new NotificationEvent("b"));
        Assert.Equal(2, t.Calls.Count); // one per controller, second event suppressed by cooldown
        now = now.AddSeconds(11);
        e.Handle(new NotificationEvent("c"));
        Assert.Equal(4, t.Calls.Count);
        Assert.DoesNotContain(t.Calls, c => c.StartsWith("stop"));
    }

    [Fact]
    public void Controller_specific_rule_ignores_other_controllers()
    {
        var t = new Target();
        var e = new AutomationEngine(t)
        {
            Rules = [new AutomationRule { Trigger = RuleTrigger.ControllerConnected, ControllerId = "B", Actions = [new RuleAction { Type = RuleActionType.SetTriggerPreset, Value = "Pistola" }] }],
        };
        e.Handle(new ControllerConnectedEvent("A", "DualSense", "Usb"));
        Assert.Empty(t.Calls);
        e.Handle(new ControllerConnectedEvent("B", "DualSense", "Usb"));
        Assert.Equal(["trigger B both Pistola"], t.Calls);
    }

    [Fact]
    public void Process_watcher_reports_only_watched_names()
    {
        var src = new FakeProcesses();
        var w = new ProcessWatcher(src);
        src.Names.Add("explorer");
        Assert.Equal((0, 0), Counts(w.Poll(["game.exe"])));
        src.Names.Add("Game");
        var (started, exited) = w.Poll(["game.exe"]);
        Assert.Equal(["Game"], started); Assert.Empty(exited);
        src.Names.Remove("Game");
        Assert.Equal(["Game"], w.Poll(["game.exe"]).Exited);
        static (int, int) Counts((List<string> s, List<string> e) r) => (r.s.Count, r.e.Count);
    }
}

public sealed class FakeProcesses : IProcessSource
{
    public HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase);
    public string? Foreground;
    public IReadOnlyCollection<string> RunningProcessNames() { lock (Names) return Names.ToList(); }
    public string? ForegroundProcessName() => Foreground;
}
