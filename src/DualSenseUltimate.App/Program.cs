using Avalonia;

namespace DualSenseUltimate.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--cleanup")) return Cleanup();
        // Single instance: two copies would fight over the same controllers.
        using var mutex = new Mutex(true, "DualSenseUltimate.SingleInstance", out bool first);
        if (!first) return 0;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("fatal", ex);
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "DualSenseUltimate-crash.txt"), ex.ToString()); } catch { }
            return 1;
        }
        finally
        {
            // Even on a crash: stop effects, release keys, un-hide devices.
            AppState.Shutdown();
        }
    }

    /// <summary>Used by the uninstaller: un-hide any controller we hid and remove the autostart entry.</summary>
    private static int Cleanup()
    {
        try
        {
            new Platform.HidHideController(new Core.Storage.AppPaths()).UnhideAll();
            if (OperatingSystem.IsWindows()) Platform.WindowsAutoStart.Set(false);
            return 0;
        }
        catch { return 1; }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
