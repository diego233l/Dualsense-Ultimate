using System.Diagnostics;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Storage;
using DualSenseUltimate.Core.Virtual;

namespace DualSenseUltimate.Platform;

/// <summary>Hides physical controllers from games using the HidHide filter driver, driven through its
/// official command line client (HidHideCLI.exe). The list of devices we hid is persisted, so after a
/// crash the next start (or the uninstaller) can un-hide them: a controller is never left invisible.</summary>
public sealed class HidHideController : IDeviceHider
{
    private readonly string _stateFile;
    private readonly string? _cli;
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    public HidHideController(AppPaths paths)
    {
        _stateFile = paths.HiddenDevicesFile;
        _cli = FindCli();
        try { if (File.Exists(_stateFile)) foreach (var s in JsonFile.Load<List<string>>(_stateFile)) _hidden.Add(s); } catch { }
    }

    public bool IsAvailable => _cli != null;
    public string Status => _cli != null ? "HidHide detectado" : "HidHide no está instalado (opcional; necesario solo para ocultar el mando físico al emular).";

    private static string? FindCli()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions e.U", "HidHide", "x64", "HidHideCLI.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Converts a HID device interface path into the device instance path HidHide expects.
    /// \\?\hid#vid_054c&amp;pid_0ce6&amp;mi_03#8&amp;1a2b&amp;0&amp;0000#{4d1e55b2-...} -> HID\VID_054C&amp;PID_0CE6&amp;MI_03\8&amp;1A2B&amp;0&amp;0000</summary>
    public static string? ToInstancePath(string hidPath)
    {
        string p = hidPath;
        if (p.StartsWith(@"\\?\") || p.StartsWith(@"\\.\")) p = p[4..];
        int guid = p.LastIndexOf("#{", StringComparison.Ordinal);
        if (guid < 0) return null;
        p = p[..guid];
        var parts = p.Split('#');
        return parts.Length == 3 ? string.Join('\\', parts).ToUpperInvariant() : null;
    }

    private bool Run(params string[] args)
    {
        if (_cli == null) return false;
        try
        {
            var psi = new ProcessStartInfo(_cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi)!;
            if (!proc.WaitForExit(5000)) { try { proc.Kill(); } catch { } return false; }
            if (proc.ExitCode != 0) Log.Warn($"HidHideCLI {string.Join(' ', args)} -> exit {proc.ExitCode}: {proc.StandardError.ReadToEnd().Trim()}");
            return proc.ExitCode == 0;
        }
        catch (Exception ex) { Log.Error("HidHideCLI failed", ex); return false; }
    }

    private void Persist() { try { JsonFile.Save(_stateFile, _hidden.ToList()); } catch { } }

    public bool Hide(string hidPath)
    {
        string? instance = ToInstancePath(hidPath);
        if (instance == null || _cli == null) return false;
        // Whitelist ourselves first so we keep seeing the device, then hide it and enable cloaking.
        if (Environment.ProcessPath is { } exe) Run("--app-reg", exe);
        _hidden.Add(instance); Persist(); // persist before hiding: a crash in between is still recoverable
        if (!Run("--dev-hide", instance)) { _hidden.Remove(instance); Persist(); return false; }
        Run("--cloak-on");
        return true;
    }

    public void Unhide(string hidPath)
    {
        string? instance = ToInstancePath(hidPath);
        if (instance == null) return;
        Run("--dev-unhide", instance);
        _hidden.Remove(instance); Persist();
    }

    public void UnhideAll()
    {
        foreach (var instance in _hidden.ToList()) Run("--dev-unhide", instance);
        _hidden.Clear(); Persist();
    }
}
