using System.Globalization;
using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Lighting;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    public static Rgb Parse(string? hex, Rgb fallback = default)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 8) hex = hex[2..]; // ignore alpha
        return hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)
            ? new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v) : fallback;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    public Rgb Scale(double f) { f = Math.Clamp(f, 0, 1); return new((byte)Math.Round(R * f), (byte)Math.Round(G * f), (byte)Math.Round(B * f)); }
    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static Rgb FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return new((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

public enum LightingMode { Off, Static, Breathing, Rainbow, ColorCycle, Pulse, AudioReactive, Battery, Custom }
public enum PlayerLedMode { Off, Player1, Player2, Player3, Player4, All, Battery, Custom }

public sealed class LightingKeyframe
{
    /// <summary>Position in the loop, 0..1.</summary>
    public double Time { get; set; }
    public string Color { get; set; } = "#FFFFFF";
}

public sealed class LightingConfig
{
    public LightingMode Mode { get; set; } = LightingMode.Static;
    public string Color { get; set; } = "#0A84FF";
    public string Color2 { get; set; } = "#BF5AF2";
    /// <summary>Loop duration in seconds for animated modes.</summary>
    public double PeriodSeconds { get; set; } = 4;
    /// <summary>0..100.</summary>
    public int Brightness { get; set; } = 100;
    /// <summary>Palette for ColorCycle.</summary>
    public List<string> Palette { get; set; } = ["#FF453A", "#FFD60A", "#30D158", "#0A84FF", "#BF5AF2"];
    /// <summary>Keyframes for Custom (linear interpolation, looping).</summary>
    public List<LightingKeyframe> Keyframes { get; set; } = [];
    public PlayerLedMode PlayerLeds { get; set; } = PlayerLedMode.Player1;
    /// <summary>5 bit mask used when PlayerLeds = Custom.</summary>
    public int PlayerLedMask { get; set; } = 0b00100;
    public LedBrightness PlayerLedBrightness { get; set; } = LedBrightness.Medium;
    public MicLedMode MicLed { get; set; } = MicLedMode.Off;
    /// <summary>Flash the light bar when a notification event arrives.</summary>
    public bool NotificationFlash { get; set; } = true;
    public string NotificationColor { get; set; } = "#FFFFFF";

    public LightingConfig Clone() => new()
    {
        Mode = Mode, Color = Color, Color2 = Color2, PeriodSeconds = PeriodSeconds, Brightness = Brightness, Palette = [.. Palette],
        Keyframes = Keyframes.Select(k => new LightingKeyframe { Time = k.Time, Color = k.Color }).ToList(),
        PlayerLeds = PlayerLeds, PlayerLedMask = PlayerLedMask, PlayerLedBrightness = PlayerLedBrightness, MicLed = MicLed,
        NotificationFlash = NotificationFlash, NotificationColor = NotificationColor,
    };
}

public readonly record struct LightingContext(int BatteryPercent, bool Charging, double AudioLevel, double SecondsSinceNotification)
{
    public static LightingContext Default => new(100, false, 0, double.MaxValue);
}

/// <summary>Pure function from (config, time, context) to LED values.</summary>
public static class LightingEngine
{
    public const double NotificationFlashSeconds = 1.2;

    public static Rgb Compute(LightingConfig c, double t, LightingContext ctx)
    {
        double period = Math.Max(0.2, c.PeriodSeconds);
        double phase = t / period % 1.0;
        Rgb baseColor = Rgb.Parse(c.Color, new Rgb(10, 132, 255)), second = Rgb.Parse(c.Color2);
        Rgb color = c.Mode switch
        {
            LightingMode.Off => Rgb.Black,
            LightingMode.Static => baseColor,
            LightingMode.Breathing => baseColor.Scale(0.5 - 0.5 * Math.Cos(phase * 2 * Math.PI)),
            LightingMode.Rainbow => Rgb.FromHsv(phase * 360, 1, 1),
            LightingMode.ColorCycle => Cycle(c.Palette, phase),
            // Sharp attack, exponential decay towards the secondary colour.
            LightingMode.Pulse => Rgb.Lerp(baseColor, second, 1 - Math.Exp(-5 * phase)),
            LightingMode.AudioReactive => Rgb.Lerp(second.Scale(0.15), baseColor, Math.Clamp(ctx.AudioLevel, 0, 1)),
            LightingMode.Battery => BatteryColor(ctx, t),
            LightingMode.Custom => Keyframes(c.Keyframes, phase, baseColor),
            _ => baseColor,
        };
        color = color.Scale(Math.Clamp(c.Brightness, 0, 100) / 100.0);
        if (c.NotificationFlash && ctx.SecondsSinceNotification >= 0 && ctx.SecondsSinceNotification < NotificationFlashSeconds)
        {
            // Three quick blinks fading out.
            double n = ctx.SecondsSinceNotification / NotificationFlashSeconds;
            double blink = Math.Sin(n * Math.PI * 6) > 0 ? 1 - n : 0;
            color = Rgb.Lerp(color, Rgb.Parse(c.NotificationColor, new Rgb(255, 255, 255)), blink);
        }
        return color;
    }

    public static Rgb BatteryColor(LightingContext ctx, double t)
    {
        double p = Math.Clamp(ctx.BatteryPercent, 0, 100) / 100.0;
        var c = Rgb.FromHsv(p * 120, 1, 1); // red -> yellow -> green
        if (ctx.Charging) c = c.Scale(0.65 + 0.35 * Math.Sin(t * 2));
        else if (ctx.BatteryPercent <= 15) c = c.Scale(Math.Sin(t * 6) > 0 ? 1 : 0.15); // low battery blink
        return c;
    }

    private static Rgb Cycle(List<string> palette, double phase)
    {
        if (palette.Count == 0) return Rgb.Black;
        double pos = phase * palette.Count;
        int i = (int)pos % palette.Count;
        return Rgb.Lerp(Rgb.Parse(palette[i]), Rgb.Parse(palette[(i + 1) % palette.Count]), pos - Math.Floor(pos));
    }

    private static Rgb Keyframes(List<LightingKeyframe> frames, double phase, Rgb fallback)
    {
        if (frames.Count == 0) return fallback;
        var f = frames.OrderBy(k => k.Time).ToList();
        if (f.Count == 1) return Rgb.Parse(f[0].Color);
        for (int i = 0; i < f.Count; i++)
        {
            var a = f[i]; var b = f[(i + 1) % f.Count];
            double ta = a.Time, tb = i + 1 < f.Count ? b.Time : b.Time + 1;
            double ph = phase < f[0].Time ? phase + 1 : phase;
            if (ph >= ta && ph <= tb) return Rgb.Lerp(Rgb.Parse(a.Color), Rgb.Parse(b.Color), tb - ta < 1e-9 ? 0 : (ph - ta) / (tb - ta));
        }
        return Rgb.Parse(f[^1].Color);
    }

    public static byte PlayerLedBits(LightingConfig c, LightingContext ctx) => c.PlayerLeds switch
    {
        PlayerLedMode.Off => 0,
        PlayerLedMode.Player1 => 0b00100,
        PlayerLedMode.Player2 => 0b01010,
        PlayerLedMode.Player3 => 0b10101,
        PlayerLedMode.Player4 => 0b11011,
        PlayerLedMode.All => 0b11111,
        PlayerLedMode.Battery => (byte)((1 << Math.Clamp((int)Math.Ceiling(ctx.BatteryPercent / 20.0), 1, 5)) - 1),
        _ => (byte)(c.PlayerLedMask & 0x1F),
    };
}
