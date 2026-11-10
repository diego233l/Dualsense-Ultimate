using DualSenseUltimate.Plugins;

namespace BatteryLoggerPlugin;

/// <summary>Minimal example plugin: logs battery changes to a CSV in the plugin's private data folder.</summary>
public sealed class BatteryLogger : IDsuPlugin
{
    private IDisposable? _subscription;
    private IDsuHost? _host;

    public void Start(IDsuHost host)
    {
        _host = host;
        host.Log($"started; {host.Controllers.Count} controller(s) connected");
        _subscription = host.Subscribe(OnEvent);
    }

    private void OnEvent(DsuEvent e)
    {
        if (e.Type != "battery.changed" || e.ControllerId == null || _host == null) return;
        File.AppendAllText(Path.Combine(_host.DataDirectory, "battery.csv"), $"{e.Time:O},{e.ControllerId},{e.Detail}\n");
        string[] parts = (e.Detail ?? "").Split('/');
        if (parts.Length == 2 && int.TryParse(parts[0], out int percent) && percent < 10 && parts[1] == "Discharging")
            _host.SetLightbar(e.ControllerId, 255, 0, 0, 1500);
    }

    public void Stop() { _subscription?.Dispose(); _host?.Log("stopped"); }
}
