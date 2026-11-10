using System.Reflection;
using System.Runtime.Loader;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Runtime;
using DualSenseUltimate.Core.Storage;
using DualSenseUltimate.Plugins;

namespace DualSenseUltimate.Core.Plugins;

public sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Assembly file name inside the plugin folder.</summary>
    public string Assembly { get; set; } = "";
    public List<PluginPermissions> Permissions { get; set; } = [];
    public PluginPermissions Granted => Permissions.Aggregate(PluginPermissions.None, (a, b) => a | b);
}

public sealed class PluginEntry
{
    public required PluginManifest Manifest { get; init; }
    public required string Directory { get; init; }
    public bool Loaded { get; internal set; }
    public string? Error { get; internal set; }
    internal IDsuPlugin? Instance; internal AssemblyLoadContext? Context; internal PluginHost? Host;
}

/// <summary>Discovers plugins in the plugins folder and runs the ones the user enabled.
/// Each plugin gets its own collectible load context and a host object that enforces the permissions
/// declared in its manifest. NOTE: .NET offers no in-process security sandbox, so permissions limit
/// what a plugin can do through the host API, not what its code can do on the machine. Plugins are
/// therefore never loaded unless the user explicitly enables them.</summary>
public sealed class PluginManager
{
    private readonly AppRuntime _runtime;
    private readonly List<PluginEntry> _entries = new();
    public event Action? Changed;

    public PluginManager(AppRuntime runtime) { _runtime = runtime; }

    public IReadOnlyList<PluginEntry> Entries { get { lock (_entries) return _entries.ToList(); } }

    public void Discover()
    {
        lock (_entries)
        {
            foreach (var dir in Directory.GetDirectories(_runtime.Paths.Plugins))
            {
                string manifestFile = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestFile) || _entries.Any(e => e.Directory == dir)) continue;
                try
                {
                    var m = JsonFile.Deserialize<PluginManifest>(File.ReadAllText(manifestFile));
                    if (m == null || m.Id.Length == 0 || m.Id.Length > 64 || !m.Id.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')) continue;
                    // The assembly must live inside the plugin folder.
                    if (m.Assembly.Length == 0 || m.Assembly.IndexOfAny(['/', '\\', ':']) >= 0 || m.Assembly.Contains("..") || !m.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                    if (_entries.Any(e => e.Manifest.Id == m.Id)) continue;
                    _entries.Add(new PluginEntry { Manifest = m, Directory = dir });
                }
                catch (Exception ex) { Log.Warn($"invalid plugin manifest in {dir}: {ex.Message}"); }
            }
        }
        Changed?.Invoke();
    }

    public void LoadEnabled()
    {
        Discover();
        foreach (var e in Entries.Where(e => _runtime.Settings.EnabledPlugins.Contains(e.Manifest.Id))) Load(e);
    }

    public void SetEnabled(PluginEntry e, bool enabled)
    {
        var list = _runtime.Settings.EnabledPlugins;
        if (enabled && !list.Contains(e.Manifest.Id)) list.Add(e.Manifest.Id);
        if (!enabled) list.Remove(e.Manifest.Id);
        _runtime.SaveSettings();
        if (enabled) Load(e); else Unload(e);
        Changed?.Invoke();
    }

    public bool IsEnabled(PluginEntry e) => _runtime.Settings.EnabledPlugins.Contains(e.Manifest.Id);

    private void Load(PluginEntry e)
    {
        if (e.Loaded) return;
        try
        {
            string path = Path.Combine(e.Directory, e.Manifest.Assembly);
            var ctx = new PluginLoadContext(path);
            var asm = ctx.LoadFromAssemblyPath(path);
            var type = asm.GetTypes().FirstOrDefault(t => typeof(IDsuPlugin).IsAssignableFrom(t) && !t.IsAbstract)
                ?? throw new InvalidOperationException("El ensamblado no contiene ninguna clase IDsuPlugin.");
            var instance = (IDsuPlugin)Activator.CreateInstance(type)!;
            var host = new PluginHost(_runtime, e.Manifest);
            instance.Start(host);
            e.Instance = instance; e.Context = ctx; e.Host = host; e.Loaded = true; e.Error = null;
            _runtime.Activity.Add($"Plugin cargado: {e.Manifest.Name} {e.Manifest.Version}");
        }
        catch (Exception ex)
        {
            e.Error = ex.Message; e.Loaded = false;
            Log.Error($"plugin {e.Manifest.Id} failed to load", ex);
        }
    }

    private void Unload(PluginEntry e)
    {
        if (!e.Loaded) return;
        try { e.Instance?.Stop(); } catch (Exception ex) { Log.Warn($"plugin {e.Manifest.Id} stop failed: {ex.Message}"); }
        e.Host?.Dispose();
        try { e.Context?.Unload(); } catch { }
        e.Instance = null; e.Context = null; e.Host = null; e.Loaded = false;
    }

    public void UnloadAll() { foreach (var e in Entries) Unload(e); }

    private sealed class PluginLoadContext(string mainAssembly) : AssemblyLoadContext("plugin:" + Path.GetFileNameWithoutExtension(mainAssembly), isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssembly);
        protected override Assembly? Load(AssemblyName name)
        {
            // Share the abstractions assembly with the host so interface identities match.
            if (name.Name == typeof(IDsuPlugin).Assembly.GetName().Name) return null;
            string? path = _resolver.ResolveAssemblyToPath(name);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }
    }
}

/// <summary>Per-plugin facade over the runtime that checks permissions on every call.</summary>
internal sealed class PluginHost : IDsuHost, IDisposable
{
    private readonly AppRuntime _rt; private readonly PluginManifest _m;
    private readonly List<IDisposable> _subs = new();
    public PluginHost(AppRuntime rt, PluginManifest m) { _rt = rt; _m = m; }

    private void Require(PluginPermissions p)
    {
        if ((_m.Granted & p) != p) throw new UnauthorizedAccessException($"El plugin '{_m.Id}' no declaró el permiso {p}.");
    }

    public IReadOnlyList<ControllerInfo> Controllers
    {
        get
        {
            Require(PluginPermissions.ReadState);
            return _rt.Sessions.Select(s => new ControllerInfo(s.Id, _rt.DisplayName(s), s.Device.Model.ToString(), s.Device.Connection.ToString(),
                s.Device.State.BatteryPercent, s.Device.State.BatteryStatus.ToString(), s.Profile.Name, s.Device.IsSimulated)).ToList();
        }
    }

    public void SetLightbar(string id, byte r, byte g, byte b, int durationMs = 0) { Require(PluginPermissions.ControlOutput); _rt.SetLightbarOverride(id, r, g, b, durationMs); }
    public void ClearOverrides(string id) { Require(PluginPermissions.ControlOutput); _rt.ClearOverrides(id); }
    public void PlayHaptic(string id, string patternName) { Require(PluginPermissions.ControlOutput); _rt.PlayHapticByName(id, patternName); }
    public void Rumble(string id, byte left, byte right, int durationMs) { Require(PluginPermissions.ControlOutput); _rt.Session(id)?.Rumble(left, right, durationMs); }
    public void SetTriggerPreset(string id, string side, string preset) { Require(PluginPermissions.ControlOutput); _rt.SetTriggerPreset(id, side, preset); }
    public bool ApplyProfile(string id, string profileName) { Require(PluginPermissions.Profiles); return _rt.ApplyProfileByName(id, profileName); }
    public void Notify(string title) { Require(PluginPermissions.Events); _rt.Notify(title); }

    public IDisposable Subscribe(Action<DsuEvent> handler)
    {
        Require(PluginPermissions.Events);
        var sub = _rt.Events.Subscribe(e =>
        {
            try { handler(EventMapper.ToDsu(e)); } catch (Exception ex) { Diagnostics.Log.Warn($"plugin {_m.Id} handler failed: {ex.Message}"); }
        });
        lock (_subs) _subs.Add(sub);
        return sub;
    }

    public void Log(string message) => Diagnostics.Log.Info($"[plugin:{_m.Id}] {message}");

    public string DataDirectory
    {
        get { string d = Path.Combine(_rt.Paths.Plugins, ".data", _m.Id); Directory.CreateDirectory(d); return d; }
    }

    public void Dispose() { lock (_subs) { foreach (var s in _subs) s.Dispose(); _subs.Clear(); } }
}

public static class EventMapper
{
    public static DsuEvent ToDsu(AppEvent e) => e switch
    {
        ControllerConnectedEvent c => new DsuEvent("controller.connected", c.Id, $"{c.Model}/{c.Connection}", e.Time),
        ControllerDisconnectedEvent c => new DsuEvent("controller.disconnected", c.Id, null, e.Time),
        BatteryChangedEvent b => new DsuEvent("battery.changed", b.Id, $"{b.Percent}/{b.Status}", e.Time),
        ProcessStartedEvent p => new DsuEvent("process.started", null, p.Name, e.Time),
        ProcessExitedEvent p => new DsuEvent("process.exited", null, p.Name, e.Time),
        NotificationEvent n => new DsuEvent("notification", null, n.Title, e.Time),
        ProfileAppliedEvent p => new DsuEvent("profile.applied", p.Id, p.ProfileName, e.Time),
        DeviceHealthEvent h => new DsuEvent("controller.health", h.Id, h.Health, e.Time),
        _ => new DsuEvent(e.GetType().Name, e.ControllerId, null, e.Time),
    };
}
