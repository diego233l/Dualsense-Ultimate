using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.App.Controls;

/// <summary>Interactive drawing of the controller. Shows live input, the light bar and player LEDs, and
/// lets the user pick any button, trigger, stick or touchpad zone.</summary>
public sealed class ControllerView : Control
{
    private const double W = 640, H = 400;
    private sealed record Region(string Name, Rect Rect, bool Round, string Label, bool EdgeOnly = false);

    private static readonly Region[] Regions =
    [
        new("L2", new Rect(98, 8, 92, 36), false, "L2"), new("L1", new Rect(98, 50, 92, 22), false, "L1"),
        new("R2", new Rect(450, 8, 92, 36), false, "R2"), new("R1", new Rect(450, 50, 92, 22), false, "R1"),
        new("Touchpad", new Rect(232, 96, 176, 92), false, ""),
        new("Create", new Rect(204, 100, 16, 30), false, ""), new("Options", new Rect(420, 100, 16, 30), false, ""),
        new("DpadUp", new Rect(139, 116, 24, 28), false, "▲"), new("DpadDown", new Rect(139, 162, 24, 28), false, "▼"),
        new("DpadLeft", new Rect(113, 142, 28, 24), false, "◀"), new("DpadRight", new Rect(161, 142, 28, 24), false, "▶"),
        new("Triangle", new Rect(475, 110, 30, 30), true, "△"), new("Cross", new Rect(475, 168, 30, 30), true, "✕"),
        new("Square", new Rect(446, 139, 30, 30), true, "□"), new("Circle", new Rect(504, 139, 30, 30), true, "○"),
        new("L3", new Rect(201, 204, 68, 68), true, "L3"), new("R3", new Rect(371, 204, 68, 68), true, "R3"),
        new("PS", new Rect(306, 222, 28, 28), true, "PS"), new("Mute", new Rect(306, 258, 28, 12), false, ""),
        new("FnLeft", new Rect(222, 282, 26, 14), false, "Fn", true), new("FnRight", new Rect(392, 282, 26, 14), false, "Fn", true),
        new("PaddleLeft", new Rect(128, 318, 56, 18), false, "LB", true), new("PaddleRight", new Rect(456, 318, 56, 18), false, "RB", true),
    ];

    public InputState? State { get; set; }
    public string? Selected { get; set; }
    public Color LightColor { get; set; } = Color.FromRgb(61, 123, 255);
    public byte PlayerLeds { get; set; }
    public bool IsEdge { get; set; }
    public TouchpadMode TouchMode { get; set; } = TouchpadMode.Passthrough;
    /// <summary>Sources that have a custom binding (marked with a dot).</summary>
    public HashSet<string> Bound { get; set; } = [];
    public bool Interactive { get; set; } = true;
    public event Action<string>? SourceClicked;

    public ControllerView() { ClipToBounds = true; MinHeight = 160; }

    private (double scale, double ox, double oy) Layout()
    {
        double s = Math.Min(Bounds.Width / W, Bounds.Height / H);
        return (s, (Bounds.Width - W * s) / 2, (Bounds.Height - H * s) / 2);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!Interactive) return;
        var (s, ox, oy) = Layout();
        var p = e.GetPosition(this);
        var pt = new Point((p.X - ox) / s, (p.Y - oy) / s);
        foreach (var r in Regions.Reverse())
        {
            if (r.EdgeOnly && !IsEdge) continue;
            if (!r.Rect.Contains(pt)) continue;
            string name = r.Name;
            if (name == "Touchpad")
            {
                bool right = pt.X > r.Rect.Center.X, bottom = pt.Y > r.Rect.Center.Y;
                name = TouchMode switch
                {
                    TouchpadMode.TwoRegions => right ? Sources.TouchRight : Sources.TouchLeft,
                    TouchpadMode.FourRegions => bottom ? (right ? Sources.TouchBottomRight : Sources.TouchBottomLeft) : (right ? Sources.TouchTopRight : Sources.TouchTopLeft),
                    _ => "Touchpad",
                };
            }
            Selected = name;
            SourceClicked?.Invoke(name);
            InvalidateVisual();
            return;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var (s, ox, oy) = Layout();
        using var _ = ctx.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy));
        IBrush body = Ui.Brush(this, "PadBody"), dark = Ui.Brush(this, "PadDark"), accent = Ui.Brush(this, "Accent");
        var st = State ?? InputState.Neutral;
        var light = new SolidColorBrush(LightColor);
        var glow = new SolidColorBrush(LightColor, 0.25);
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        var outline = new Pen(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), 1);

        // body: grips + centre
        ctx.DrawEllipse(body, outline, new Rect(62, 150, 150, 240));
        ctx.DrawEllipse(body, outline, new Rect(428, 150, 150, 240));
        ctx.DrawRectangle(body, null, new RoundedRect(new Rect(88, 78, 464, 216), 70));
        ctx.DrawRectangle(dark, null, new RoundedRect(new Rect(196, 88, 248, 196), 26));
        // light bar glow + strips
        ctx.DrawRectangle(glow, null, new RoundedRect(new Rect(216, 88, 208, 108), 14));
        ctx.DrawRectangle(light, null, new RoundedRect(new Rect(223, 96, 5, 92), 2));
        ctx.DrawRectangle(light, null, new RoundedRect(new Rect(412, 96, 5, 92), 2));
        // player LEDs
        for (int i = 0; i < 5; i++)
            ctx.DrawEllipse((PlayerLeds & (1 << i)) != 0 ? Brushes.White : new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), null, new Point(296 + i * 12, 198), 2.6, 2.6);

        foreach (var r in Regions)
        {
            if (r.EdgeOnly && !IsEdge) continue;
            bool isStick = r.Name is "L3" or "R3";
            bool pressed = Enum.TryParse<PadButtons>(r.Name, out var pb) && st.IsDown(pb);
            bool selected = Selected == r.Name || (r.Name == "Touchpad" && Selected != null && Selected.StartsWith("Touch"));
            bool onBody = r.Name is "L1" or "R1" or "L2" or "R2" or "PaddleLeft" or "PaddleRight";
            IBrush fill = pressed ? accent : onBody ? Ui.Brush(this, "CardBg2") : r.Name == "Touchpad" ? new SolidColorBrush(Color.FromRgb(42, 46, 60)) : isStick ? new SolidColorBrush(Color.FromRgb(38, 41, 52)) : new SolidColorBrush(Color.FromRgb(58, 63, 80));
            var pen = selected ? new Pen(accent, 2.5) : new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
            var rect = r.Rect;
            if (isStick)
            {
                ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(24, 26, 34)), pen, rect);
                double dx = ((r.Name == "L3" ? st.LeftX : st.RightX) - 128) / 127.0 * 12, dy = ((r.Name == "L3" ? st.LeftY : st.RightY) - 128) / 127.0 * 12;
                ctx.DrawEllipse(pressed ? accent : new SolidColorBrush(Color.FromRgb(70, 76, 96)), null, rect.Center + new Vector(dx, dy), 22, 22);
            }
            else if (r.Round) ctx.DrawEllipse(fill, pen, rect);
            else ctx.DrawRectangle(fill, pen, new RoundedRect(rect, r.Name is "L2" or "R2" ? 10 : 5));

            if (r.Name is "L2" or "R2")
            {
                double v = (r.Name == "L2" ? st.L2 : st.R2) / 255.0;
                if (v > 0) ctx.DrawRectangle(accent, null, new RoundedRect(new Rect(rect.X, rect.Bottom - rect.Height * v, rect.Width, rect.Height * v), 10));
            }
            if (r.Name == "Touchpad")
            {
                var guide = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1, new DashStyle([3, 3], 0));
                if (TouchMode is TouchpadMode.TwoRegions or TouchpadMode.FourRegions) ctx.DrawLine(guide, new Point(rect.Center.X, rect.Y + 4), new Point(rect.Center.X, rect.Bottom - 4));
                if (TouchMode == TouchpadMode.FourRegions) ctx.DrawLine(guide, new Point(rect.X + 4, rect.Center.Y), new Point(rect.Right - 4, rect.Center.Y));
                if (Selected != null && Selected.StartsWith("Touch") && Selected != "Touchpad")
                {
                    bool right = Selected.EndsWith("Right"), four = TouchMode == TouchpadMode.FourRegions, bottom = Selected.Contains("Bottom");
                    var zone = new Rect(right ? rect.Center.X : rect.X, four && bottom ? rect.Center.Y : rect.Y, rect.Width / 2, four ? rect.Height / 2 : rect.Height);
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 61, 123, 255)), null, zone);
                }
                foreach (var t in new[] { st.Touch0, st.Touch1 })
                    if (t.Active) ctx.DrawEllipse(accent, null, new Point(rect.X + t.X / 1920.0 * rect.Width, rect.Y + t.Y / 1080.0 * rect.Height), 6, 6);
            }
            if (r.Label.Length > 0 && !isStick)
            {
                var ft = new FormattedText(r.Label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, r.Round ? 13 : 11,
                    pressed ? Brushes.White : onBody ? Ui.Brush(this, "TextMain") : new SolidColorBrush(Color.FromRgb(200, 205, 220)));
                ctx.DrawText(ft, new Point(rect.Center.X - ft.Width / 2, rect.Center.Y - ft.Height / 2));
            }
            if (Bound.Contains(r.Name)) ctx.DrawEllipse(Ui.Brush(this, "Warn"), null, new Point(rect.Right - 2, rect.Y + 2), 4, 4);
        }
    }
}
