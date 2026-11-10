using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Core.Mapping;

[Flags] public enum MouseButtons { None = 0, Left = 1, Right = 2, Middle = 4, X1 = 8, X2 = 16 }

/// <summary>Result of mapping one input report: the virtual pad state plus keyboard/mouse output.</summary>
public sealed class OutputFrame
{
    public PadButtons Buttons;
    public double LeftX, LeftY, RightX, RightY; // -1..1, Y up
    public double L2, R2;                        // 0..1
    public HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase);
    public MouseButtons Mouse;
    public double MouseDx, MouseDy;
    public int Wheel;
    /// <summary>Special actions triggered during this frame (edge events).</summary>
    public List<string> Specials = [];
    public TouchPoint Touch0, Touch1;           // forwarded to a virtual DualShock 4
    public short GyroX, GyroY, GyroZ, AccelX, AccelY, AccelZ;
}
