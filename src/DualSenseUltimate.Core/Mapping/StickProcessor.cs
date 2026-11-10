namespace DualSenseUltimate.Core.Mapping;

/// <summary>Stick shaping: centre offset, radial dead zone, saturation, response curve, sensitivity, anti dead zone.</summary>
public static class StickProcessor
{
    /// <summary>Converts a raw 0-255 axis pair into shaped -1..1 values. Positive Y is up.</summary>
    public static (double X, double Y) Apply(StickConfig c, byte rawX, byte rawY)
    {
        double x = Normalize(rawX) - c.CenterX;
        double y = -(Normalize(rawY) - c.CenterY); // raw Y grows downwards
        double m = Math.Sqrt(x * x + y * y);
        double dz = Math.Clamp(c.DeadZone, 0, 0.95), max = Math.Clamp(c.MaxZone, dz + 0.01, 1.5);
        if (m <= dz || m < 1e-9) return (0, 0);
        double n = Math.Clamp((m - dz) / (max - dz), 0, 1);
        n = Curve(c, n);
        n = Math.Clamp(n * Math.Max(0, c.Sensitivity), 0, 1);
        double adz = Math.Clamp(c.AntiDeadZone, 0, 0.9);
        if (n > 0) n = adz + (1 - adz) * n;
        double ox = x / m * n, oy = y / m * n;
        if (c.InvertX) ox = -ox;
        if (c.InvertY) oy = -oy;
        return (Math.Clamp(ox, -1, 1), Math.Clamp(oy, -1, 1));
    }

    public static double Normalize(byte raw) => Math.Clamp((raw - 128) / 127.0, -1, 1);

    public static double Curve(StickConfig c, double n) => c.Curve switch
    {
        ResponseCurve.Quadratic => n * n,
        ResponseCurve.Cubic => n * n * n,
        ResponseCurve.EaseOut => Math.Sqrt(n),
        ResponseCurve.Custom => Math.Pow(n, Math.Clamp(c.CurveExponent, 0.2, 5)),
        _ => n,
    };

    public static double ApplyTrigger(TriggerAxisConfig c, byte raw)
    {
        double v = raw / 255.0, dz = Math.Clamp(c.DeadZone, 0, 0.95), max = Math.Clamp(c.MaxZone, dz + 0.01, 1);
        return v <= dz ? 0 : Math.Clamp((v - dz) / (max - dz), 0, 1);
    }
}

/// <summary>Collects resting samples to compute the stick centre and a dead zone that hides drift.</summary>
public sealed class StickCalibrator
{
    private double _sumX, _sumY; private int _n;
    private readonly List<(double x, double y)> _samples = new();

    public void AddSample(byte rawX, byte rawY)
    {
        double x = StickProcessor.Normalize(rawX), y = StickProcessor.Normalize(rawY);
        _sumX += x; _sumY += y; _n++; _samples.Add((x, y));
    }

    public int SampleCount => _n;

    /// <summary>Returns centre offset and suggested dead zone (max observed jitter * 1.5 + margin).</summary>
    public (double CenterX, double CenterY, double SuggestedDeadZone) Compute()
    {
        if (_n == 0) return (0, 0, 0.06);
        double cx = _sumX / _n, cy = _sumY / _n;
        double jitter = _samples.Max(s => Math.Sqrt((s.x - cx) * (s.x - cx) + (s.y - cy) * (s.y - cy)));
        return (cx, cy, Math.Clamp(jitter * 1.5 + 0.02, 0.03, 0.4));
    }
}
