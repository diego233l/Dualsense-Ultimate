namespace DualSenseUltimate.Plugins;

/// <summary>Capabilities a plugin must declare in plugin.json. The host refuses any call outside them.</summary>
[Flags]
public enum PluginPermissions
{
    None = 0,
    /// <summary>Read controller list, battery and connection state.</summary>
    ReadState = 1,
    /// <summary>Change light bar, play haptics, set trigger effects.</summary>
    ControlOutput = 2,
    /// <summary>Switch profiles.</summary>
    Profiles = 4,
    /// <summary>Receive application events and publish notifications.</summary>
    Events = 8,
}

public sealed record ControllerInfo(string Id, string Name, string Model, string Connection, int BatteryPercent, string BatteryStatus, string? ActiveProfile, bool Simulated);

public sealed record DsuEvent(string Type, string? ControllerId, string? Detail, DateTime Time);

/// <summary>Surface the application exposes to plugins.</summary>
public interface IDsuHost
{
    IReadOnlyList<ControllerInfo> Controllers { get; }
    /// <summary>Overrides the light bar. durationMs = 0 keeps it until <see cref="ClearOverrides"/>.</summary>
    void SetLightbar(string controllerId, byte r, byte g, byte b, int durationMs = 0);
    void ClearOverrides(string controllerId);
    void PlayHaptic(string controllerId, string patternName);
    void Rumble(string controllerId, byte left, byte right, int durationMs);
    /// <summary>side: "L2", "R2" or "both"; preset: a trigger effect name from the library.</summary>
    void SetTriggerPreset(string controllerId, string side, string preset);
    bool ApplyProfile(string controllerId, string profileName);
    /// <summary>Publishes a notification event (drives notification lighting, haptics and automation rules).</summary>
    void Notify(string title);
    IDisposable Subscribe(Action<DsuEvent> handler);
    void Log(string message);
    /// <summary>Private folder where the plugin may store its own files.</summary>
    string DataDirectory { get; }
}

public interface IDsuPlugin
{
    void Start(IDsuHost host);
    void Stop();
}
