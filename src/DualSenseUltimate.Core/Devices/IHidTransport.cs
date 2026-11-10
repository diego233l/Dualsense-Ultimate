using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Devices;

/// <summary>Static description of a HID device as found during enumeration.</summary>
public sealed record HidDeviceDescriptor(string Path, int VendorId, int ProductId, int MaxInputReportLength, string? SerialNumber, bool IsSimulated = false)
{
    public ControllerModel? Model => SonyIds.ModelFor(VendorId, ProductId);
    /// <summary>USB reports are 64 bytes; Bluetooth extended reports are 78 (Windows may report 547 for the BT collection).</summary>
    public ConnectionType Connection => MaxInputReportLength > 64 ? ConnectionType.Bluetooth : ConnectionType.Usb;
}

/// <summary>An open HID device. Implementations must be safe to Dispose from another thread to unblock Read.</summary>
public interface IHidTransport : IDisposable
{
    /// <summary>Reads one input report (with report id). Returns 0 on timeout. Throws IOException when the device is gone.</summary>
    int Read(Span<byte> buffer, int timeoutMs);
    void Write(ReadOnlySpan<byte> report);
    /// <summary>buffer[0] must hold the report id. Returns bytes read, or 0 if unsupported.</summary>
    int GetFeature(Span<byte> buffer);
}

public interface IHidEnumerator
{
    IReadOnlyList<HidDeviceDescriptor> Enumerate();
    IHidTransport Open(HidDeviceDescriptor descriptor);
    /// <summary>Raised when the OS reports a device list change (may be raised on any thread; optional).</summary>
    event Action? Changed;
}
