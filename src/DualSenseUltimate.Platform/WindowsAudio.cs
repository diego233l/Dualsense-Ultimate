using System.Runtime.Versioning;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Haptics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DualSenseUltimate.Platform;

/// <summary>System audio capture (WASAPI loopback) for sound-reactive rumble and lighting.</summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiLoopbackSource : IAudioLevelSource
{
    private WasapiLoopbackCapture? _capture;
    public bool IsAvailable => true;
    public event Action<float[], int>? Samples;

    public void Start()
    {
        if (_capture != null) return;
        _capture = new WasapiLoopbackCapture();
        var fmt = _capture.WaveFormat;
        if (fmt.Encoding != WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample != 32) Log.Warn("loopback format is not 32-bit float: " + fmt);
        _capture.DataAvailable += (_, e) =>
        {
            int ch = fmt.Channels, frames = e.BytesRecorded / (4 * ch);
            if (frames == 0) return;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int c = 0; c < ch; c++) sum += BitConverter.ToSingle(e.Buffer, (i * ch + c) * 4);
                mono[i] = sum / ch;
            }
            try { Samples?.Invoke(mono, fmt.SampleRate); } catch (Exception ex) { Log.Error("audio handler failed", ex); }
        };
        _capture.RecordingStopped += (_, e) => { if (e.Exception != null) Log.Warn("loopback stopped: " + e.Exception.Message); };
        _capture.StartRecording();
    }

    public void Stop()
    {
        try { _capture?.StopRecording(); _capture?.Dispose(); } catch { }
        _capture = null;
    }

    public void Dispose() => Stop();
}

/// <summary>Native haptic audio. Over USB the DualSense is a 4 channel USB audio device: channels 1-2
/// are the speaker/headset, channels 3-4 drive the left/right voice-coil actuators. We open that
/// endpoint in shared mode and write our signal to channels 3-4 only.
/// Not possible over Bluetooth with stock Windows drivers: the controller exposes no audio endpoint there.</summary>
[SupportedOSPlatform("windows")]
public sealed class DualSenseHapticAudioOutput : IHapticAudioOutput
{
    private WasapiOut? _out;
    private BufferedWaveProvider? _buffer;
    private WaveFormat? _format;
    private string _status = "Sin inicializar";
    private readonly object _lock = new();

    public string Status { get { EnsureOpen(); return _status; } }
    public bool IsAvailable => EnsureOpen();

    private static MMDevice? FindEndpoint()
    {
        using var en = new MMDeviceEnumerator();
        foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            string name = d.FriendlyName;
            if ((name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) || name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase))
                && d.AudioClient.MixFormat.Channels >= 4)
                return d;
        }
        return null;
    }

    private bool EnsureOpen()
    {
        lock (_lock)
        {
            if (_out != null) return true;
            try
            {
                var dev = FindEndpoint();
                if (dev == null)
                {
                    _status = "No se encontró el dispositivo de audio de 4 canales del DualSense. Conecta el mando por USB (por Bluetooth Windows no expone los actuadores hápticos).";
                    return false;
                }
                var mix = dev.AudioClient.MixFormat;
                _format = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
                _buffer = new BufferedWaveProvider(_format) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromMilliseconds(500) };
                _out = new WasapiOut(dev, AudioClientShareMode.Shared, true, 20);
                _out.Init(_buffer);
                _out.Play();
                _status = $"Audio háptico activo en «{dev.FriendlyName}» ({mix.Channels} canales, {mix.SampleRate} Hz)";
                return true;
            }
            catch (Exception ex)
            {
                _status = "No se pudo abrir el audio háptico: " + ex.Message;
                _out?.Dispose(); _out = null;
                return false;
            }
        }
    }

    public void Play(float[] samples, int sampleRate, bool left, bool right)
    {
        if (!EnsureOpen() || _format == null || _buffer == null) return;
        int ch = _format.Channels, outRate = _format.SampleRate;
        // Nearest-neighbour resampling is adequate: haptic content is below a few hundred Hz.
        int frames = (int)((long)samples.Length * outRate / sampleRate);
        var bytes = new byte[frames * ch * 4];
        for (int i = 0; i < frames; i++)
        {
            float v = samples[Math.Min(samples.Length - 1, (int)((long)i * sampleRate / outRate))];
            if (left) BitConverter.TryWriteBytes(bytes.AsSpan((i * ch + 2) * 4), v);
            if (right) BitConverter.TryWriteBytes(bytes.AsSpan((i * ch + 3) * 4), v);
        }
        _buffer.AddSamples(bytes, 0, bytes.Length);
    }

    public void Stop() { lock (_lock) _buffer?.ClearBuffer(); }

    public void Dispose()
    {
        lock (_lock) { try { _out?.Stop(); _out?.Dispose(); } catch { } _out = null; }
    }
}
