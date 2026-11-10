using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Devices;

/// <summary>Discovers controllers, opens them and tracks hot plug. Several enumerators can be combined
/// (real HID + simulator).</summary>
public sealed class DeviceManager : IDisposable
{
    private readonly List<IHidEnumerator> _enumerators;
    private readonly Dictionary<string, ControllerDevice> _byPath = new();
    private readonly Dictionary<string, DateTime> _openFailures = new();
    private readonly object _lock = new();
    private readonly Timer _timer;
    private int _scanning;
    private bool _disposed;

    public event Action<ControllerDevice>? Connected;
    public event Action<ControllerDevice>? Disconnected;

    public DeviceManager(params IHidEnumerator[] enumerators)
    {
        _enumerators = enumerators.ToList();
        foreach (var e in _enumerators) e.Changed += () => _timer?.Change(150, 1500);
        _timer = new Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<ControllerDevice> Devices { get { lock (_lock) return _byPath.Values.ToList(); } }

    public void Start() => _timer.Change(0, 1500);

    /// <summary>Synchronous rescan. Safe to call from any thread; overlapping calls are skipped.</summary>
    public void Scan()
    {
        if (_disposed || Interlocked.Exchange(ref _scanning, 1) == 1) return;
        try
        {
            var found = new List<(IHidEnumerator e, HidDeviceDescriptor d)>();
            foreach (var e in _enumerators)
            {
                try { found.AddRange(e.Enumerate().Where(d => d.Model != null).Select(d => (e, d))); }
                catch (Exception ex) { Log.Warn("enumeration failed: " + ex.Message); }
            }
            var paths = found.Select(f => f.d.Path).ToHashSet();
            List<ControllerDevice> gone;
            lock (_lock) gone = _byPath.Where(kv => !paths.Contains(kv.Key)).Select(kv => kv.Value).ToList();
            foreach (var g in gone) Remove(g);

            // USB first: if the same controller is visible over both transports we keep the wired one.
            foreach (var (e, d) in found.OrderBy(f => f.d.Connection == ConnectionType.Usb ? 0 : 1))
            {
                lock (_lock) if (_byPath.ContainsKey(d.Path)) continue;
                if (_openFailures.TryGetValue(d.Path, out var until) && DateTime.UtcNow < until) continue;
                ControllerDevice dev;
                try { dev = new ControllerDevice(d, e.Open(d)); }
                catch (Exception ex)
                {
                    // Typically another program holds the device exclusively. Retry with back-off.
                    _openFailures[d.Path] = DateTime.UtcNow.AddSeconds(5);
                    Log.Warn($"cannot open {d.Path}: {ex.Message}");
                    continue;
                }
                bool duplicate;
                lock (_lock) duplicate = _byPath.Values.Any(x => x.Id == dev.Id);
                if (duplicate) { dev.Dispose(); _openFailures[d.Path] = DateTime.UtcNow.AddSeconds(5); continue; }
                lock (_lock) _byPath[d.Path] = dev;
                dev.Disconnected += Remove;
                Log.Info($"connected {dev.Model} [{dev.Id}] via {dev.Connection}{(dev.IsSimulated ? " (SIMULATED)" : "")}");
                try { Connected?.Invoke(dev); } catch (Exception ex) { Log.Error("connect handler failed", ex); }
                dev.Start();
            }
        }
        finally { Interlocked.Exchange(ref _scanning, 0); }
    }

    private void Remove(ControllerDevice dev)
    {
        lock (_lock) { if (!_byPath.Remove(dev.Descriptor.Path)) return; }
        try { Disconnected?.Invoke(dev); } catch (Exception ex) { Log.Error("disconnect handler failed", ex); }
        dev.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
        foreach (var d in Devices) { try { d.Dispose(); } catch { } }
        lock (_lock) _byPath.Clear();
    }
}
