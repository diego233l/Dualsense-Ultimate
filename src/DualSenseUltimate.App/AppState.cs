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

    // ---- updates

    public static Core.Community.UpdateInfo? Update { get; private set; }
    public static string UpdateStatus { get; private set; } = "";
    public static double UpdateProgress { get; private set; } = -1;
    public static event Action? UpdateChanged;
    public static Version CurrentVersion => typeof(AppState).Assembly.GetName().Version ?? new Version(1, 0, 0);
    /// <summary>True when installed with the installer (in-place update possible); false for the portable build.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    private static void RaiseUpdate() => Dispatcher.UIThread.Post(() => UpdateChanged?.Invoke());

    public static async Task CheckUpdateAsync(bool manual)
    {
        try
        {
            if (manual) { UpdateStatus = "Consultando…"; RaiseUpdate(); }
            var info = await Core.Community.UpdateChecker.CheckAsync(Runtime.Settings.UpdateRepository, CurrentVersion);
            if (info == null) { Update = null; UpdateStatus = "Todavía no hay versiones publicadas."; }
            else if (!info.IsNewer) { Update = null; UpdateStatus = $"Tienes la última versión ({CurrentVersion.ToString(3)})."; }
            else
            {
                bool skipped = !manual && Runtime.Settings.SkippedVersion == info.Version;
                bool isNew = Update?.Version != info.Version;
                Update = skipped ? null : info;
                UpdateStatus = $"Versión {info.Version} disponible.";
                if (!skipped && isNew) Runtime.Activity.Add($"Actualización disponible: versión {info.Version}");
            }
        }
        catch (Exception ex)
        {
            if (manual) UpdateStatus = "No se pudo consultar (¿sin conexión?): " + ex.Message; // silent when automatic: the app works offline
        }
        RaiseUpdate();
    }

    public static void SkipUpdate()
    {
        if (Update == null) return;
        Runtime.Settings.SkippedVersion = Update.Version; Runtime.SaveSettings();
        UpdateStatus = $"No se volverá a avisar de la versión {Update.Version}.";
        Update = null; RaiseUpdate();
    }

    /// <summary>Downloads the verified installer and starts it; the app then exits so files can be replaced.
    /// Only ever called from an explicit user action.</summary>
    public static async Task InstallUpdateAsync()
    {
        var info = Update;
        if (info == null || UpdateProgress >= 0) return;
        try
        {
            UpdateProgress = 0; UpdateStatus = "Descargando…"; RaiseUpdate();
            var progress = new Progress<double>(v => { UpdateProgress = v; UpdateStatus = $"Descargando… {v:0%}"; UpdateChanged?.Invoke(); });
            string dir = Path.Combine(Path.GetTempPath(), "DualSenseUltimate-Update");
            string file = await Core.Community.UpdateChecker.DownloadInstallerAsync(info, Runtime.Settings.UpdateRepository, dir, progress);
            UpdateStatus = "Descarga verificada. Iniciando el instalador…"; RaiseUpdate();
            FlushSave();
            // Start the installer a few seconds after this process has exited, so it never finds the app still running.
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add($"timeout /t 3 /nobreak >nul & start \"\" \"{file}\" /SILENT /NORESTART");
            System.Diagnostics.Process.Start(psi);
            Dispatcher.UIThread.Post(App.ExitApp);
        }
        catch (Exception ex)
        {
            Log.Error("update failed", ex);
            UpdateProgress = -1; UpdateStatus = "No se pudo actualizar: " + ex.Message; RaiseUpdate();
        }
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
