namespace DualSenseUltimate.Core.Protocol;

public enum TriggerEffectType
{
    Off,
    /// <summary>Constant resistance from a start position (Sony "Feedback").</summary>
    Feedback,
    /// <summary>Resistance that grows or shrinks along the travel.</summary>
    SlopeFeedback,
    /// <summary>Independent resistance for each of the 10 zones.</summary>
    MultiZoneFeedback,
    /// <summary>Gun trigger: resistance then release ("Weapon").</summary>
    Weapon,
    /// <summary>Bow string: tension then snap back.</summary>
    Bow,
    /// <summary>Vibration from a start position.</summary>
    Vibration,
    /// <summary>Independent vibration amplitude per zone.</summary>
    MultiZoneVibration,
    /// <summary>Two-beat pulsing ("Galloping").</summary>
    Galloping,
    /// <summary>Alternating two-strength vibration ("Machine").</summary>
    Machine,
    /// <summary>Raw mode byte + 10 parameter bytes for experimentation.</summary>
    Raw,
}

/// <summary>Serializable, hardware independent description of an adaptive trigger effect.</summary>
public sealed class TriggerEffect
{
    public TriggerEffectType Type { get; set; } = TriggerEffectType.Off;
    public string Name { get; set; } = "";
    /// <summary>Start zone 0-9.</summary>
    public int Start { get; set; }
    /// <summary>End zone 0-9.</summary>
    public int End { get; set; } = 8;
    /// <summary>Main strength / amplitude 1-8.</summary>
    public int Strength { get; set; } = 4;
    /// <summary>Secondary strength: end strength (slope), snap force (bow), amplitude B (machine).</summary>
    public int Strength2 { get; set; } = 4;
    /// <summary>Frequency in Hz for vibrating effects.</summary>
    public int Frequency { get; set; } = 20;
    /// <summary>Period in tenths of a second (machine).</summary>
    public int Period { get; set; } = 2;
    public int FirstFoot { get; set; } = 1;
    public int SecondFoot { get; set; } = 4;
    /// <summary>Per zone strength 0-8 for the multi zone effects.</summary>
    public int[] Zones { get; set; } = new int[10];
    /// <summary>Raw: [0] = mode, [1..10] = parameters.</summary>
    public byte[] RawBytes { get; set; } = new byte[11];

    public TriggerEffect Clone() => new()
    {
        Type = Type, Name = Name, Start = Start, End = End, Strength = Strength, Strength2 = Strength2, Frequency = Frequency,
        Period = Period, FirstFoot = FirstFoot, SecondFoot = SecondFoot, Zones = (int[])Zones.Clone(), RawBytes = (byte[])RawBytes.Clone(),
    };

    public static TriggerEffect OffEffect => new() { Type = TriggerEffectType.Off, Name = "Off" };
}

/// <summary>Encodes <see cref="TriggerEffect"/> into the 11 byte block of the DualSense output report.
/// Encodings follow the community documented "trigger effect factory" (Nielk1) as implemented by dualsensectl.</summary>
public static class TriggerEffectEncoder
{
    public const byte ModeOff = 0x05, ModeFeedback = 0x21, ModeBow = 0x22, ModeGalloping = 0x23,
        ModeWeapon = 0x25, ModeVibration = 0x26, ModeMachine = 0x27;
    public const int BlockSize = 11;

    public static byte[] Off() { var b = new byte[BlockSize]; b[0] = ModeOff; return b; }

    /// <summary>Encodes the effect. Out of range parameters are clamped to the nearest valid value, so
    /// an imported or hand edited effect can never produce an undefined report.</summary>
    public static byte[] Encode(TriggerEffect? e)
    {
        if (e is null) return Off();
        switch (e.Type)
        {
            case TriggerEffectType.Feedback:
            {
                int pos = Math.Clamp(e.Start, 0, 9), s = Math.Clamp(e.Strength, 1, 8);
                var z = new int[10];
                for (int i = pos; i < 10; i++) z[i] = s;
                return PackZones(ModeFeedback, z, 0);
            }
            case TriggerEffectType.SlopeFeedback:
            {
                int start = Math.Clamp(e.Start, 0, 8), end = Math.Clamp(e.End, start + 1, 9);
                int s1 = Math.Clamp(e.Strength, 1, 8), s2 = Math.Clamp(e.Strength2, 1, 8);
                var z = new int[10];
                for (int i = start; i < 10; i++)
                    z[i] = i <= end ? (int)Math.Round(s1 + (s2 - s1) * (double)(i - start) / (end - start)) : s2;
                return PackZones(ModeFeedback, z, 0);
            }
            case TriggerEffectType.MultiZoneFeedback:
                return PackZones(ModeFeedback, ClampZones(e.Zones), 0);
            case TriggerEffectType.Vibration:
            {
                int pos = Math.Clamp(e.Start, 0, 9), a = Math.Clamp(e.Strength, 1, 8);
                var z = new int[10];
                for (int i = pos; i < 10; i++) z[i] = a;
                return PackZones(ModeVibration, z, (byte)Math.Clamp(e.Frequency, 1, 255));
            }
            case TriggerEffectType.MultiZoneVibration:
                return PackZones(ModeVibration, ClampZones(e.Zones), (byte)Math.Clamp(e.Frequency, 1, 255));
            case TriggerEffectType.Weapon:
            {
                int start = Math.Clamp(e.Start, 2, 7), end = Math.Clamp(e.End, start + 1, 8);
                int s = Math.Clamp(e.Strength, 1, 8);
                ushort zones = (ushort)((1 << start) | (1 << end));
                return Block(ModeWeapon, (byte)zones, (byte)(zones >> 8), (byte)(s - 1));
            }
            case TriggerEffectType.Bow:
            {
                int start = Math.Clamp(e.Start, 1, 7), end = Math.Clamp(e.End, start + 1, 8);
                int s = Math.Clamp(e.Strength, 1, 8), snap = Math.Clamp(e.Strength2, 1, 8);
                ushort zones = (ushort)((1 << start) | (1 << end));
                int pair = ((s - 1) & 7) | (((snap - 1) & 7) << 3);
                return Block(ModeBow, (byte)zones, (byte)(zones >> 8), (byte)pair);
            }
            case TriggerEffectType.Galloping:
            {
                int start = Math.Clamp(e.Start, 0, 8), end = Math.Clamp(e.End, start + 1, 9);
                int f1 = Math.Clamp(e.FirstFoot, 0, 6), f2 = Math.Clamp(e.SecondFoot, f1 + 1, 7);
                ushort zones = (ushort)((1 << start) | (1 << end));
                int ratio = (f2 & 7) | ((f1 & 7) << 3);
                return Block(ModeGalloping, (byte)zones, (byte)(zones >> 8), (byte)ratio, (byte)Math.Clamp(e.Frequency, 1, 255));
            }
            case TriggerEffectType.Machine:
            {
                int start = Math.Clamp(e.Start, 1, 8), end = Math.Clamp(e.End, start + 1, 9);
                int a = Math.Clamp(e.Strength, 0, 7), b = Math.Clamp(e.Strength2, 0, 7);
                ushort zones = (ushort)((1 << start) | (1 << end));
                int pair = (a & 7) | ((b & 7) << 3);
                return Block(ModeMachine, (byte)zones, (byte)(zones >> 8), (byte)pair,
                    (byte)Math.Clamp(e.Frequency, 1, 255), (byte)Math.Clamp(e.Period, 0, 255));
            }
            case TriggerEffectType.Raw:
            {
                var b = new byte[BlockSize];
                (e.RawBytes ?? []).AsSpan(0, Math.Min(BlockSize, e.RawBytes?.Length ?? 0)).CopyTo(b);
                return b;
            }
            default:
                return Off();
        }
    }

    /// <summary>Approximate resistance (0..1) felt at each of the 10 zones, for the visual editor only.</summary>
    public static double[] ForceCurve(TriggerEffect e)
    {
        var c = new double[10];
        switch (e.Type)
        {
            case TriggerEffectType.Feedback: case TriggerEffectType.Vibration:
                for (int i = Math.Clamp(e.Start, 0, 9); i < 10; i++) c[i] = Math.Clamp(e.Strength, 1, 8) / 8.0; break;
            case TriggerEffectType.SlopeFeedback:
            {
                int start = Math.Clamp(e.Start, 0, 8), end = Math.Clamp(e.End, start + 1, 9);
                for (int i = start; i < 10; i++)
                    c[i] = (i <= end ? e.Strength + (e.Strength2 - e.Strength) * (double)(i - start) / (end - start) : e.Strength2) / 8.0;
                break;
            }
            case TriggerEffectType.MultiZoneFeedback: case TriggerEffectType.MultiZoneVibration:
                var z = ClampZones(e.Zones); for (int i = 0; i < 10; i++) c[i] = z[i] / 8.0; break;
            case TriggerEffectType.Weapon: case TriggerEffectType.Bow: case TriggerEffectType.Galloping: case TriggerEffectType.Machine:
                for (int i = Math.Clamp(e.Start, 0, 9); i <= Math.Clamp(e.End, 0, 9); i++) c[i] = Math.Clamp(e.Strength, 1, 8) / 8.0; break;
        }
        return c;
    }

    private static int[] ClampZones(int[]? zones)
    {
        var z = new int[10];
        if (zones != null) for (int i = 0; i < Math.Min(10, zones.Length); i++) z[i] = Math.Clamp(zones[i], 0, 8);
        return z;
    }

    private static byte[] PackZones(byte mode, int[] strength, byte frequency)
    {
        uint strengthZones = 0; ushort active = 0;
        for (int i = 0; i < 10; i++)
        {
            if (strength[i] <= 0) continue;
            strengthZones |= (uint)((strength[i] - 1) & 7) << (3 * i);
            active |= (ushort)(1 << i);
        }
        if (active == 0) return Off();
        return Block(mode, (byte)active, (byte)(active >> 8), (byte)strengthZones, (byte)(strengthZones >> 8),
            (byte)(strengthZones >> 16), (byte)(strengthZones >> 24), 0, 0, frequency);
    }

    private static byte[] Block(byte mode, params byte[] p)
    {
        var b = new byte[BlockSize];
        b[0] = mode;
        p.CopyTo(b, 1);
        return b;
    }
}
