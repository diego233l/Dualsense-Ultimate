using System.Buffers.Binary;

namespace DualSenseUltimate.Core.Protocol;

/// <summary>DualSense / DualSense Edge HID report codec. Layout taken from the Linux kernel
/// driver hid-playstation.c (struct dualsense_input_report / dualsense_output_report_common).</summary>
public static class DualSenseProtocol
{
    public const byte InputReportUsb = 0x01, InputReportBt = 0x31;
    public const int InputReportUsbSize = 64, InputReportBtSize = 78;
    public const byte OutputReportUsb = 0x02, OutputReportBt = 0x31;
    /// <summary>Windows reports 48 bytes for the USB output report (the kernel pads it to 63).</summary>
    public const int OutputReportUsbSize = 48, OutputReportBtSize = 78;
    public const byte FeatureCalibration = 0x05, FeaturePairing = 0x09, FeatureFirmware = 0x20;
    public const int FeatureCalibrationSize = 41, FeaturePairingSize = 20, FeatureFirmwareSize = 64;
    public const int TouchpadWidth = 1920, TouchpadHeight = 1080;
    public const int GyroResolutionPerDegS = 1024, AccelResolutionPerG = 8192;
    private const int CommonSize = 47;

    /// <summary>Parses a full input report (including the report id byte).</summary>
    public static bool TryParseInput(ReadOnlySpan<byte> report, ConnectionType connection, bool isEdge, out InputState state)
    {
        state = InputState.Neutral;
        ReadOnlySpan<byte> p;
        if (connection == ConnectionType.Usb)
        {
            if (report.Length < InputReportUsbSize || report[0] != InputReportUsb) return false;
            p = report.Slice(1, 63);
        }
        else
        {
            // Until feature report 0x05 is read the controller sends the short 0x01 report over Bluetooth.
            if (report.Length < InputReportBtSize || report[0] != InputReportBt) return false;
            if (!Crc32.Verify(Crc32.SeedInput, report[..InputReportBtSize])) return false;
            p = report.Slice(2, 63);
        }

        state.LeftX = p[0]; state.LeftY = p[1]; state.RightX = p[2]; state.RightY = p[3];
        state.L2 = p[4]; state.R2 = p[5];
        byte b0 = p[7], b1 = p[8], b2 = p[9];
        PadButtons b = HatToButtons(b0 & 0x0F);
        if ((b0 & 0x10) != 0) b |= PadButtons.Square;
        if ((b0 & 0x20) != 0) b |= PadButtons.Cross;
        if ((b0 & 0x40) != 0) b |= PadButtons.Circle;
        if ((b0 & 0x80) != 0) b |= PadButtons.Triangle;
        if ((b1 & 0x01) != 0) b |= PadButtons.L1;
        if ((b1 & 0x02) != 0) b |= PadButtons.R1;
        if ((b1 & 0x04) != 0) b |= PadButtons.L2;
        if ((b1 & 0x08) != 0) b |= PadButtons.R2;
        if ((b1 & 0x10) != 0) b |= PadButtons.Create;
        if ((b1 & 0x20) != 0) b |= PadButtons.Options;
        if ((b1 & 0x40) != 0) b |= PadButtons.L3;
        if ((b1 & 0x80) != 0) b |= PadButtons.R3;
        if ((b2 & 0x01) != 0) b |= PadButtons.PS;
        if ((b2 & 0x02) != 0) b |= PadButtons.Touchpad;
        if ((b2 & 0x04) != 0) b |= PadButtons.Mute;
        if (isEdge)
        {
            if ((b2 & 0x10) != 0) b |= PadButtons.FnLeft;
            if ((b2 & 0x20) != 0) b |= PadButtons.FnRight;
            if ((b2 & 0x40) != 0) b |= PadButtons.PaddleLeft;
            if ((b2 & 0x80) != 0) b |= PadButtons.PaddleRight;
        }
        state.Buttons = b;

        state.GyroX = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(15));
        state.GyroY = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(17));
        state.GyroZ = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(19));
        state.AccelX = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(21));
        state.AccelY = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(23));
        state.AccelZ = BinaryPrimitives.ReadInt16LittleEndian(p.Slice(25));
        state.SensorTimestamp = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(27));
        state.Touch0 = ParseTouch(p.Slice(32, 4));
        state.Touch1 = ParseTouch(p.Slice(36, 4));
        state.R2FeedbackStatus = p[41];
        state.L2FeedbackStatus = p[42];

        byte status0 = p[52], status1 = p[53];
        int level = status0 & 0x0F;
        state.BatteryLevelRaw = (byte)Math.Min(level, 10);
        switch ((status0 & 0xF0) >> 4)
        {
            case 0x0: state.BatteryPercent = Math.Min(level * 10 + 5, 100); state.BatteryStatus = BatteryStatus.Discharging; break;
            case 0x1: state.BatteryPercent = Math.Min(level * 10 + 5, 100); state.BatteryStatus = BatteryStatus.Charging; break;
            case 0x2: state.BatteryPercent = 100; state.BatteryStatus = BatteryStatus.Full; break;
            case 0xA: case 0xB: case 0xF: state.BatteryPercent = 0; state.BatteryStatus = BatteryStatus.Error; break;
            default: state.BatteryPercent = 0; state.BatteryStatus = BatteryStatus.Unknown; break;
        }
        state.HeadphonesConnected = (status1 & 0x01) != 0;
        state.MicConnected = (status1 & 0x02) != 0;
        state.MicMuted = (status1 & 0x04) != 0;
        return true;
    }

    public static PadButtons HatToButtons(int hat) => hat switch
    {
        0 => PadButtons.DpadUp, 1 => PadButtons.DpadUp | PadButtons.DpadRight, 2 => PadButtons.DpadRight,
        3 => PadButtons.DpadRight | PadButtons.DpadDown, 4 => PadButtons.DpadDown,
        5 => PadButtons.DpadDown | PadButtons.DpadLeft, 6 => PadButtons.DpadLeft,
        7 => PadButtons.DpadLeft | PadButtons.DpadUp, _ => PadButtons.None,
    };

    public static byte ButtonsToHat(PadButtons b)
    {
        bool u = b.HasFlag(PadButtons.DpadUp), d = b.HasFlag(PadButtons.DpadDown), l = b.HasFlag(PadButtons.DpadLeft), r = b.HasFlag(PadButtons.DpadRight);
        if (u && r) return 1; if (r && d) return 3; if (d && l) return 5; if (l && u) return 7;
        if (u) return 0; if (r) return 2; if (d) return 4; if (l) return 6;
        return 8;
    }

    internal static TouchPoint ParseTouch(ReadOnlySpan<byte> t)
    {
        bool active = (t[0] & 0x80) == 0;
        int x = t[1] | ((t[2] & 0x0F) << 8);
        int y = ((t[2] & 0xF0) >> 4) | (t[3] << 4);
        return new TouchPoint(active, (byte)(t[0] & 0x7F), x, y);
    }

    /// <summary>Builds a complete output report. Only fields covered by <see cref="OutputState.Ownership"/>
    /// have their valid flag set; the controller ignores the rest.</summary>
    public static byte[] BuildOutput(OutputState s, ConnectionType connection, byte sequence = 0, bool lightbarSetup = false)
    {
        byte[] report; Span<byte> c;
        if (connection == ConnectionType.Usb)
        {
            report = new byte[OutputReportUsbSize];
            report[0] = OutputReportUsb;
            c = report.AsSpan(1, CommonSize);
        }
        else
        {
            report = new byte[OutputReportBtSize];
            report[0] = OutputReportBt;
            report[1] = (byte)((sequence & 0x0F) << 4);
            report[2] = 0x10; // DS_OUTPUT_TAG
            c = report.AsSpan(3, CommonSize);
        }

        byte flag0 = 0, flag1 = 0, flag2 = 0;
        if (s.Ownership.HasFlag(OutputOwnership.Rumble))
        {
            flag0 |= 0x01 | 0x02; // compatible vibration + haptics select
            c[2] = s.MotorRight; c[3] = s.MotorLeft;
        }
        if (s.Ownership.HasFlag(OutputOwnership.RightTrigger)) { flag0 |= 0x04; s.RightTrigger.AsSpan(0, 11).CopyTo(c.Slice(10)); }
        if (s.Ownership.HasFlag(OutputOwnership.LeftTrigger)) { flag0 |= 0x08; s.LeftTrigger.AsSpan(0, 11).CopyTo(c.Slice(21)); }
        if (s.Ownership.HasFlag(OutputOwnership.MicLed)) { flag1 |= 0x01; c[8] = (byte)s.MicLed; }
        if (s.Ownership.HasFlag(OutputOwnership.Lightbar))
        {
            flag1 |= 0x04;
            // Lightbar setup "light out" cancels the blue boot animation. It must be sent once per
            // connection only: repeating it restarts the firmware fade on every report.
            if (lightbarSetup) { flag2 |= 0x02; c[41] = 0x02; }
            c[44] = s.Red; c[45] = s.Green; c[46] = s.Blue;
        }
        if (s.Ownership.HasFlag(OutputOwnership.PlayerLeds))
        {
            flag1 |= 0x10; flag2 |= 0x01;
            c[42] = (byte)s.PlayerLedBrightness;
            c[43] = (byte)((s.PlayerLeds & 0x1F) | 0x20); // bit 5: apply instantly, no fade
        }
        c[0] = flag0; c[1] = flag1; c[38] = flag2;

        if (connection == ConnectionType.Bluetooth) Crc32.Append(Crc32.SeedOutput, report);
        return report;
    }

    /// <summary>Report that hands LEDs back to the firmware and stops motors and triggers. Sent when
    /// the application closes so the controller is never left in a stuck state.</summary>
    public static byte[] BuildRelease(ConnectionType connection, byte sequence = 0)
    {
        var s = new OutputState { Ownership = OutputOwnership.Rumble | OutputOwnership.LeftTrigger | OutputOwnership.RightTrigger | OutputOwnership.MicLed };
        var report = BuildOutput(s, connection, sequence);
        int c = connection == ConnectionType.Usb ? 1 : 3;
        report[c + 1] |= 0x08; // DS_OUTPUT_VALID_FLAG1_RELEASE_LEDS
        if (connection == ConnectionType.Bluetooth) Crc32.Append(Crc32.SeedOutput, report);
        return report;
    }

    /// <summary>Decodes an output report back into an <see cref="OutputState"/> (used by the simulator and tests).</summary>
    public static bool TryParseOutput(ReadOnlySpan<byte> report, ConnectionType connection, out OutputState state)
    {
        state = new OutputState { Ownership = OutputOwnership.None };
        ReadOnlySpan<byte> c;
        if (connection == ConnectionType.Usb)
        {
            if (report.Length < OutputReportUsbSize || report[0] != OutputReportUsb) return false;
            c = report.Slice(1, CommonSize);
        }
        else
        {
            if (report.Length < OutputReportBtSize || report[0] != OutputReportBt || report[2] != 0x10) return false;
            if (!Crc32.Verify(Crc32.SeedOutput, report[..OutputReportBtSize])) return false;
            c = report.Slice(3, CommonSize);
        }
        byte f0 = c[0], f1 = c[1];
        if ((f0 & 0x03) != 0) { state.Ownership |= OutputOwnership.Rumble; state.MotorRight = c[2]; state.MotorLeft = c[3]; }
        if ((f0 & 0x04) != 0) { state.Ownership |= OutputOwnership.RightTrigger; state.RightTrigger = c.Slice(10, 11).ToArray(); }
        if ((f0 & 0x08) != 0) { state.Ownership |= OutputOwnership.LeftTrigger; state.LeftTrigger = c.Slice(21, 11).ToArray(); }
        if ((f1 & 0x01) != 0) { state.Ownership |= OutputOwnership.MicLed; state.MicLed = (MicLedMode)c[8]; }
        if ((f1 & 0x04) != 0) { state.Ownership |= OutputOwnership.Lightbar; state.Red = c[44]; state.Green = c[45]; state.Blue = c[46]; }
        if ((f1 & 0x10) != 0) { state.Ownership |= OutputOwnership.PlayerLeds; state.PlayerLeds = (byte)(c[43] & 0x1F); state.PlayerLedBrightness = (LedBrightness)c[42]; }
        return true;
    }

    /// <summary>Builds an input report from a state (used by the simulator and tests).</summary>
    public static byte[] BuildInput(in InputState s, ConnectionType connection, bool isEdge = false, byte sequence = 0)
    {
        byte[] report = new byte[connection == ConnectionType.Usb ? InputReportUsbSize : InputReportBtSize];
        Span<byte> p;
        if (connection == ConnectionType.Usb) { report[0] = InputReportUsb; p = report.AsSpan(1, 63); }
        else { report[0] = InputReportBt; p = report.AsSpan(2, 63); }
        p[0] = s.LeftX; p[1] = s.LeftY; p[2] = s.RightX; p[3] = s.RightY; p[4] = s.L2; p[5] = s.R2; p[6] = sequence;
        byte b0 = ButtonsToHat(s.Buttons), b1 = 0, b2 = 0;
        if (s.IsDown(PadButtons.Square)) b0 |= 0x10; if (s.IsDown(PadButtons.Cross)) b0 |= 0x20;
        if (s.IsDown(PadButtons.Circle)) b0 |= 0x40; if (s.IsDown(PadButtons.Triangle)) b0 |= 0x80;
        if (s.IsDown(PadButtons.L1)) b1 |= 0x01; if (s.IsDown(PadButtons.R1)) b1 |= 0x02;
        if (s.IsDown(PadButtons.L2)) b1 |= 0x04; if (s.IsDown(PadButtons.R2)) b1 |= 0x08;
        if (s.IsDown(PadButtons.Create)) b1 |= 0x10; if (s.IsDown(PadButtons.Options)) b1 |= 0x20;
        if (s.IsDown(PadButtons.L3)) b1 |= 0x40; if (s.IsDown(PadButtons.R3)) b1 |= 0x80;
        if (s.IsDown(PadButtons.PS)) b2 |= 0x01; if (s.IsDown(PadButtons.Touchpad)) b2 |= 0x02; if (s.IsDown(PadButtons.Mute)) b2 |= 0x04;
        if (isEdge)
        {
            if (s.IsDown(PadButtons.FnLeft)) b2 |= 0x10; if (s.IsDown(PadButtons.FnRight)) b2 |= 0x20;
            if (s.IsDown(PadButtons.PaddleLeft)) b2 |= 0x40; if (s.IsDown(PadButtons.PaddleRight)) b2 |= 0x80;
        }
        p[7] = b0; p[8] = b1; p[9] = b2;
        BinaryPrimitives.WriteInt16LittleEndian(p.Slice(15), s.GyroX);
        BinaryPrimitives.WriteInt16LittleEndian(p.Slice(17), s.GyroY);
        BinaryPrimitives.WriteInt16LittleEndian(p.Slice(19), s.GyroZ);
        BinaryPrimitives.WriteInt16LittleEndian(p.Slice(21), s.AccelX);
        BinaryPrimitives.WriteInt16LittleEndian(p.Slice(23), s.AccelY);
        BinaryPrimitives.WriteInt16LittleEndian(p.Slice(25), s.AccelZ);
        BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(27), s.SensorTimestamp);
        WriteTouch(p.Slice(32, 4), s.Touch0); WriteTouch(p.Slice(36, 4), s.Touch1);
        p[41] = s.R2FeedbackStatus; p[42] = s.L2FeedbackStatus;
        int level = Math.Clamp((s.BatteryPercent - 5) / 10, 0, 10);
        int charge = s.BatteryStatus switch { BatteryStatus.Charging => 1, BatteryStatus.Full => 2, BatteryStatus.Error => 0xF, _ => 0 };
        p[52] = (byte)(level | (charge << 4));
        p[53] = (byte)((s.HeadphonesConnected ? 1 : 0) | (s.MicConnected ? 2 : 0) | (s.MicMuted ? 4 : 0));
        if (connection == ConnectionType.Bluetooth) Crc32.Append(Crc32.SeedInput, report);
        return report;
    }

    private static void WriteTouch(Span<byte> t, TouchPoint tp)
    {
        t[0] = (byte)((tp.Id & 0x7F) | (tp.Active ? 0 : 0x80));
        t[1] = (byte)(tp.X & 0xFF);
        t[2] = (byte)(((tp.X >> 8) & 0x0F) | ((tp.Y & 0x0F) << 4));
        t[3] = (byte)(tp.Y >> 4);
    }

    /// <summary>MAC address from feature report 0x09 (bytes 1..6, little endian).</summary>
    public static string? ParseMac(ReadOnlySpan<byte> pairingReport)
    {
        if (pairingReport.Length < 7) return null;
        var parts = new string[6];
        for (int i = 0; i < 6; i++) parts[i] = pairingReport[6 - i].ToString("X2");
        return string.Join(":", parts);
    }

    public readonly record struct FirmwareInfo(string BuildDate, uint HardwareVersion, uint FirmwareVersion)
    {
        public override string ToString() => $"0x{FirmwareVersion:X8}";
    }

    public static FirmwareInfo? ParseFirmware(ReadOnlySpan<byte> r)
    {
        if (r.Length < 32) return null;
        string date = System.Text.Encoding.ASCII.GetString(r.Slice(1, 19)).Replace('\0', ' ').Trim();
        return new FirmwareInfo(date, BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(24)), BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(28)));
    }
}

/// <summary>Factory IMU calibration from feature report 0x05.</summary>
public sealed class ImuCalibration
{
    public readonly double[] GyroScale = [1, 1, 1];   // raw -> 1/1024 deg/s units
    public readonly int[] AccelBias = new int[3];
    public readonly double[] AccelScale = [1, 1, 1];  // raw -> 1/8192 g units
    public bool Valid { get; private set; }

    public static ImuCalibration Identity => new();

    public static ImuCalibration Parse(ReadOnlySpan<byte> buf)
    {
        var c = new ImuCalibration();
        if (buf.Length < 35) return c;
        short R(int o, ReadOnlySpan<byte> b) => BinaryPrimitives.ReadInt16LittleEndian(b.Slice(o));
        int pitchBias = R(1, buf), yawBias = R(3, buf), rollBias = R(5, buf);
        int pitchPlus = R(7, buf), pitchMinus = R(9, buf), yawPlus = R(11, buf), yawMinus = R(13, buf);
        int rollPlus = R(15, buf), rollMinus = R(17, buf), speedPlus = R(19, buf), speedMinus = R(21, buf);
        int speed2x = speedPlus + speedMinus;
        int[] denom =
        [
            Math.Abs(pitchPlus - pitchBias) + Math.Abs(pitchMinus - pitchBias),
            Math.Abs(yawPlus - yawBias) + Math.Abs(yawMinus - yawBias),
            Math.Abs(rollPlus - rollBias) + Math.Abs(rollMinus - rollBias),
        ];
        for (int i = 0; i < 3; i++)
            c.GyroScale[i] = denom[i] == 0 ? 1 : (double)speed2x * DualSenseProtocol.GyroResolutionPerDegS / denom[i] / DualSenseProtocol.GyroResolutionPerDegS;
        for (int i = 0; i < 3; i++)
        {
            int plus = R(23 + i * 4, buf), minus = R(25 + i * 4, buf), range = plus - minus;
            c.AccelBias[i] = plus - range / 2;
            c.AccelScale[i] = range == 0 ? 1 : 2.0 * DualSenseProtocol.AccelResolutionPerG / range;
        }
        c.Valid = denom.All(d => d != 0);
        return c;
    }

    /// <summary>Angular velocity in degrees per second.</summary>
    public (double X, double Y, double Z) GyroDps(in InputState s) =>
        (s.GyroX * GyroScale[0] / DualSenseProtocol.GyroResolutionPerDegS,
         s.GyroY * GyroScale[1] / DualSenseProtocol.GyroResolutionPerDegS,
         s.GyroZ * GyroScale[2] / DualSenseProtocol.GyroResolutionPerDegS);

    /// <summary>Acceleration in g.</summary>
    public (double X, double Y, double Z) AccelG(in InputState s) =>
        ((s.AccelX - AccelBias[0]) * AccelScale[0] / DualSenseProtocol.AccelResolutionPerG,
         (s.AccelY - AccelBias[1]) * AccelScale[1] / DualSenseProtocol.AccelResolutionPerG,
         (s.AccelZ - AccelBias[2]) * AccelScale[2] / DualSenseProtocol.AccelResolutionPerG);
}
