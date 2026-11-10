using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Runtime;

public abstract record AppEvent(string? ControllerId) { public DateTime Time { get; } = DateTime.Now; }
public sealed record ControllerConnectedEvent(string Id, string Model, string Connection) : AppEvent(Id);
public sealed record ControllerDisconnectedEvent(string Id) : AppEvent(Id);
public sealed record BatteryChangedEvent(string Id, int Percent, BatteryStatus Status, BatteryStatus PreviousStatus) : AppEvent(Id);
public sealed record ProcessStartedEvent(string Name) : AppEvent((string?)null);
public sealed record ProcessExitedEvent(string Name) : AppEvent((string?)null);
public sealed record NotificationEvent(string Title) : AppEvent((string?)null);
public sealed record ProfileAppliedEvent(string Id, string ProfileName, string Reason) : AppEvent(Id);
public sealed record DeviceHealthEvent(string Id, string Health) : AppEvent(Id);

/// <summary>Decoupled communication between modules.</summary>
public sealed class EventBus
{
    private readonly List<Action<AppEvent>> _handlers = new();
    public IDisposable Subscribe(Action<AppEvent> handler)
    {
        lock (_handlers) _handlers.Add(handler);
        return new Unsubscriber(() => { lock (_handlers) _handlers.Remove(handler); });
    }
    public void Publish(AppEvent e)
    {
        Action<AppEvent>[] snapshot;
        lock (_handlers) snapshot = _handlers.ToArray();
        foreach (var h in snapshot)
        {
            try { h(e); } catch (Exception ex) { Diagnostics.Log.Error("event handler failed", ex); }
        }
    }
    private sealed class Unsubscriber(Action a) : IDisposable { public void Dispose() => a(); }
}

public sealed record ActivityEntry(DateTime Time, string Text);

/// <summary>Short human readable history shown on the dashboard.</summary>
public sealed class ActivityLog
{
    private readonly LinkedList<ActivityEntry> _entries = new();
    public event Action? Changed;
    public void Add(string text)
    {
        lock (_entries) { _entries.AddFirst(new ActivityEntry(DateTime.Now, text)); while (_entries.Count > 100) _entries.RemoveLast(); }
        Diagnostics.Log.Info(text);
        try { Changed?.Invoke(); } catch { }
    }
    public IReadOnlyList<ActivityEntry> Entries { get { lock (_entries) return _entries.ToList(); } }
}

/// <summary>Source of running process names (abstracted for tests).</summary>
public interface IProcessSource
{
    IReadOnlyCollection<string> RunningProcessNames();
    /// <summary>Executable name of the foreground window's process, when the platform can tell.</summary>
    string? ForegroundProcessName() => null;
    /// <summary>Full executable paths of running processes with the given name (best effort).</summary>
    IReadOnlyList<string> ExecutablePaths(string processName) => [];
}

public sealed class SystemProcessSource : IProcessSource
{
    public IReadOnlyCollection<string> RunningProcessNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try { set.Add(p.ProcessName); } catch { }
            p.Dispose();
        }
        return set;
    }
}

/// <summary>Polls the process list and reports starts/exits for the names being watched only.</summary>
public sealed class ProcessWatcher
{
    private readonly IProcessSource _source;
    private HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private bool _primed;
    public ProcessWatcher(IProcessSource source) => _source = source;
    public IReadOnlyCollection<string> Running => _running;

    public static string Normalize(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    /// <summary>Returns (started, exited) among <paramref name="watched"/> since the previous poll.</summary>
    public (List<string> Started, List<string> Exited) Poll(IEnumerable<string> watched)
    {
        var names = watched.Select(Normalize).Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var started = new List<string>(); var exited = new List<string>();
        if (names.Count == 0 && _primed) { return (started, exited); }
        var now = new HashSet<string>(_source.RunningProcessNames().Select(Normalize), StringComparer.OrdinalIgnoreCase);
        now.IntersectWith(names);
        foreach (var n in now) if (!_running.Contains(n)) started.Add(n);
        foreach (var n in _running) if (!now.Contains(n)) exited.Add(n);
        _running = now; _primed = true;
        return (started, exited);
    }
}
