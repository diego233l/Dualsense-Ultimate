using System.Buffers.Binary;

namespace DualSenseUltimate.Core.Protocol;

/// <summary>DualShock 4 HID report codec (layout from hid-playstation.c).</summary>
public static class DualShock4Protocol
{
    public const byte InputReportUsb = 0x01, InputReportBt = 0x11;
    public const int InputReportUsbSize = 64, InputReportBtSize = 78;
    public const byte OutputReportUsb = 0x05, OutputReportBt = 0x11;
    public const int OutputReportUsbSize = 32, OutputReportBtSize = 78;
    public const byte FeatureCalibrationUsb = 0x02, FeatureCalibrationBt = 0x05, FeaturePairing = 0x12, FeatureFirmware = 0xA3;
    public const int TouchpadWidth = 1920, TouchpadHeight = 942;

    public static bool TryParseInput(ReadOnlySpan<byte> report, ConnectionType connection, out InputState state)
    {
        state = InputState.Neutral;
        ReadOnlySpan<byte> c, touch; int touchCount;
        if (connection == ConnectionType.Usb)
        {
            if (report.Length < InputReportUsbSize || report[0] != InputReportUsb) return false;
            c = report.Slice(1, 32); touchCount = Math.Min((int)report[33], 3); touch = report.Slice(34);
        }
        else
        {
            if (report.Length < InputReportBtSize || report[0] != InputReportBt) return false;
            if (!Crc32.Verify(Crc32.SeedInput, report[..InputReportBtSize])) return false;
            c = report.Slice(3, 32); touchCount = Math.Min((int)report[35], 4); touch = report.Slice(36);
        }
        state.LeftX = c[0]; state.LeftY = c[1]; state.RightX = c[2]; state.RightY = c[3];
        byte b0 = c[4], b1 = c[5], b2 = c[6];
        state.L2 = c[7]; state.R2 = c[8];
        PadButtons b = DualSenseProtocol.HatToButtons(b0 & 0x0F);
        if ((b0 & 0x10) != 0) b |= PadButtons.Square;
        if ((b0 & 0x20) != 0) b |= PadButtons.Cross;
        if ((b0 & 0x40) != 0) b |= PadButtons.Circle;
        if ((b0 & 0x80) != 0) b |= PadButtons.Triangle;
        if ((b1 & 0x01) != 0) b |= PadButtons.L1;
        if ((b1 & 0x02) != 0) b |= PadButtons.R1;
        if ((b1 & 0x04) != 0) b |= PadButtons.L2;
        if ((b1 & 0x08) != 0) b |= PadButtons.R2;
        if ((b1 & 0x10) != 0) b |= PadButtons.Create;   // Share
        if ((b1 & 0x20) != 0) b |= PadButtons.Options;
        if ((b1 & 0x40) != 0) b |= PadButtons.L3;
        if ((b1 & 0x80) != 0) b |= PadButtons.R3;
        if ((b2 & 0x01) != 0) b |= PadButtons.PS;
        if ((b2 & 0x02) != 0) b |= PadButtons.Touchpad;
        state.Buttons = b;
        state.SensorTimestamp = BinaryPrimitives.ReadUInt16LittleEndian(c.Slice(9));
        state.SensorTemperatureRaw = c[11];
        state.GyroX = BinaryPrimitives.ReadInt16LittleEndian(c.Slice(12));
        state.GyroY = BinaryPrimitives.ReadInt16LittleEndian(c.Slice(14));
        state.GyroZ = BinaryPrimitives.ReadInt16LittleEndian(c.Slice(16));
        state.AccelX = BinaryPrimitives.ReadInt16LittleEndian(c.Slice(18));
        state.AccelY = BinaryPrimitives.ReadInt16LittleEndian(c.Slice(20));
        state.AccelZ = BinaryPrimitives.ReadInt16LittleEndian(c.Slice(22));

        byte status0 = c[29], status1 = c[30];
        int level = status0 & 0x0F; bool cable = (status0 & 0x10) != 0;
        if (cable)
        {
            if (level < 10) { state.BatteryPercent = level * 10 + 5; state.BatteryStatus = BatteryStatus.Charging; }
            else if (level == 10) { state.BatteryPercent = 100; state.BatteryStatus = BatteryStatus.Charging; }
            else if (level == 11) { state.BatteryPercent = 100; state.BatteryStatus = BatteryStatus.Full; }
            else { state.BatteryPercent = 0; state.BatteryStatus = BatteryStatus.Error; }
        }
        else
        {
            state.BatteryPercent = level < 10 ? level * 10 + 5 : 100;
            state.BatteryStatus = BatteryStatus.Discharging;
        }
        state.HeadphonesConnected = (status1 & 0x01) != 0;
        state.MicConnected = (status1 & 0x02) != 0;

        if (touchCount > 0 && touch.Length >= 9)
        {
            // Each touch report: 1 byte timestamp + two 4 byte points. The first report is the most recent.
            state.Touch0 = DualSenseProtocol.ParseTouch(touch.Slice(1, 4));
            state.Touch1 = DualSenseProtocol.ParseTouch(touch.Slice(5, 4));
        }
        return true;
    }

    public static byte[] BuildOutput(OutputState s, ConnectionType connection)
    {
        byte[] report; Span<byte> c;
        if (connection == ConnectionType.Usb)
        {
            report = new byte[OutputReportUsbSize];
            report[0] = OutputReportUsb;
            c = report.AsSpan(1);
        }
        else
        {
            report = new byte[OutputReportBtSize];
            report[0] = OutputReportBt;
            report[1] = 0x80 | 0x40 | 4; // HID + CRC32 + default poll interval
            c = report.AsSpan(3);
        }
        byte flags = 0;
        if (s.Ownership.HasFlag(OutputOwnership.Rumble)) { flags |= 0x01; c[3] = s.MotorRight; c[4] = s.MotorLeft; }
        if (s.Ownership.HasFlag(OutputOwnership.Lightbar)) { flags |= 0x02; c[5] = s.Red; c[6] = s.Green; c[7] = s.Blue; }
        c[0] = flags;
        if (connection == ConnectionType.Bluetooth) Crc32.Append(Crc32.SeedOutput, report);
        return report;
    }

    public static string? ParseMacUsb(ReadOnlySpan<byte> pairingReport) => DualSenseProtocol.ParseMac(pairingReport);

    public static string? ParseFirmware(ReadOnlySpan<byte> r) =>
        r.Length < 43 ? null : $"0x{BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(41)):X4}";
}
