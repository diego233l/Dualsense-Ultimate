namespace DualSenseUltimate.Core.Haptics;

public sealed class HapticStep
{
    public int DurationMs { get; set; } = 100;
    /// <summary>Left (heavy, low frequency) actuator 0-255.</summary>
    public int Left { get; set; }
    /// <summary>Right (light, high frequency) actuator 0-255.</summary>
    public int Right { get; set; }
}

public sealed class HapticPattern
{
    public string Name { get; set; } = "";
    public List<HapticStep> Steps { get; set; } = [];
    /// <summary>Number of times to play; 0 = loop until stopped.</summary>
    public int Repeat { get; set; } = 1;
    public int TotalMs => Steps.Sum(s => Math.Max(1, s.DurationMs));

    public HapticPattern Clone() => new()
    {
        Name = Name, Repeat = Repeat,
        Steps = Steps.Select(s => new HapticStep { DurationMs = s.DurationMs, Left = s.Left, Right = s.Right }).ToList(),
    };

    public static HapticPattern Make(string name, int repeat, params (int ms, int l, int r)[] steps) => new()
    {
        Name = name, Repeat = repeat, Steps = steps.Select(s => new HapticStep { DurationMs = s.ms, Left = s.l, Right = s.r }).ToList(),
    };

    public static IReadOnlyList<HapticPattern> BuiltIn { get; } =
    [
        Make("Clic", 1, (40, 0, 200)),
        Make("Doble clic", 1, (40, 0, 200), (80, 0, 0), (40, 0, 200)),
        Make("Notificación", 1, (80, 60, 160), (70, 0, 0), (120, 120, 60)),
        Make("Latido", 3, (90, 220, 0), (110, 0, 0), (70, 140, 0), (500, 0, 0)),
        Make("Impacto", 1, (60, 255, 255), (80, 180, 90), (120, 90, 0), (160, 40, 0)),
        Make("Explosión", 1, (120, 255, 255), (150, 220, 140), (200, 150, 60), (250, 80, 0), (300, 30, 0)),
        Make("Motor", 0, (60, 110, 40), (60, 140, 60)),
        Make("Rampa", 1, (80, 30, 0), (80, 70, 20), (80, 110, 50), (80, 160, 90), (80, 210, 140), (120, 255, 200)),
        Make("Alarma", 4, (150, 0, 255), (150, 0, 0)),
    ];
}

/// <summary>Time-driven pattern playback. Deterministic: call <see cref="Advance"/> with elapsed milliseconds.</summary>
public sealed class HapticPlayer
{
    private HapticPattern? _pattern;
    private int _step, _loops;
    private double _elapsedInStep;
    public bool IsPlaying => _pattern != null;
    public string? CurrentName => _pattern?.Name;

    public void Play(HapticPattern pattern)
    {
        if (pattern.Steps.Count == 0) { Stop(); return; }
        _pattern = pattern.Clone(); _step = 0; _loops = 0; _elapsedInStep = 0;
    }

    public void Stop() => _pattern = null;

    /// <summary>Advances time and returns the motor values to apply now (0,0 when idle).</summary>
    public (byte Left, byte Right) Advance(double dtMs)
    {
        if (_pattern == null) return (0, 0);
        _elapsedInStep += dtMs;
        while (_pattern != null && _elapsedInStep >= Math.Max(1, _pattern.Steps[_step].DurationMs))
        {
            _elapsedInStep -= Math.Max(1, _pattern.Steps[_step].DurationMs);
            if (++_step >= _pattern.Steps.Count)
            {
                _step = 0;
                if (_pattern.Repeat > 0 && ++_loops >= _pattern.Repeat) _pattern = null;
            }
        }
        if (_pattern == null) return (0, 0);
        var s = _pattern.Steps[_step];
        return ((byte)Math.Clamp(s.Left, 0, 255), (byte)Math.Clamp(s.Right, 0, 255));
    }
}

public sealed class HapticsConfig
{
    /// <summary>Global rumble strength 0..100.</summary>
    public int Intensity { get; set; } = 100;
    /// <summary>Convert system audio (WASAPI loopback) into rumble.</summary>
    public bool AudioReactive { get; set; }
    public int AudioReactiveGain { get; set; } = 100;
    /// <summary>Low-pass cutoff in Hz for the audio-to-haptics path (bass drives the actuators best).</summary>
    public int AudioCutoffHz { get; set; } = 160;
    /// <summary>Send system audio to the native haptic actuators (USB only).</summary>
    public bool NativeAudioHaptics { get; set; }
    public int BassGainDb { get; set; }
    public int MidGainDb { get; set; }
    public int TrebleGainDb { get; set; }

    public HapticsConfig Clone() => (HapticsConfig)MemberwiseClone();
}

/// <summary>Combines rumble requests from independent sources (pattern, audio, virtual pad, API). The
/// strongest request wins per motor, so modules never fight over the hardware.</summary>
public sealed class RumbleMixer
{
    private readonly Dictionary<string, (byte L, byte R)> _sources = new();
    public void Set(string source, byte left, byte right)
    {
        lock (_sources) { if (left == 0 && right == 0) _sources.Remove(source); else _sources[source] = (left, right); }
    }
    public void Clear() { lock (_sources) _sources.Clear(); }
    public (byte Left, byte Right) Mix(int intensityPercent)
    {
        byte l = 0, r = 0;
        lock (_sources) foreach (var v in _sources.Values) { l = Math.Max(l, v.L); r = Math.Max(r, v.R); }
        double k = Math.Clamp(intensityPercent, 0, 100) / 100.0;
        return ((byte)Math.Round(l * k), (byte)Math.Round(r * k));
    }
}
