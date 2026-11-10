using System.Diagnostics;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Devices;

public enum DeviceHealth { Ok, Degraded, Lost }

/// <summary>One physical controller: owns the HID handle, the read loop and the coalesced output writer.</summary>
public sealed class ControllerDevice : IDisposable
{
    private readonly IHidTransport _transport;
    private readonly Thread _readThread, _writeThread;
    private readonly AutoResetEvent _outputSignal = new(false);
    private readonly object _outputLock = new();
    private OutputState _pending = new();
    private OutputState? _lastSent;
    private volatile bool _disposed, _outputDirty = true;
    private byte _sequence;
    private long _reportCount, _errorCount, _lastReportTicks, _writeCount;
    private bool _setupSent, _skipRelease;
    /// <summary>How often an unchanged output state is re-sent. A short interval lets this app win when another
    /// program keeps overwriting the controller.</summary>
    public static int KeepAliveMs { get; set; } = 2000;
    public long WriteCount => Interlocked.Read(ref _writeCount);
    private InputState _state = InputState.Neutral;

    public HidDeviceDescriptor Descriptor { get; }
    public ControllerModel Model { get; }
    public ConnectionType Connection { get; }
    public bool IsSimulated => Descriptor.IsSimulated;
    public bool IsDualSense => Model != ControllerModel.DualShock4;
    /// <summary>Stable identifier: the controller MAC address when readable, otherwise the serial or path hash.</summary>
    public string Id { get; private set; }
    public string? FirmwareVersion { get; private set; }
    public string? FirmwareDate { get; private set; }
    public ImuCalibration Calibration { get; private set; } = ImuCalibration.Identity;
    public DeviceHealth Health { get; private set; } = DeviceHealth.Ok;
    public long ReportCount => Interlocked.Read(ref _reportCount);
    public long ErrorCount => Interlocked.Read(ref _errorCount);
    /// <summary>Measured input report rate in Hz.</summary>
    public double ReportRate { get; private set; }
    public InputState State => _state;

    /// <summary>Raised on the read thread for every valid input report.</summary>
    public event Action<ControllerDevice, InputState>? InputReceived;
    /// <summary>Raised once when the device is lost (unplugged, out of range, I/O failure).</summary>
    public event Action<ControllerDevice>? Disconnected;
    public event Action<ControllerDevice>? HealthChanged;

    public ControllerDevice(HidDeviceDescriptor descriptor, IHidTransport transport)
    {
        Descriptor = descriptor;
        _transport = transport;
        Model = descriptor.Model ?? throw new ArgumentException("Not a supported controller", nameof(descriptor));
        Connection = descriptor.Connection;
        Id = descriptor.SerialNumber is { Length: > 0 } s ? NormalizeId(s) : "path-" + ((uint)descriptor.Path.GetHashCode()).ToString("X8");
        ReadIdentity();
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "dsu-read-" + Id };
        _writeThread = new Thread(WriteLoop) { IsBackground = true, Name = "dsu-write-" + Id };
    }

    public void Start() { _readThread.Start(); _writeThread.Start(); }

    private static string NormalizeId(string s)
    {
        string hex = new(s.Where(Uri.IsHexDigit).ToArray());
        return hex.Length == 12 ? string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2).ToUpperInvariant())) : s;
    }

    /// <summary>Reads MAC, firmware and calibration. Reading the calibration feature report also switches
    /// a Bluetooth DualSense from the short 0x01 report to the full 0x31 report.</summary>
    private void ReadIdentity()
    {
        try
        {
            if (IsDualSense)
            {
                var buf = new byte[Connection == ConnectionType.Bluetooth ? 64 + 4 : 64];
                buf[0] = DualSenseProtocol.FeatureCalibration;
                if (_transport.GetFeature(buf.AsSpan(0, DualSenseProtocol.FeatureCalibrationSize)) > 0)
                    Calibration = ImuCalibration.Parse(buf);
                Array.Clear(buf); buf[0] = DualSenseProtocol.FeaturePairing;
                if (_transport.GetFeature(buf.AsSpan(0, DualSenseProtocol.FeaturePairingSize)) > 6 && DualSenseProtocol.ParseMac(buf) is { } mac && mac != "00:00:00:00:00:00")
                    Id = mac;
                Array.Clear(buf); buf[0] = DualSenseProtocol.FeatureFirmware;
                if (_transport.GetFeature(buf.AsSpan(0, DualSenseProtocol.FeatureFirmwareSize)) > 31 && DualSenseProtocol.ParseFirmware(buf) is { } fw)
                { FirmwareVersion = fw.ToString(); FirmwareDate = fw.BuildDate; }
            }
            else
            {
                var buf = new byte[64];
                if (Connection == ConnectionType.Usb)
                {
                    buf[0] = DualShock4Protocol.FeaturePairing;
                    if (_transport.GetFeature(buf.AsSpan(0, 16)) > 6 && DualShock4Protocol.ParseMacUsb(buf) is { } mac) Id = mac;
                }
                else
                {
                    buf[0] = DualShock4Protocol.FeatureCalibrationBt; // enables the full 0x11 report over Bluetooth
                    _transport.GetFeature(buf.AsSpan(0, 41));
                }
                Array.Clear(buf); buf[0] = DualShock4Protocol.FeatureFirmware;
                if (_transport.GetFeature(buf.AsSpan(0, 49)) > 42) FirmwareVersion = DualShock4Protocol.ParseFirmware(buf);
            }
        }
        catch (Exception ex) { Log.Warn($"[{Id}] identity read failed: {ex.Message}"); }
    }

    private void ReadLoop()
    {
        var buffer = new byte[1024];
        int timeouts = 0; long windowStart = Stopwatch.GetTimestamp(); int windowCount = 0;
        while (!_disposed)
        {
            try
            {
                int n = _transport.Read(buffer, 500);
                if (n <= 0)
                {
                    if (++timeouts >= 2) SetHealth(DeviceHealth.Degraded);
                    if (timeouts >= 10) { Lose("no input reports for 5 s"); return; }
                    continue;
                }
                timeouts = 0;
                bool ok = IsDualSense
                    ? DualSenseProtocol.TryParseInput(buffer.AsSpan(0, n), Connection, Model == ControllerModel.DualSenseEdge, out var st)
                    : DualShock4Protocol.TryParseInput(buffer.AsSpan(0, n), Connection, out st);
                if (!ok) { Interlocked.Increment(ref _errorCount); continue; } // short BT report or CRC error
                _state = st;
                Interlocked.Increment(ref _reportCount);
                _lastReportTicks = Stopwatch.GetTimestamp();
                if (++windowCount >= 100)
                {
                    double secs = Stopwatch.GetElapsedTime(windowStart).TotalSeconds;
                    ReportRate = secs > 0 ? windowCount / secs : 0;
                    windowStart = Stopwatch.GetTimestamp(); windowCount = 0;
                }
                SetHealth(DeviceHealth.Ok);
                try { InputReceived?.Invoke(this, st); }
                catch (Exception ex) { Log.Error($"[{Id}] input handler failed", ex); }
            }
            catch (Exception ex) when (!_disposed)
            {
                Lose(ex.Message);
                return;
            }
            catch { return; }
        }
    }

    /// <summary>Queues a new desired output state. The writer thread coalesces updates and only writes when something changed.</summary>
    public void SetOutput(OutputState state)
    {
        lock (_outputLock) { _pending = state.Clone(); _outputDirty = true; }
        _outputSignal.Set();
    }

    private void WriteLoop()
    {
        var keepAlive = Stopwatch.StartNew();
        while (!_disposed)
        {
            _outputSignal.WaitOne(Math.Clamp(KeepAliveMs, 20, 1000));
            if (_disposed) break;
            OutputState s;
            lock (_outputLock)
            {
                // Rewrite every 2 s even if unchanged so the state survives controller sleep/wake and BT glitches.
                if (!_outputDirty && keepAlive.ElapsedMilliseconds < KeepAliveMs) continue;
                s = _pending; _outputDirty = false;
            }
            if (_lastSent != null && _lastSent.ContentEquals(s) && keepAlive.ElapsedMilliseconds < KeepAliveMs) continue;
            if (s.Ownership == OutputOwnership.None) { _lastSent = s; continue; }
            try
            {
                byte[] report = IsDualSense ? DualSenseProtocol.BuildOutput(s, Connection, _sequence++, lightbarSetup: !_setupSent) : DualShock4Protocol.BuildOutput(s, Connection);
                _transport.Write(report);
                _setupSent = true; Interlocked.Increment(ref _writeCount);
                _lastSent = s; keepAlive.Restart();
            }
            catch (Exception ex) when (!_disposed)
            {
                Interlocked.Increment(ref _errorCount);
                Log.Warn($"[{Id}] output write failed: {ex.Message}");
                SetHealth(DeviceHealth.Degraded);
                Thread.Sleep(50);
                lock (_outputLock) _outputDirty = true;
            }
            catch { break; }
            Thread.Sleep(Connection == ConnectionType.Bluetooth ? 8 : 4); // cap write rate (BT bandwidth is limited)
        }
    }

    private void SetHealth(DeviceHealth h)
    {
        if (Health == h) return;
        Health = h;
        try { HealthChanged?.Invoke(this); } catch { }
    }

    private void Lose(string reason)
    {
        if (_disposed) return;
        Log.Info($"[{Id}] device lost: {reason}");
        SetHealth(DeviceHealth.Lost);
        try { Disconnected?.Invoke(this); } catch (Exception ex) { Log.Error("disconnect handler failed", ex); }
    }

    /// <summary>Stops effects and hands the LEDs back to the firmware. Best effort.</summary>
    public void ReleaseHardware()
    {
        try
        {
            if (IsDualSense) _transport.Write(DualSenseProtocol.BuildRelease(Connection, _sequence++));
            else _transport.Write(DualShock4Protocol.BuildOutput(new OutputState { Ownership = OutputOwnership.Rumble }, Connection));
        }
        catch { }
    }

    /// <summary>Closes the handle without touching the controller's state (used for a second HID path of a
    /// controller that is already managed through another path).</summary>
    public void DisposeSilently() { _skipRelease = true; Dispose(); }

    public void Dispose()
    {
        if (_disposed) return;
        if (Health != DeviceHealth.Lost && !_skipRelease) ReleaseHardware();
        _disposed = true;
        _outputSignal.Set();
        try { _transport.Dispose(); } catch { }
    }
}
