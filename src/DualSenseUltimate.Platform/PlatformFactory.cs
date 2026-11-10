using DualSenseUltimate.Core.Devices;
using DualSenseUltimate.Core.Runtime;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.Platform;

public static class PlatformFactory
{
    private static readonly (string Process, string Label)[] KnownConflicts =
    [
        ("DS4Windows", "DS4Windows"), ("DSX", "DSX"), ("DualSenseX", "DualSenseX"), ("reWASD", "reWASD"),
        ("InputMapper", "InputMapper"), ("JoyShockMapper", "JoyShockMapper"), ("DualSenseY", "DualSenseY"), ("DualSenseY-v2", "DualSenseY"),
        ("PlayStationAccessories", "PlayStation Accessories"), ("Playnite.DesktopApp", "Playnite"), ("JoyToKey", "JoyToKey"), ("SignalRgb", "SignalRGB"), ("OpenRGB", "OpenRGB"), ("steam", "Steam (Steam Input puede tomar el control del mando)"),
    ];

    /// <summary>Creates the services available on the current OS. On non-Windows systems only HID access
    /// is provided (useful for development); every Windows-only feature reports itself as unavailable.</summary>
    public static (PlatformServices Services, IHidEnumerator Hid) Create(AppPaths paths)
    {
        var hid = new HidSharpEnumerator();
        IProcessSource processes = OperatingSystem.IsWindows() ? new WindowsProcessSource() : new SystemProcessSource();
        Func<IReadOnlyList<string>> conflicts = () =>
        {
            var running = processes.RunningProcessNames();
            return KnownConflicts.Where(c => running.Contains(c.Process)).Select(c => c.Label).ToList();
        };
        if (!OperatingSystem.IsWindows())
            return (new PlatformServices { ProcessSource = processes, ConflictDetector = conflicts }, hid);

        return (new PlatformServices
        {
            Injector = new WindowsInputInjector(),
            PadFactory = new ViGEmPadFactory(),
            Hider = new HidHideController(paths),
            AudioSource = new WasapiLoopbackSource(),
            HapticAudio = new DualSenseHapticAudioOutput(),
            ProcessSource = processes,
            ConflictDetector = conflicts,
        }, hid);
    }
}
