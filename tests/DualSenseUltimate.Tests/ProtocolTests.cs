using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Tests;

public class ProtocolTests
{
    [Fact]
    public void Crc32_matches_standard_check_value()
    {
        // CRC-32("123456789") = 0xCBF43926; the seed is simply the first hashed byte.
        Assert.Equal(0xCBF43926u, Crc32.Compute((byte)'1', "23456789"u8));
    }

    [Theory]
    [InlineData(ConnectionType.Usb)]
    [InlineData(ConnectionType.Bluetooth)]
    public void Input_report_round_trips(ConnectionType conn)
    {
        var s = InputState.Neutral;
        s.LeftX = 10; s.LeftY = 250; s.RightX = 77; s.RightY = 200; s.L2 = 33; s.R2 = 255;
        s.Buttons = PadButtons.Cross | PadButtons.L1 | PadButtons.DpadUp | PadButtons.DpadRight | PadButtons.PS | PadButtons.Mute | PadButtons.R3;
        s.GyroX = -1234; s.GyroY = 4321; s.GyroZ = 7; s.AccelX = 100; s.AccelY = 8192; s.AccelZ = -300;
        s.Touch0 = new TouchPoint(true, 5, 1919, 1079); s.Touch1 = new TouchPoint(false, 0, 0, 0);
        s.BatteryPercent = 65; s.BatteryStatus = BatteryStatus.Charging; s.HeadphonesConnected = true; s.MicMuted = true;
        var report = DualSenseProtocol.BuildInput(s, conn);
        Assert.Equal(conn == ConnectionType.Usb ? 64 : 78, report.Length);
        Assert.True(DualSenseProtocol.TryParseInput(report, conn, false, out var p));
        Assert.Equal(s.LeftX, p.LeftX); Assert.Equal(s.RightY, p.RightY); Assert.Equal(s.L2, p.L2); Assert.Equal(s.R2, p.R2);
        Assert.Equal(s.Buttons, p.Buttons);
        Assert.Equal(s.GyroX, p.GyroX); Assert.Equal(s.GyroY, p.GyroY); Assert.Equal(s.AccelY, p.AccelY);
        Assert.Equal(s.Touch0, p.Touch0); Assert.False(p.Touch1.Active);
        Assert.Equal(65, p.BatteryPercent); Assert.Equal(BatteryStatus.Charging, p.BatteryStatus);
        Assert.True(p.HeadphonesConnected); Assert.True(p.MicMuted);
    }

    [Fact]
    public void Usb_input_uses_documented_offsets()
    {
        // Hand-built report following hid-playstation.c: id, x, y, rx, ry, z, rz, seq, buttons[4]...
        var r = new byte[64];
        r[0] = 0x01; r[1] = 0x80; r[2] = 0x7F; r[3] = 0x10; r[4] = 0xF0; r[5] = 0x40; r[6] = 0xC0;
        r[8] = 0x28;        // hat 8 (neutral) + cross
        r[9] = 0x01 | 0x20; // L1 + options
        r[10] = 0x02;       // touchpad click
        r[53] = 0x08;       // status0: level 8, discharging -> 85 %
        r[33] = 0x80; r[37] = 0x80; // touch points inactive
        Assert.True(DualSenseProtocol.TryParseInput(r, ConnectionType.Usb, false, out var s));
        Assert.Equal(0x80, s.LeftX); Assert.Equal(0x10, s.RightX); Assert.Equal(0x40, s.L2); Assert.Equal(0xC0, s.R2);
        Assert.Equal(PadButtons.Cross | PadButtons.L1 | PadButtons.Options | PadButtons.Touchpad, s.Buttons);
        Assert.Equal(85, s.BatteryPercent); Assert.Equal(BatteryStatus.Discharging, s.BatteryStatus);
    }

    [Fact]
    public void Bluetooth_input_with_bad_crc_is_rejected()
    {
        var report = DualSenseProtocol.BuildInput(InputState.Neutral, ConnectionType.Bluetooth);
        report[10] ^= 0xFF;
        Assert.False(DualSenseProtocol.TryParseInput(report, ConnectionType.Bluetooth, false, out _));
    }

    [Fact]
    public void Short_bluetooth_report_is_rejected()
    {
        // Before feature report 0x05 is read, BT controllers send a 10 byte 0x01 report.
        Assert.False(DualSenseProtocol.TryParseInput(new byte[10] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, ConnectionType.Bluetooth, false, out _));
    }

    [Fact]
    public void Edge_buttons_only_parsed_for_edge()
    {
        var s = InputState.Neutral; s.Buttons = PadButtons.PaddleLeft | PadButtons.FnRight;
        var report = DualSenseProtocol.BuildInput(s, ConnectionType.Usb, isEdge: true);
        Assert.True(DualSenseProtocol.TryParseInput(report, ConnectionType.Usb, true, out var edge));
        Assert.Equal(PadButtons.PaddleLeft | PadButtons.FnRight, edge.Buttons);
        Assert.True(DualSenseProtocol.TryParseInput(report, ConnectionType.Usb, false, out var plain));
        Assert.Equal(PadButtons.None, plain.Buttons);
    }

    [Fact]
    public void Usb_output_report_layout()
    {
        var o = new OutputState { MotorLeft = 200, MotorRight = 100, Red = 1, Green = 2, Blue = 3, PlayerLeds = 0b00100, MicLed = MicLedMode.Pulse, PlayerLedBrightness = LedBrightness.Low };
        o.RightTrigger = TriggerEffectEncoder.Encode(new TriggerEffect { Type = TriggerEffectType.Weapon, Start = 2, End = 5, Strength = 8 });
        var r = DualSenseProtocol.BuildOutput(o, ConnectionType.Usb);
        Assert.Equal(48, r.Length);
        Assert.Equal(0x02, r[0]);
        Assert.Equal(0x0F, r[1]);              // vibration + haptics select + both triggers
        Assert.Equal(0x01 | 0x04 | 0x10, r[2]); // mic led + lightbar + player leds
        Assert.Equal(100, r[3]); Assert.Equal(200, r[4]);
        Assert.Equal(2, r[9]);                 // mute LED pulse
        Assert.Equal(0x25, r[11]);             // right trigger mode: weapon
        Assert.Equal(0x05, r[22]);             // left trigger mode: off
        Assert.Equal(2, r[43]);                // LED brightness low
        Assert.Equal(0b00100 | 0x20, r[44]);
        Assert.Equal(1, r[45]); Assert.Equal(2, r[46]); Assert.Equal(3, r[47]);
    }

    [Fact]
    public void Bluetooth_output_report_has_header_and_valid_crc()
    {
        var r = DualSenseProtocol.BuildOutput(new OutputState { Red = 255 }, ConnectionType.Bluetooth, sequence: 5);
        Assert.Equal(78, r.Length);
        Assert.Equal(0x31, r[0]); Assert.Equal(0x50, r[1]); Assert.Equal(0x10, r[2]);
        Assert.Equal(255, r[3 + 44]);
        Assert.True(Crc32.Verify(Crc32.SeedOutput, r));
        Assert.True(DualSenseProtocol.TryParseOutput(r, ConnectionType.Bluetooth, out var back));
        Assert.Equal(255, back.Red);
    }

    [Fact]
    public void Unowned_fields_do_not_set_valid_flags()
    {
        var o = new OutputState { Red = 9, MotorLeft = 50, Ownership = OutputOwnership.Lightbar };
        var r = DualSenseProtocol.BuildOutput(o, ConnectionType.Usb);
        Assert.Equal(0, r[1]);       // no rumble / trigger flags: the game keeps control of them
        Assert.Equal(0x04, r[2]);    // lightbar only
        Assert.Equal(0, r[4]);       // motor value not written
        Assert.Equal(0, r[11]);      // trigger block untouched
    }

    [Fact]
    public void Release_report_sets_release_leds_and_stops_effects()
    {
        var r = DualSenseProtocol.BuildRelease(ConnectionType.Bluetooth);
        Assert.True(Crc32.Verify(Crc32.SeedOutput, r));
        Assert.True((r[4] & 0x08) != 0);
        Assert.Equal(0x05, r[3 + 10]); Assert.Equal(0x05, r[3 + 21]);
        Assert.Equal(0, r[3 + 2]); Assert.Equal(0, r[3 + 3]);
    }

    [Fact]
    public void Mac_and_firmware_parsing()
    {
        var pairing = new byte[20]; pairing[0] = 9;
        byte[] mac = [0x66, 0x55, 0x44, 0x33, 0x22, 0x11];
        mac.CopyTo(pairing, 1);
        Assert.Equal("11:22:33:44:55:66", DualSenseProtocol.ParseMac(pairing));
        var fw = new byte[64]; fw[28] = 0x34; fw[29] = 0x12; fw[30] = 0x02; fw[31] = 0x01;
        Assert.Equal(0x01021234u, DualSenseProtocol.ParseFirmware(fw)!.Value.FirmwareVersion);
    }

    [Fact]
    public void DualShock4_usb_input_parses()
    {
        var r = new byte[64];
        r[0] = 0x01; r[1] = 200; r[2] = 50; r[3] = 128; r[4] = 128;
        r[5] = 0x48;   // hat neutral + circle
        r[6] = 0x02;   // R1
        r[7] = 0x01;   // PS
        r[8] = 90; r[9] = 180;
        r[30] = 0x15;  // cable + level 5 -> charging 55 %
        r[33] = 1; r[35] = 0x01; r[36] = 0x64; r[37] = 0x00; r[38] = 0x0A; // one touch report, finger at (100, 160)
        r[39] = 0x80;
        Assert.True(DualShock4Protocol.TryParseInput(r, ConnectionType.Usb, out var s));
        Assert.Equal(200, s.LeftX); Assert.Equal(90, s.L2); Assert.Equal(180, s.R2);
        Assert.Equal(PadButtons.Circle | PadButtons.R1 | PadButtons.PS, s.Buttons);
        Assert.Equal(55, s.BatteryPercent); Assert.Equal(BatteryStatus.Charging, s.BatteryStatus);
        Assert.True(s.Touch0.Active); Assert.Equal(100, s.Touch0.X); Assert.Equal(160, s.Touch0.Y);
    }

    [Fact]
    public void DualShock4_output_reports()
    {
        var o = new OutputState { MotorLeft = 10, MotorRight = 20, Red = 30, Green = 40, Blue = 50 };
        var usb = DualShock4Protocol.BuildOutput(o, ConnectionType.Usb);
        Assert.Equal(32, usb.Length); Assert.Equal(0x05, usb[0]); Assert.Equal(0x03, usb[1]);
        Assert.Equal(20, usb[4]); Assert.Equal(10, usb[5]); Assert.Equal(30, usb[6]); Assert.Equal(50, usb[8]);
        var bt = DualShock4Protocol.BuildOutput(o, ConnectionType.Bluetooth);
        Assert.Equal(78, bt.Length); Assert.Equal(0x11, bt[0]); Assert.Equal(0xC4, bt[1]);
        Assert.Equal(20, bt[6]); Assert.Equal(30, bt[8]);
        Assert.True(Crc32.Verify(Crc32.SeedOutput, bt));
    }
}

public class TriggerEffectTests
{
    private static TriggerEffect E(TriggerEffectType t, int start = 0, int end = 8, int s = 4, int s2 = 4, int f = 20, int period = 2, int f1 = 1, int f2 = 4) =>
        new() { Type = t, Start = start, End = end, Strength = s, Strength2 = s2, Frequency = f, Period = period, FirstFoot = f1, SecondFoot = f2 };

    [Fact] public void Off() => Assert.Equal(new byte[] { 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, TriggerEffectEncoder.Encode(null));

    [Fact]
    public void Feedback_full_range_max_strength() =>
        Assert.Equal(new byte[] { 0x21, 0xFF, 0x03, 0xFF, 0xFF, 0xFF, 0x3F, 0, 0, 0, 0 }, TriggerEffectEncoder.Encode(E(TriggerEffectType.Feedback, 0, s: 8)));

    [Fact]
    public void Feedback_from_position_5_strength_3()
    {
        // zones 5..9 active = 0x3E0; strength value 2 in 3-bit fields 5..9.
        uint zones = 0; for (int i = 5; i < 10; i++) zones |= 2u << (3 * i);
        var b = TriggerEffectEncoder.Encode(E(TriggerEffectType.Feedback, 5, s: 3));
        Assert.Equal(new byte[] { 0x21, 0xE0, 0x03, (byte)zones, (byte)(zones >> 8), (byte)(zones >> 16), (byte)(zones >> 24), 0, 0, 0, 0 }, b);
    }

    [Fact] public void Weapon() => Assert.Equal(new byte[] { 0x25, 0x24, 0x00, 0x07, 0, 0, 0, 0, 0, 0, 0 }, TriggerEffectEncoder.Encode(E(TriggerEffectType.Weapon, 2, 5, 8)));
    [Fact] public void Bow() => Assert.Equal(new byte[] { 0x22, 0x42, 0x00, 0x3D, 0, 0, 0, 0, 0, 0, 0 }, TriggerEffectEncoder.Encode(E(TriggerEffectType.Bow, 1, 6, 6, 8)));
    [Fact] public void Galloping() => Assert.Equal(new byte[] { 0x23, 0x01, 0x02, 0x0D, 4, 0, 0, 0, 0, 0, 0 }, TriggerEffectEncoder.Encode(E(TriggerEffectType.Galloping, 0, 9, f: 4, f1: 1, f2: 5)));
    [Fact] public void Machine() => Assert.Equal(new byte[] { 0x27, 0x04, 0x02, 0x1F, 14, 2, 0, 0, 0, 0, 0 }, TriggerEffectEncoder.Encode(E(TriggerEffectType.Machine, 2, 9, 7, 3, 14, 2)));

    [Fact]
    public void Vibration_puts_frequency_in_ninth_parameter()
    {
        var b = TriggerEffectEncoder.Encode(E(TriggerEffectType.Vibration, 9, s: 1, f: 40));
        Assert.Equal(0x26, b[0]); Assert.Equal(0x00, b[1]); Assert.Equal(0x02, b[2]); Assert.Equal(40, b[9]);
    }

    [Fact]
    public void Slope_interpolates_between_strengths()
    {
        var curve = TriggerEffectEncoder.ForceCurve(E(TriggerEffectType.SlopeFeedback, 0, 9, 1, 8));
        Assert.True(curve[0] < curve[4] && curve[4] < curve[9]);
        Assert.Equal(0x21, TriggerEffectEncoder.Encode(E(TriggerEffectType.SlopeFeedback, 0, 9, 1, 8))[0]);
    }

    [Fact]
    public void Out_of_range_values_are_clamped_not_thrown()
    {
        var b = TriggerEffectEncoder.Encode(E(TriggerEffectType.Weapon, -50, 999, 9999));
        Assert.Equal(new byte[] { 0x25, 0x04, 0x01, 0x07 }, b[..4]); // start 2, end 8, strength 8
        var all = Enum.GetValues<TriggerEffectType>();
        foreach (var t in all)
            Assert.Equal(11, TriggerEffectEncoder.Encode(new TriggerEffect { Type = t, Start = 99, End = -3, Strength = 100, Strength2 = -1, Frequency = 100000, Zones = [99, -1], RawBytes = [1, 2] }).Length);
    }

    [Fact]
    public void Multi_zone_with_no_active_zone_is_off() =>
        Assert.Equal(0x05, TriggerEffectEncoder.Encode(new TriggerEffect { Type = TriggerEffectType.MultiZoneFeedback })[0]);

    [Fact]
    public void Raw_bytes_pass_through()
    {
        var raw = new byte[] { 0x01, 0x10, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0 };
        Assert.Equal(raw, TriggerEffectEncoder.Encode(new TriggerEffect { Type = TriggerEffectType.Raw, RawBytes = raw }));
    }
}
