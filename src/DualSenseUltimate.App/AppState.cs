using Avalonia.Threading;
using DualSenseUltimate.Api;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Runtime;
using DualSenseUltimate.Core.Storage;
using DualSenseUltimate.Platform;

namespace DualSenseUltimate.App;

/// <summary>Application-wide state shared by every page: the runtime, the selected controller and the
/// profile being edited. Pages bind straight to the profile objects; a low-frequency change detector
/// applies edits live to the hardware and saves them (debounced), so no per-control plumbing is needed.</summary>
public static class AppState
{
    private static string _lastJson = "";
    private static DateTime _dirtySince = DateTime.MaxValue;
    private static bool _shutdown;

    public static AppRuntime Runtime { get; private set; } = null!;
    public static LocalApiServer Api { get; private set; } = null!;
    public static string? SelectedControllerId { get; set; }
    /// <summary>Profile chosen for editing when no controller is connected.</summary>
    public static string? OfflineProfileId { get; set; }

    /// <summary>The set of controllers or the selected controller changed.</summary>
    public static event Action? ControllersChanged;
    /// <summary>A different profile is now being edited: pages must rebind.</summary>
    public static event Action? EditProfileChanged;
    /// <summary>The edited profile's content changed (for dependent visuals).</summary>
    public static event Action? ProfileContentChanged;
    /// <summary>~30 Hz UI refresh for live data.</summary>
    public static event Action? LiveTick;

    public static ControllerSession? Session =>
        Runtime.Session(SelectedControllerId) ?? Runtime.Sessions.FirstOrDefault();

    public static Profile EditProfile =>
        Session?.Profile ?? Runtime.Profiles.Get(OfflineProfileId) ?? Runtime.Profiles.Get(Runtime.Settings.DefaultProfileId) ?? Runtime.Profiles.Profiles[0];

    public static void Initialize(AppRuntime? runtime = null)
    {
        if (runtime == null)
        {
            var paths = new AppPaths();
            var (services, hid) = PlatformFactory.Create(paths);
            runtime = new AppRuntime(paths, services, hid);
        }
        Runtime = runtime;
        Api = new LocalApiServer(runtime);
        runtime.SessionsChanged += () => Dispatcher.UIThread.Post(() => { ControllersChanged?.Invoke(); CheckProfileSwitch(); });
        runtime.Events.Subscribe(e => { if (e is ProfileAppliedEvent) Dispatcher.UIThread.Post(CheckProfileSwitch); });
        runtime.Start();
        if (runtime.Settings.ApiEnabled) _ = Api.StartAsync(runtime.Settings.ApiPort);
        _lastJson = JsonFile.Serialize(EditProfile);
        _lastProfile = EditProfile;
    }

    private static Profile? _lastProfile;

    public static void SelectController(string? id)
    {
        SelectedControllerId = id;
        ControllersChanged?.Invoke();
        CheckProfileSwitch();
    }

    public static void CheckProfileSwitch()
    {
        var p = EditProfile;
        if (ReferenceEquals(p, _lastProfile)) return;
        FlushSave();
        _lastProfile = p; _lastJson = JsonFile.Serialize(p);
        EditProfileChanged?.Invoke();
    }

    /// <summary>Called by the UI timer. Detects edits, applies them live and saves after 1 s of calm.</summary>
    public static void Tick(bool live)
    {
        if (_shutdown) return;
        if (live) LiveTick?.Invoke();
        else
        {
            CheckProfileSwitch();
            var p = EditProfile;
            string json = JsonFile.Serialize(p);
            if (json != _lastJson)
            {
                _lastJson = json; _dirtySince = DateTime.UtcNow;
                Runtime.RefreshProfile(p);
                ProfileContentChanged?.Invoke();
            }
            else if (_dirtySince != DateTime.MaxValue && (DateTime.UtcNow - _dirtySince).TotalSeconds > 1) FlushSave();
        }
    }

    public static void FlushSave()
    {
        if (_dirtySince == DateTime.MaxValue || _lastProfile == null) return;
        _dirtySince = DateTime.MaxValue;
        try { Runtime.Profiles.Save(_lastProfile); _lastJson = JsonFile.Serialize(_lastProfile); }
        catch (Exception ex) { Log.Error("cannot save profile", ex); }
    }

    public static void Shutdown()
    {
        if (_shutdown || Runtime == null) return;
        _shutdown = true;
        try { FlushSave(); } catch { }
        try { Api.StopAsync().Wait(2000); } catch { }
        try { Runtime.Dispose(); } catch { }
    }
}
