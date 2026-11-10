namespace DualSenseUltimate.Core.Protocol;

public enum ControllerModel { DualSense, DualSenseEdge, DualShock4 }
public enum ConnectionType { Usb, Bluetooth }
public enum BatteryStatus { Unknown, Discharging, Charging, Full, Error }

/// <summary>Logical buttons shared by every supported controller.</summary>
[Flags]
public enum PadButtons : uint
{
    None = 0,
    Cross = 1 << 0, Circle = 1 << 1, Square = 1 << 2, Triangle = 1 << 3,
    L1 = 1 << 4, R1 = 1 << 5, L2 = 1 << 6, R2 = 1 << 7,
    Create = 1 << 8, Options = 1 << 9, L3 = 1 << 10, R3 = 1 << 11,
    PS = 1 << 12, Touchpad = 1 << 13, Mute = 1 << 14,
    DpadUp = 1 << 15, DpadDown = 1 << 16, DpadLeft = 1 << 17, DpadRight = 1 << 18,
    // DualSense Edge only
    FnLeft = 1 << 19, FnRight = 1 << 20, PaddleLeft = 1 << 21, PaddleRight = 1 << 22,
}

public readonly record struct TouchPoint(bool Active, byte Id, int X, int Y);

/// <summary>Decoded snapshot of one input report.</summary>
public struct InputState
{
    public byte LeftX, LeftY, RightX, RightY, L2, R2;
    public PadButtons Buttons;
    public short GyroX, GyroY, GyroZ;      // raw units, pitch / yaw / roll
    public short AccelX, AccelY, AccelZ;   // raw units
    public uint SensorTimestamp;
    public TouchPoint Touch0, Touch1;
    public int BatteryPercent;
    public BatteryStatus BatteryStatus;
    public bool HeadphonesConnected, MicConnected, MicMuted;
    /// <summary>Raw adaptive trigger status bytes (DualSense only, pending physical validation).</summary>
    public byte L2FeedbackStatus, R2FeedbackStatus;
    /// <summary>Raw sensor temperature byte. Only DualShock 4 reports one (IMU temperature, not battery).</summary>
    public byte? SensorTemperatureRaw;

    public static InputState Neutral => new()
    {
        LeftX = 128, LeftY = 128, RightX = 128, RightY = 128,
        Touch0 = new TouchPoint(false, 0, 0, 0), Touch1 = new TouchPoint(false, 0, 0, 0),
        BatteryPercent = 100, BatteryStatus = BatteryStatus.Unknown,
    };

    public readonly bool IsDown(PadButtons b) => (Buttons & b) == b;
}

public enum MicLedMode : byte { Off = 0, On = 1, Pulse = 2 }
public enum LedBrightness : byte { High = 0, Medium = 1, Low = 2 }

/// <summary>Which parts of the output report this application owns. Unowned parts are never
/// written, so a game talking to the controller natively keeps control of them (hybrid mode).</summary>
[Flags]
public enum OutputOwnership
{
    None = 0, Rumble = 1, Lightbar = 2, PlayerLeds = 4, MicLed = 8, LeftTrigger = 16, RightTrigger = 32,
    All = Rumble | Lightbar | PlayerLeds | MicLed | LeftTrigger | RightTrigger,
}

/// <summary>Desired output state for a controller.</summary>
public sealed class OutputState
{
    public byte MotorLeft, MotorRight;          // left = heavy/low frequency, right = light/high frequency
    public byte Red, Green, Blue;
    public byte PlayerLeds;                     // 5 bits
    public LedBrightness PlayerLedBrightness;
    public MicLedMode MicLed;
    public byte[] LeftTrigger = TriggerEffectEncoder.Off();   // 11 bytes: mode + 10 params
    public byte[] RightTrigger = TriggerEffectEncoder.Off();
    public OutputOwnership Ownership = OutputOwnership.All;

    public OutputState Clone() => new()
    {
        MotorLeft = MotorLeft, MotorRight = MotorRight, Red = Red, Green = Green, Blue = Blue,
        PlayerLeds = PlayerLeds, PlayerLedBrightness = PlayerLedBrightness, MicLed = MicLed,
        LeftTrigger = (byte[])LeftTrigger.Clone(), RightTrigger = (byte[])RightTrigger.Clone(), Ownership = Ownership,
    };

    public bool ContentEquals(OutputState o) =>
        MotorLeft == o.MotorLeft && MotorRight == o.MotorRight && Red == o.Red && Green == o.Green && Blue == o.Blue &&
        PlayerLeds == o.PlayerLeds && PlayerLedBrightness == o.PlayerLedBrightness && MicLed == o.MicLed &&
        Ownership == o.Ownership && LeftTrigger.AsSpan().SequenceEqual(o.LeftTrigger) && RightTrigger.AsSpan().SequenceEqual(o.RightTrigger);
}

public static class SonyIds
{
    public const int VendorId = 0x054C;
    public const int DualSense = 0x0CE6;
    public const int DualSenseEdge = 0x0DF2;
    public const int DualShock4V1 = 0x05C4;
    public const int DualShock4V2 = 0x09CC;
    public const int DualShock4Dongle = 0x0BA0;

    public static ControllerModel? ModelFor(int vid, int pid) => vid != VendorId ? null : pid switch
    {
        DualSense => ControllerModel.DualSense,
        DualSenseEdge => ControllerModel.DualSenseEdge,
        DualShock4V1 or DualShock4V2 or DualShock4Dongle => ControllerModel.DualShock4,
        _ => null,
    };
}
