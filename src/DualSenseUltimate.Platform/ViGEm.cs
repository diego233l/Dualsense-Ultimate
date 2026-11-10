using System.Runtime.Versioning;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Virtual;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace DualSenseUltimate.Platform;

/// <summary>Virtual pads through the ViGEmBus driver (BSD-3) using Nefarius.ViGEm.Client (MIT).
/// ViGEmBus is end-of-life but remains the only signed, widely deployed virtual gamepad bus for
/// Windows; it is the same driver DS4Windows, Sunshine and Steam Link-style tools depend on.</summary>
[SupportedOSPlatform("windows")]
public sealed class ViGEmPadFactory : IVirtualPadFactory, IDisposable
{
    private ViGEmClient? _client;
    public string Status { get; private set; } = "";
    public bool IsAvailable => EnsureClient();

    private bool EnsureClient()
    {
        if (_client != null) return true;
        try { _client = new ViGEmClient(); Status = "ViGEmBus detectado"; return true; }
        catch (Exception ex)
        {
            Status = "ViGEmBus no está instalado o no responde (" + ex.GetType().Name + "). Instálalo desde el instalador de DualSense Ultimate o desde github.com/nefarius/ViGEmBus/releases.";
            return false;
        }
    }

    public IVirtualPad Create(EmulationMode mode)
    {
        if (!EnsureClient()) throw new InvalidOperationException(Status);
        return mode == EmulationMode.DualShock4 ? new Ds4Pad(_client!) : new XboxPad(_client!);
    }

    public void Dispose() { _client?.Dispose(); _client = null; }

    private static short Axis(double v) => (short)Math.Clamp(Math.Round(v * 32767), -32768, 32767);
    private static byte Axis8(double v) => (byte)Math.Clamp(Math.Round(128 + v * 127), 0, 255);

    private sealed class XboxPad : IVirtualPad
    {
        private readonly IXbox360Controller _c;
        public event Action<byte, byte>? Rumble;

        public XboxPad(ViGEmClient client)
        {
            _c = client.CreateXbox360Controller();
            _c.AutoSubmitReport = false;
            _c.FeedbackReceived += (_, e) => Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
            _c.Connect();
        }

        public void Submit(OutputFrame f)
        {
            var b = f.Buttons;
            _c.SetButtonState(Xbox360Button.A, b.HasFlag(PadButtons.Cross));
            _c.SetButtonState(Xbox360Button.B, b.HasFlag(PadButtons.Circle));
            _c.SetButtonState(Xbox360Button.X, b.HasFlag(PadButtons.Square));
            _c.SetButtonState(Xbox360Button.Y, b.HasFlag(PadButtons.Triangle));
            _c.SetButtonState(Xbox360Button.LeftShoulder, b.HasFlag(PadButtons.L1));
            _c.SetButtonState(Xbox360Button.RightShoulder, b.HasFlag(PadButtons.R1));
            _c.SetButtonState(Xbox360Button.Back, b.HasFlag(PadButtons.Create) || b.HasFlag(PadButtons.Touchpad));
            _c.SetButtonState(Xbox360Button.Start, b.HasFlag(PadButtons.Options));
            _c.SetButtonState(Xbox360Button.LeftThumb, b.HasFlag(PadButtons.L3));
            _c.SetButtonState(Xbox360Button.RightThumb, b.HasFlag(PadButtons.R3));
            _c.SetButtonState(Xbox360Button.Guide, b.HasFlag(PadButtons.PS));
            _c.SetButtonState(Xbox360Button.Up, b.HasFlag(PadButtons.DpadUp));
            _c.SetButtonState(Xbox360Button.Down, b.HasFlag(PadButtons.DpadDown));
            _c.SetButtonState(Xbox360Button.Left, b.HasFlag(PadButtons.DpadLeft));
            _c.SetButtonState(Xbox360Button.Right, b.HasFlag(PadButtons.DpadRight));
            _c.SetAxisValue(Xbox360Axis.LeftThumbX, Axis(f.LeftX)); _c.SetAxisValue(Xbox360Axis.LeftThumbY, Axis(f.LeftY));
            _c.SetAxisValue(Xbox360Axis.RightThumbX, Axis(f.RightX)); _c.SetAxisValue(Xbox360Axis.RightThumbY, Axis(f.RightY));
            _c.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)Math.Round(f.L2 * 255));
            _c.SetSliderValue(Xbox360Slider.RightTrigger, (byte)Math.Round(f.R2 * 255));
            _c.SubmitReport();
        }

        public void Dispose() { try { _c.Disconnect(); } catch (Exception ex) { Log.Debug("x360 disconnect: " + ex.Message); } }
    }

    private sealed class Ds4Pad : IVirtualPad
    {
        private readonly IDualShock4Controller _c;
        private readonly byte[] _raw = new byte[63];
        private byte _touchCounter; private bool _prevTouch;
        private ushort _timestamp;
        public event Action<byte, byte>? Rumble;

        public Ds4Pad(ViGEmClient client)
        {
            _c = client.CreateDualShock4Controller();
            _c.AutoSubmitReport = false;
            _c.FeedbackReceived += (_, e) => Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
            _c.Connect();
        }

        /// <summary>Uses the extended (raw) report so touchpad and motion data reach the game too. The
        /// layout is the DualShock 4 USB input report without the report id byte.</summary>
        public void Submit(OutputFrame f)
        {
            var b = f.Buttons; var r = _raw;
            Array.Clear(r);
            r[0] = Axis8(f.LeftX); r[1] = Axis8(-f.LeftY); r[2] = Axis8(f.RightX); r[3] = Axis8(-f.RightY);
            byte b0 = DualSenseProtocol.ButtonsToHat(b);
            if (b.HasFlag(PadButtons.Square)) b0 |= 0x10; if (b.HasFlag(PadButtons.Cross)) b0 |= 0x20;
            if (b.HasFlag(PadButtons.Circle)) b0 |= 0x40; if (b.HasFlag(PadButtons.Triangle)) b0 |= 0x80;
            byte b1 = 0;
            if (b.HasFlag(PadButtons.L1)) b1 |= 0x01; if (b.HasFlag(PadButtons.R1)) b1 |= 0x02;
            if (f.L2 > 0.02 || b.HasFlag(PadButtons.L2)) b1 |= 0x04; if (f.R2 > 0.02 || b.HasFlag(PadButtons.R2)) b1 |= 0x08;
            if (b.HasFlag(PadButtons.Create)) b1 |= 0x10; if (b.HasFlag(PadButtons.Options)) b1 |= 0x20;
            if (b.HasFlag(PadButtons.L3)) b1 |= 0x40; if (b.HasFlag(PadButtons.R3)) b1 |= 0x80;
            byte b2 = 0;
            if (b.HasFlag(PadButtons.PS)) b2 |= 0x01; if (b.HasFlag(PadButtons.Touchpad)) b2 |= 0x02;
            r[4] = b0; r[5] = b1; r[6] = b2;
            r[7] = (byte)Math.Round(f.L2 * 255); r[8] = (byte)Math.Round(f.R2 * 255);
            _timestamp += 188; // 5.33 us units, ~1 ms per report
            r[9] = (byte)_timestamp; r[10] = (byte)(_timestamp >> 8);
            void W(int o, short v) { r[o] = (byte)v; r[o + 1] = (byte)(v >> 8); }
            W(12, f.GyroX); W(14, f.GyroY); W(16, f.GyroZ); W(18, f.AccelX); W(20, f.AccelY); W(22, f.AccelZ);
            r[29] = 0x1B; // cable connected, battery full
            bool touching = f.Touch0.Active || f.Touch1.Active;
            if (touching && !_prevTouch) _touchCounter++;
            _prevTouch = touching;
            r[32] = 1; r[33] = _touchCounter;
            WriteTouch(r.AsSpan(34, 4), f.Touch0, DualShock4Protocol.TouchpadHeight);
            WriteTouch(r.AsSpan(38, 4), f.Touch1, DualShock4Protocol.TouchpadHeight);
            _c.SubmitRawReport(r);
        }

        private static void WriteTouch(Span<byte> t, TouchPoint tp, int maxY)
        {
            // DualSense pad is 1920x1080, DualShock 4 is 1920x942: rescale Y.
            int y = Math.Clamp(tp.Y * maxY / DualSenseProtocol.TouchpadHeight, 0, maxY - 1);
            t[0] = (byte)((tp.Id & 0x7F) | (tp.Active ? 0 : 0x80));
            t[1] = (byte)(tp.X & 0xFF);
            t[2] = (byte)(((tp.X >> 8) & 0x0F) | ((y & 0x0F) << 4));
            t[3] = (byte)(y >> 4);
        }

        public void Dispose() { try { _c.Disconnect(); } catch (Exception ex) { Log.Debug("ds4 disconnect: " + ex.Message); } }
    }
}
