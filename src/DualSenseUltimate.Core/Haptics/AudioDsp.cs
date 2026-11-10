namespace DualSenseUltimate.Core.Haptics;

/// <summary>RBJ cookbook biquad filter (transposed direct form II).</summary>
public sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2, _z1, _z2;

    public static Biquad LowPass(double sampleRate, double freq, double q = 0.707) => Design(sampleRate, freq, q, 0, 0);
    public static Biquad HighPass(double sampleRate, double freq, double q = 0.707) => Design(sampleRate, freq, q, 0, 1);
    public static Biquad LowShelf(double sampleRate, double freq, double gainDb) => Design(sampleRate, freq, 0.707, gainDb, 2);
    public static Biquad HighShelf(double sampleRate, double freq, double gainDb) => Design(sampleRate, freq, 0.707, gainDb, 3);
    public static Biquad Peaking(double sampleRate, double freq, double gainDb, double q = 0.9) => Design(sampleRate, freq, q, gainDb, 4);

    private static Biquad Design(double fs, double f, double q, double gainDb, int kind)
    {
        f = Math.Clamp(f, 1, fs * 0.49);
        double w = 2 * Math.PI * f / fs, cos = Math.Cos(w), sin = Math.Sin(w), alpha = sin / (2 * q);
        double a = Math.Pow(10, gainDb / 40), sq = 2 * Math.Sqrt(a) * alpha;
        double b0, b1, b2, a0, a1, a2;
        switch (kind)
        {
            case 0: b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = b0; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            case 1: b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = b0; a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha; break;
            case 2:
                b0 = a * ((a + 1) - (a - 1) * cos + sq); b1 = 2 * a * ((a - 1) - (a + 1) * cos); b2 = a * ((a + 1) - (a - 1) * cos - sq);
                a0 = (a + 1) + (a - 1) * cos + sq; a1 = -2 * ((a - 1) + (a + 1) * cos); a2 = (a + 1) + (a - 1) * cos - sq; break;
            case 3:
                b0 = a * ((a + 1) + (a - 1) * cos + sq); b1 = -2 * a * ((a - 1) + (a + 1) * cos); b2 = a * ((a + 1) + (a - 1) * cos - sq);
                a0 = (a + 1) - (a - 1) * cos + sq; a1 = 2 * ((a - 1) - (a + 1) * cos); a2 = (a + 1) - (a - 1) * cos - sq; break;
            default:
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a; a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a; break;
        }
        return new Biquad { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0 };
    }

    public float Process(float x)
    {
        double y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return (float)y;
    }
}

/// <summary>Audio processing chain for haptics: 3 band EQ, low-pass, gain, soft limiter and an envelope
/// follower. The processed signal feeds the native haptic channels (USB); the envelope drives
/// classic rumble and audio-reactive lighting.</summary>
public sealed class HapticAudioProcessor
{
    private readonly Biquad _bass, _mid, _treble, _lowPass;
    private readonly double _attack, _release;
    private double _envelope;
    public double Gain { get; set; } = 1.0;
    /// <summary>Current signal envelope, 0..1.</summary>
    public double Envelope => _envelope;

    public HapticAudioProcessor(int sampleRate, HapticsConfig cfg)
    {
        _bass = Biquad.LowShelf(sampleRate, 120, cfg.BassGainDb);
        _mid = Biquad.Peaking(sampleRate, 400, cfg.MidGainDb);
        _treble = Biquad.HighShelf(sampleRate, 1000, cfg.TrebleGainDb);
        _lowPass = Biquad.LowPass(sampleRate, Math.Clamp(cfg.AudioCutoffHz, 20, 2000));
        Gain = Math.Clamp(cfg.AudioReactiveGain, 0, 400) / 100.0;
        _attack = Math.Exp(-1.0 / (0.005 * sampleRate));
        _release = Math.Exp(-1.0 / (0.080 * sampleRate));
    }

    /// <summary>Processes mono samples in place.</summary>
    public void Process(Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            float x = _lowPass.Process(_treble.Process(_mid.Process(_bass.Process(mono[i]))));
            x = (float)Math.Tanh(x * Gain); // soft limiter keeps the actuators inside their range
            mono[i] = x;
            double a = Math.Abs(x);
            _envelope = a > _envelope ? _attack * _envelope + (1 - _attack) * a : _release * _envelope + (1 - _release) * a;
        }
    }

    /// <summary>Maps the envelope to classic rumble values: heavy motor follows the bass envelope,
    /// light motor gets a reduced share.</summary>
    public (byte Left, byte Right) EnvelopeToRumble()
    {
        double e = Math.Clamp(_envelope * 2.2, 0, 1);
        if (e < 0.04) return (0, 0);
        return ((byte)(e * 255), (byte)(e * 140));
    }

    /// <summary>Test signals for the channel test: sine or sweep, -6 dBFS.</summary>
    public static float[] TestTone(int sampleRate, double seconds, double startHz, double endHz)
    {
        int n = (int)(sampleRate * seconds);
        var buf = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double f = startHz + (endHz - startHz) * i / Math.Max(1, n - 1);
            phase += 2 * Math.PI * f / sampleRate;
            double fade = Math.Min(1, Math.Min(i, n - 1 - i) / (0.01 * sampleRate));
            buf[i] = (float)(Math.Sin(phase) * 0.5 * fade);
        }
        return buf;
    }
}

/// <summary>Sink for native audio haptics (implemented by the platform layer where available).</summary>
public interface IHapticAudioOutput : IDisposable
{
    /// <summary>Human readable availability, e.g. the audio endpoint in use or the reason it is unavailable.</summary>
    string Status { get; }
    bool IsAvailable { get; }
    /// <summary>Queues mono samples for the left and/or right actuator.</summary>
    void Play(float[] samples, int sampleRate, bool left, bool right);
    void Stop();
}

/// <summary>Source of system audio (loopback) levels for reactive effects.</summary>
public interface IAudioLevelSource : IDisposable
{
    bool IsAvailable { get; }
    /// <summary>Raised with mono float samples and their sample rate.</summary>
    event Action<float[], int>? Samples;
    void Start();
    void Stop();
}
