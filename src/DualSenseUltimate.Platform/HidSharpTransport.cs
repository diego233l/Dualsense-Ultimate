using DualSenseUltimate.Core.Devices;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Protocol;
using HidSharp;

namespace DualSenseUltimate.Platform;

/// <summary>Real HID access through HidSharp (Apache-2.0). Devices are opened shared (non-exclusive),
/// so Steam and games can keep talking to the controller.</summary>
public sealed class HidSharpEnumerator : IHidEnumerator
{
    public event Action? Changed;

    public HidSharpEnumerator()
    {
        try { DeviceList.Local.Changed += (_, _) => Changed?.Invoke(); }
        catch (Exception ex) { Log.Warn("HID change notifications unavailable: " + ex.Message); }
    }

    public IReadOnlyList<HidDeviceDescriptor> Enumerate()
    {
        var list = new List<HidDeviceDescriptor>();
        foreach (var d in DeviceList.Local.GetHidDevices(SonyIds.VendorId))
        {
            try
            {
                if (SonyIds.ModelFor(d.VendorID, d.ProductID) == null) continue;
                int maxIn = d.GetMaxInputReportLength();
                if (maxIn < 64) continue; // not the gamepad collection
                string? serial = null;
                try { serial = d.GetSerialNumber(); } catch { /* USB DualSense has no serial string */ }
                list.Add(new HidDeviceDescriptor(d.DevicePath, d.VendorID, d.ProductID, maxIn, serial));
            }
            catch (Exception ex) { Log.Debug("skipping HID device: " + ex.Message); }
        }
        return list;
    }

    public IHidTransport Open(HidDeviceDescriptor descriptor)
    {
        var dev = DeviceList.Local.GetHidDevices(descriptor.VendorId, descriptor.ProductId).FirstOrDefault(d => d.DevicePath == descriptor.Path)
            ?? throw new IOException("device not found");
        var cfg = new OpenConfiguration();
        cfg.SetOption(OpenOption.Exclusive, false);
        cfg.SetOption(OpenOption.Interruptible, false);
        if (!dev.TryOpen(cfg, out var stream)) throw new IOException("cannot open device (in use by another program with exclusive access?)");
        return new HidSharpTransport(dev, stream);
    }
}

internal sealed class HidSharpTransport : IHidTransport
{
    private readonly HidStream _stream;
    private readonly int _maxOut, _maxIn;
    private readonly byte[] _readBuffer;

    public HidSharpTransport(HidDevice dev, HidStream stream)
    {
        _stream = stream;
        _maxOut = dev.GetMaxOutputReportLength();
        _maxIn = dev.GetMaxInputReportLength();
        _readBuffer = new byte[Math.Max(_maxIn, 64)];
        _stream.WriteTimeout = 500;
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        _stream.ReadTimeout = timeoutMs;
        try
        {
            int n = _stream.Read(_readBuffer, 0, _readBuffer.Length);
            n = Math.Min(n, buffer.Length);
            _readBuffer.AsSpan(0, n).CopyTo(buffer);
            return n;
        }
        catch (TimeoutException) { return 0; }
    }

    public void Write(ReadOnlySpan<byte> report)
    {
        // Windows requires the buffer to be exactly the declared output report length (547 bytes for a
        // Bluetooth DualSense); the extra bytes are zero padding after the CRC.
        var buf = new byte[Math.Max(_maxOut, report.Length)];
        report.CopyTo(buf);
        _stream.Write(buf, 0, buf.Length);
    }

    public int GetFeature(Span<byte> buffer)
    {
        try
        {
            var tmp = buffer.ToArray();
            _stream.GetFeature(tmp, 0, tmp.Length);
            tmp.CopyTo(buffer);
            return tmp.Length;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or TimeoutException) { return 0; }
    }

    public void Dispose() => _stream.Dispose();
}
