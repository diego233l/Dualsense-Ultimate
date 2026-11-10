using System.Diagnostics;
using DualSenseUltimate.Core.Automation;
using DualSenseUltimate.Core.Devices;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Haptics;
using DualSenseUltimate.Core.Lighting;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Plugins;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.Core.Runtime;

/// <summary>Composition root shared by the UI, the local API, plugins and the tests.</summary>
public sealed class AppRuntime : IAutomationTarget, IDisposable
{
    private readonly Dictionary<string, ControllerSession> _sessions = new();
    private readonly object _lock = new();
    private readonly ProcessWatcher _watcher;
    private readonly Stopwatch _tickClock = Stopwatch.StartNew();
    private Timer? _tickTimer, _slowTimer;
    private double _lastTickMs;
    private int _ticking, _polling;
    private HapticAudioProcessor? _audioProcessor;
    private bool _audioRunning, _disposed;
    private List<string> _activeGames = new();

    public AppPaths Paths { get; }
    public AppSettings Settings { get; private set; }
    public ProfileStore Profiles { get; }
    public DeviceManager Devices { get; }
    public SimulatedEnumerator Simulator { get; } = new();
    public PlatformServices Platform { get; }
    public EventBus Events { get; } = new();
    public ActivityLog Activity { get; } = new();
    public AutomationEngine Automation { get; }
    public PluginManager Plugins { get; }
    public IReadOnlyList<string> Conflicts { get; private set; } = [];

    /// <summary>Raised (on a worker thread) when the session list changes.</summary>
    public event Action? SessionsChanged;

    public AppRuntime(AppPaths paths, PlatformServices platform, params IHidEnumerator[] enumerators)
    {
        Paths = paths; Platform = platform;
        Log.Initialize(paths.Logs);
        Settings = JsonFile.Load<AppSettings>(paths.SettingsFile);
        if (string.IsNullOrEmpty(Settings.ApiToken)) { Settings.ApiToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(); SaveSettings(); }
        Log.MinLevel = Settings.VerboseLogging ? LogLevel.Debug : LogLevel.Info;
        Profiles = new ProfileStore(paths);
        Profiles.Changed += () => { try { ResolveAllProfiles(); } catch (Exception ex) { Log.Error("profile refresh failed", ex); } };
        Devices = new DeviceManager([.. enumerators, Simulator]);
        Devices.Connected += OnConnected;
        Devices.Disconnected += OnDisconnected;
        _watcher = new ProcessWatcher(platform.ProcessSource);
        Automation = new AutomationEngine(this);
        if (File.Exists(paths.RulesFile)) Automation.Rules = JsonFile.Load<RuleSet>(paths.RulesFile).Rules;
        else { Automation.Rules = AutomationEngine.Defaults(); SaveRules(); }
        Automation.RuleFired += (rule, id) => Activity.Add($"Automatización ejecutada: {rule.Name}");
        Events.Subscribe(Automation.Handle);
        Plugins = new PluginManager(this);
        // A previous crash may have left devices hidden: undo that before anything else.
        try { platform.Hider?.UnhideAll(); } catch (Exception ex) { Log.Warn("unhide on start failed: " + ex.Message); }
    }

    public void Start()
    {
        ConfigBackup.Create(Paths);
        if (Settings.SimulationEnabled) EnableSimulation(true);
        Devices.Start();
        _tickTimer = new Timer(_ => Tick(), null, 16, 16);
        _slowTimer = new Timer(_ => SlowPoll(), null, 500, 2000);
        Plugins.LoadEnabled();
        Activity.Add("DualSense Ultimate iniciado");
    }

    public IReadOnlyList<ControllerSession> Sessions { get { lock (_lock) return _sessions.Values.ToList(); } }
    public ControllerSession? Session(string? id) { lock (_lock) return id != null && _sessions.TryGetValue(id, out var s) ? s : null; }
    public IReadOnlyList<string> ControllerIds { get { lock (_lock) return _sessions.Keys.ToList(); } }

    public string DisplayName(ControllerSession s) =>
        Settings.ControllerNames.TryGetValue(s.Id, out var n) && n.Length > 0 ? n : ModelName(s.Device.Model);

    public static string ModelName(ControllerModel m) => m switch
    {
        ControllerModel.DualSense => "DualSense", ControllerModel.DualSenseEdge => "DualSense Edge", _ => "DualShock 4",
    };

    public void SaveSettings() { try { JsonFile.Save(Paths.SettingsFile, Settings); } catch (Exception ex) { Log.Error("cannot save settings", ex); } }
    public void SaveRules() { try { JsonFile.Save(Paths.RulesFile, new RuleSet { Rules = Automation.Rules.ToList() }); } catch (Exception ex) { Log.Error("cannot save rules", ex); } }

    public void ReloadAfterRestore()
    {
        Settings = JsonFile.Load<AppSettings>(Paths.SettingsFile);
        Profiles.Load();
        if (File.Exists(Paths.RulesFile)) Automation.Rules = JsonFile.Load<RuleSet>(Paths.RulesFile).Rules;
        ResolveAllProfiles();
    }

    // ---- simulation

    public void EnableSimulation(bool on)
    {
        Settings.SimulationEnabled = on; SaveSettings();
        if (on && Simulator.Controllers.Count == 0)
        {
            var sim = new SimulatedController("demo", ConnectionType.Usb, SonyIds.DualSense, "51:4D:55:4C:41:01", autoReportHz: 60);
            Simulator.Plug(sim);
            _simAnim = new Timer(_ => AnimateSim(sim), null, 0, 16);
        }
        else if (!on)
        {
            _simAnim?.Dispose(); _simAnim = null;
            foreach (var c in Simulator.Controllers) Simulator.Unplug(c);
        }
        Devices.Scan();
    }

    private Timer? _simAnim;
    private void AnimateSim(SimulatedController sim)
    {
        // Gentle synthetic motion so live graphs have something to show. Clearly labelled as simulated in the UI.
        double t = _tickClock.Elapsed.TotalSeconds;
        sim.Input.LeftX = (byte)(128 + 90 * Math.Cos(t * 0.9)); sim.Input.LeftY = (byte)(128 + 90 * Math.Sin(t * 0.9));
        sim.Input.RightX = (byte)(128 + 50 * Math.Sin(t * 1.7)); sim.Input.RightY = (byte)(128 + 30 * Math.Cos(t * 2.3));
        sim.Input.L2 = (byte)(127 + 127 * Math.Sin(t * 0.6)); sim.Input.R2 = (byte)(127 + 127 * Math.Cos(t * 0.8));
        sim.Input.Buttons = ((int)t % 4) switch { 0 => PadButtons.Cross, 1 => PadButtons.Circle, 2 => PadButtons.Triangle, _ => PadButtons.Square };
        sim.Input.GyroY = (short)(2000 * Math.Sin(t * 1.3)); sim.Input.GyroX = (short)(1200 * Math.Cos(t * 1.1));
        sim.Input.AccelY = 8192;
    }

    // ---- device lifecycle

    private void OnConnected(ControllerDevice dev)
    {
        var s = new ControllerSession(dev, Platform);
        s.BatteryChanged += e => Events.Publish(e);
        s.SpecialAction += OnSpecial;
        dev.HealthChanged += d => Events.Publish(new DeviceHealthEvent(d.Id, d.Health.ToString()));
        lock (_lock) _sessions[dev.Id] = s;
        ResolveProfile(s);
        Activity.Add($"{DisplayName(s)} conectado por {(dev.Connection == ConnectionType.Usb ? "USB" : "Bluetooth")}{(dev.IsSimulated ? " (SIMULADO)" : "")}");
        Events.Publish(new ControllerConnectedEvent(dev.Id, dev.Model.ToString(), dev.Connection.ToString()));
        SessionsChanged?.Invoke();
        UpdateAudioCapture();
    }

    private void OnDisconnected(ControllerDevice dev)
    {
        ControllerSession? s;
        lock (_lock) { _sessions.Remove(dev.Id, out s); }
        if (s == null) return;
        Activity.Add($"{DisplayName(s)} desconectado");
        s.Dispose();
        Events.Publish(new ControllerDisconnectedEvent(dev.Id));
        SessionsChanged?.Invoke();
        UpdateAudioCapture();
    }

    private void OnSpecial(ControllerSession s, string action)
    {
        var list = Profiles.Profiles.Where(p => p.ControllerId == null || p.ControllerId == s.Id).OrderBy(p => p.Name).ToList();
        if (list.Count == 0) return;
        int i = list.FindIndex(p => p.Id == s.Profile.Id);
        if (action == SpecialActions.ProfileNext) AssignProfile(s.Id, list[(i + 1 + list.Count) % list.Count].Id);
        else if (action == SpecialActions.ProfilePrevious) AssignProfile(s.Id, list[(i - 1 + list.Count) % list.Count].Id);
    }

    // ---- profiles

    /// <summary>Priority: running game profile &gt; manually assigned profile &gt; default profile. When the game
    /// exits the next poll re-resolves, which restores the previous configuration automatically.</summary>
    private void ResolveProfile(ControllerSession s)
    {
        Profile? chosen = null; string reason = "default";
        foreach (var game in _activeGames)
        {
            chosen = Profiles.Profiles.Where(p => p.Processes.Any(x => AutomationEngine.SameProcess(x, game)) && (p.ControllerId == null || p.ControllerId == s.Id))
                .OrderByDescending(p => p.ControllerId != null).FirstOrDefault();
            if (chosen != null) { reason = "game:" + game; break; }
        }
        if (chosen == null && Settings.ControllerProfiles.TryGetValue(s.Id, out var manual) && Profiles.Get(manual) is { } m) { chosen = m; reason = "manual"; }
        chosen ??= Profiles.Get(Settings.DefaultProfileId) ?? Profiles.Profiles.First();
        if (ReferenceEquals(chosen, s.Profile) && s.ProfileReason == reason) return;
        bool changed = chosen.Id != s.Profile.Id || s.ProfileReason != reason;
        s.ApplyProfile(chosen, reason);
        if (changed)
        {
            Activity.Add($"Perfil «{chosen.Name}» aplicado a {DisplayName(s)}" + (reason.StartsWith("game:") ? $" (juego detectado: {reason[5..]})" : ""));
            Events.Publish(new ProfileAppliedEvent(s.Id, chosen.Name, reason));
        }
        UpdateAudioCapture();
    }

    public void ResolveAllProfiles() { foreach (var s in Sessions) ResolveProfile(s); }

    public void AssignProfile(string controllerId, string profileId)
    {
        Settings.ControllerProfiles[controllerId] = profileId; SaveSettings();
        if (Session(controllerId) is { } s) ResolveProfile(s);
    }

    /// <summary>Re-applies the current profile object after the UI edited it in place (live preview).</summary>
    public void RefreshProfile(Profile p)
    {
        foreach (var s in Sessions.Where(s => s.Profile.Id == p.Id)) s.ApplyProfile(p, s.ProfileReason);
        UpdateAudioCapture();
    }

    // ---- periodic work

    private void Tick()
    {
        if (_disposed || Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            double now = _tickClock.Elapsed.TotalMilliseconds, dt = Math.Clamp(now - _lastTickMs, 1, 200);
            _lastTickMs = now;
            foreach (var s in Sessions)
            {
                try { s.Tick(dt); } catch (Exception ex) { Log.Error($"[{s.Id}] tick failed", ex); }
            }
        }
        finally { Interlocked.Exchange(ref _ticking, 0); }
    }

    private void SlowPoll()
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var watched = Profiles.Profiles.SelectMany(p => p.Processes)
                .Concat(Automation.Rules.Where(r => r.Enabled && r.Trigger is RuleTrigger.ProcessStarted or RuleTrigger.ProcessExited).Select(r => r.Process));
            var (started, exited) = _watcher.Poll(watched);
            foreach (var n in started) { Activity.Add($"Aplicación detectada: {n}"); Events.Publish(new ProcessStartedEvent(n)); }
            foreach (var n in exited) { Activity.Add($"Aplicación cerrada: {n}"); Events.Publish(new ProcessExitedEvent(n)); }
            if (started.Count > 0 || exited.Count > 0 || _activeGames.Count != _watcher.Running.Count)
            {
                // Foreground game first when the platform can tell, otherwise any running match.
                string? fg = Platform.ProcessSource.ForegroundProcessName();
                _activeGames = _watcher.Running.OrderByDescending(n => fg != null && AutomationEngine.SameProcess(n, fg)).ToList();
                ResolveAllProfiles();
            }
            Conflicts = Platform.ConflictDetector?.Invoke() ?? [];
        }
        catch (Exception ex) { Log.Error("poll failed", ex); }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    // ---- audio reactive

    private void UpdateAudioCapture()
    {
        var src = Platform.AudioSource;
        if (src is not { IsAvailable: true }) return;
        var sessions = Sessions;
        bool need = sessions.Any(s => s.Profile.Haptics.AudioReactive || s.Profile.Haptics.NativeAudioHaptics || s.Profile.Lighting.Mode == LightingMode.AudioReactive);
        try
        {
            if (need && !_audioRunning) { _audioProcessor = null; src.Samples += OnAudio; src.Start(); _audioRunning = true; }
            else if (!need && _audioRunning)
            {
                src.Samples -= OnAudio; src.Stop(); _audioRunning = false;
                foreach (var s in sessions) { s.AudioLevel = 0; s.SetAudioRumble(0, 0); }
            }
            else if (need) _audioProcessor = null; // settings may have changed: rebuild filters
        }
        catch (Exception ex) { Log.Error("audio capture failed", ex); }
    }

    private void OnAudio(float[] mono, int sampleRate)
    {
        var sessions = Sessions;
        var cfgOwner = sessions.FirstOrDefault(s => s.Profile.Haptics.AudioReactive || s.Profile.Haptics.NativeAudioHaptics) ?? sessions.FirstOrDefault();
        if (cfgOwner == null) return;
        _audioProcessor ??= new HapticAudioProcessor(sampleRate, cfgOwner.Profile.Haptics);
        _audioProcessor.Process(mono);
        var (l, r) = _audioProcessor.EnvelopeToRumble();
        foreach (var s in sessions)
        {
            s.AudioLevel = Math.Clamp(_audioProcessor.Envelope * 2.5, 0, 1);
            if (s.Profile.Haptics.AudioReactive && !s.Profile.Haptics.NativeAudioHaptics) s.SetAudioRumble(l, r); else s.SetAudioRumble(0, 0);
        }
        if (Platform.HapticAudio is { IsAvailable: true } native && sessions.Any(s => s.Profile.Haptics.NativeAudioHaptics))
            native.Play(mono, sampleRate, true, true);
    }

    // ---- IAutomationTarget + helpers shared with API/plugins

    public void SetLightbarOverride(string controllerId, byte r, byte g, byte b, int durationMs) => Session(controllerId)?.SetLightbarOverride(new Rgb(r, g, b), durationMs);
    public void ClearOverrides(string controllerId) => Session(controllerId)?.ClearOverrides();
    public void StopEffects(string controllerId) => Session(controllerId)?.StopEffects();

    public bool ApplyProfileByName(string controllerId, string profileName)
    {
        var p = Profiles.GetByName(profileName) ?? Profiles.Get(profileName);
        if (p == null || Session(controllerId) == null) return false;
        AssignProfile(controllerId, p.Id);
        return true;
    }

    public void PlayHapticByName(string controllerId, string patternName)
    {
        var p = Profiles.AllHapticPatterns.FirstOrDefault(x => x.Name.Equals(patternName, StringComparison.OrdinalIgnoreCase));
        if (p != null) Session(controllerId)?.PlayHaptic(p);
    }

    public void SetTriggerPreset(string controllerId, string side, string presetName)
    {
        var e = Profiles.AllTriggerEffects.FirstOrDefault(x => x.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (e == null) return;
        bool both = side.Equals("both", StringComparison.OrdinalIgnoreCase);
        Session(controllerId)?.SetTriggerOverride(both || side.Equals("L2", StringComparison.OrdinalIgnoreCase), both || side.Equals("R2", StringComparison.OrdinalIgnoreCase), e);
    }

    /// <summary>Entry point for notification integrations (API, plugins).</summary>
    public void Notify(string title)
    {
        title = title.Length > 200 ? title[..200] : title;
        foreach (var s in Sessions) if (s.Profile.Lighting.NotificationFlash) s.NotifyFlash();
        Activity.Add("Notificación: " + title);
        Events.Publish(new NotificationEvent(title));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tickTimer?.Dispose(); _slowTimer?.Dispose(); _simAnim?.Dispose();
        try { Plugins.UnloadAll(); } catch { }
        try { if (_audioRunning) Platform.AudioSource?.Stop(); } catch { }
        foreach (var s in Sessions) { try { s.Dispose(); } catch { } }
        Devices.Dispose();
        try { Platform.Hider?.UnhideAll(); } catch { }
        try { Platform.HapticAudio?.Dispose(); Platform.AudioSource?.Dispose(); } catch { }
    }
}
