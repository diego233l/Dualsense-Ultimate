using System.Collections.Concurrent;
using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Devices;

/// <summary>In-memory controller that speaks the real wire protocol. It exists for automated tests and
/// for developing the UI without hardware. It is NOT evidence that anything works on a physical controller.</summary>
public sealed class SimulatedController : IHidTransport
{
    private readonly BlockingCollection<byte[]> _inbox = new();
    private volatile bool _disposed;
    private byte _seq;
    public HidDeviceDescriptor Descriptor { get; }
    public ConnectionType Connection => Descriptor.Connection;
    public bool IsEdge => Descriptor.ProductId == SonyIds.DualSenseEdge;
    /// <summary>Input state reported by the fake controller. Mutate and call <see cref="Push"/> (or let auto-report run).</summary>
    public InputState Input = InputState.Neutral;
    /// <summary>Last output state the application wrote (decoded from the raw report).</summary>
    public OutputState? LastOutput { get; private set; }
    public List<byte[]> RawWrites { get; } = new();
    public string Mac { get; }
    private readonly Timer? _auto;

    public SimulatedController(string name, ConnectionType connection, int productId = SonyIds.DualSense, string mac = "AA:BB:CC:00:00:01", int autoReportHz = 0)
    {
        Mac = mac;
        Descriptor = new HidDeviceDescriptor($"sim://{name}", SonyIds.VendorId, productId, connection == ConnectionType.Usb ? 64 : 78, null, true);
        Input.BatteryPercent = 75; Input.BatteryStatus = BatteryStatus.Discharging;
        if (autoReportHz > 0) _auto = new Timer(_ => Push(), null, 0, 1000 / autoReportHz);
    }

    /// <summary>Emits one input report with the current <see cref="Input"/>.</summary>
    public void Push()
    {
        if (_disposed) return;
        var s = Input; s.SensorTimestamp += 4000;
        Input.SensorTimestamp = s.SensorTimestamp;
        try { _inbox.Add(DualSenseProtocol.BuildInput(s, Connection, IsEdge, _seq++)); } catch (InvalidOperationException) { }
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        if (_disposed) throw new IOException("device removed");
        try
        {
            if (!_inbox.TryTake(out var r, timeoutMs)) return _disposed ? throw new IOException("device removed") : 0;
            r.CopyTo(buffer);
            return r.Length;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { throw new IOException("device removed"); }
    }

    public void Write(ReadOnlySpan<byte> report)
    {
        if (_disposed) throw new IOException("device removed");
        lock (RawWrites)
        {
            RawWrites.Add(report.ToArray());
            if (RawWrites.Count > 256) RawWrites.RemoveAt(0);
            if (DualSenseProtocol.TryParseOutput(report, Connection, out var st)) LastOutput = Merge(LastOutput, st);
        }
    }

    // The real controller keeps fields whose valid flag is not set; mirror that.
    private static OutputState Merge(OutputState? prev, OutputState n)
    {
        var m = prev?.Clone() ?? new OutputState();
        if (n.Ownership.HasFlag(OutputOwnership.Rumble)) { m.MotorLeft = n.MotorLeft; m.MotorRight = n.MotorRight; }
        if (n.Ownership.HasFlag(OutputOwnership.Lightbar)) { m.Red = n.Red; m.Green = n.Green; m.Blue = n.Blue; }
        if (n.Ownership.HasFlag(OutputOwnership.PlayerLeds)) { m.PlayerLeds = n.PlayerLeds; m.PlayerLedBrightness = n.PlayerLedBrightness; }
        if (n.Ownership.HasFlag(OutputOwnership.MicLed)) m.MicLed = n.MicLed;
        if (n.Ownership.HasFlag(OutputOwnership.LeftTrigger)) m.LeftTrigger = n.LeftTrigger;
        if (n.Ownership.HasFlag(OutputOwnership.RightTrigger)) m.RightTrigger = n.RightTrigger;
        m.Ownership = n.Ownership;
        return m;
    }

    public int GetFeature(Span<byte> buffer)
    {
        byte id = buffer[0];
        if (id == DualSenseProtocol.FeaturePairing && buffer.Length >= 7)
        {
            var parts = Mac.Split(':');
            for (int i = 0; i < 6; i++) buffer[6 - i] = Convert.ToByte(parts[i], 16);
            return buffer.Length;
        }
        if (id == DualSenseProtocol.FeatureFirmware && buffer.Length >= 32)
        {
            System.Text.Encoding.ASCII.GetBytes("Jan 01 2024 00:00:00").AsSpan(0, 19).CopyTo(buffer.Slice(1));
            buffer[28] = 0x10; buffer[29] = 0x04; buffer[30] = 0x00; buffer[31] = 0x51; // arbitrary fake version
            return buffer.Length;
        }
        return 0;
    }

    public void Dispose()
    {
        _disposed = true;
        _auto?.Dispose();
        try { _inbox.CompleteAdding(); } catch { }
    }
}

/// <summary>Enumerator exposing simulated controllers. Add/remove to emulate hot plug.</summary>
public sealed class SimulatedEnumerator : IHidEnumerator
{
    private readonly List<SimulatedController> _devices = new();
    public event Action? Changed;

    public void Plug(SimulatedController c) { lock (_devices) _devices.Add(c); Changed?.Invoke(); }
    public void Unplug(SimulatedController c) { lock (_devices) _devices.Remove(c); c.Dispose(); Changed?.Invoke(); }
    public IReadOnlyList<SimulatedController> Controllers { get { lock (_devices) return _devices.ToList(); } }
    public IReadOnlyList<HidDeviceDescriptor> Enumerate() { lock (_devices) return _devices.Select(d => d.Descriptor).ToList(); }
    public IHidTransport Open(HidDeviceDescriptor descriptor)
    {
        lock (_devices) return _devices.First(d => d.Descriptor.Path == descriptor.Path);
    }
}
