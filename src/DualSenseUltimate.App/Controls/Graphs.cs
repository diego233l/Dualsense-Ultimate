using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.App.Controls;

/// <summary>Live stick plot: raw position, processed position, dead zone and saturation rings, plus the response curve.</summary>
public sealed class StickGraph : Control
{
    public StickConfig? Config { get; set; }
    public byte RawX { get; set; } = 128;
    public byte RawY { get; set; } = 128;
    private readonly Queue<Point> _trail = new();

    public StickGraph() { MinHeight = 180; MinWidth = 180; }

    public override void Render(DrawingContext ctx)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height) - 8, r = size / 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var cfg = Config ?? new StickConfig();
        IBrush accent = Ui.Brush(this, "Accent"), muted = Ui.Brush(this, "TextMuted");
        var grid = new Pen(Ui.Brush(this, "CardBorder"), 1);
        ctx.DrawEllipse(Ui.Brush(this, "CardBg2"), grid, c, r, r);
        ctx.DrawLine(grid, c + new Vector(-r, 0), c + new Vector(r, 0));
        ctx.DrawLine(grid, c + new Vector(0, -r), c + new Vector(0, r));
        double max = Math.Clamp(cfg.MaxZone, 0.05, 1);
        if (max < 1) ctx.DrawEllipse(null, new Pen(Ui.Brush(this, "Good"), 1.2, new DashStyle([4, 3], 0)), c, r * max, r * max);
        var centre = c + new Vector(cfg.CenterX * r, cfg.CenterY * r);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(50, 255, 69, 58)), new Pen(Ui.Brush(this, "Bad"), 1), centre, r * cfg.DeadZone, r * cfg.DeadZone);

        // response curve along the horizontal radius
        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            for (int i = 0; i <= 40; i++)
            {
                double n = i / 40.0, v = Math.Clamp(StickProcessor.Curve(cfg, n) * cfg.Sensitivity, 0, 1);
                var p = new Point(c.X + n * r * 0.707, c.Y - v * r * 0.707); // ends on the circle at 45 degrees
                if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
            }
        }
        ctx.DrawGeometry(null, new Pen(Ui.Brush(this, "Accent2"), 1.5), curve);

        var raw = c + new Vector(StickProcessor.Normalize(RawX) * r, StickProcessor.Normalize(RawY) * r);
        var (px, py) = StickProcessor.Apply(cfg, RawX, RawY);
        var processed = c + new Vector(px * r, -py * r);
        _trail.Enqueue(processed); while (_trail.Count > 40) _trail.Dequeue();
        int k = 0;
        foreach (var t in _trail) ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(k++ * 3), 61, 123, 255)), null, t, 2, 2);
        ctx.DrawEllipse(muted, null, raw, 4, 4);
        ctx.DrawEllipse(accent, new Pen(Brushes.White, 1.5), processed, 7, 7);
    }
}

/// <summary>Adaptive trigger visualisation: resistance per zone and the live trigger position.</summary>
public sealed class TriggerGraph : Control
{
    public TriggerEffect? TriggerEffect { get; set; }
    /// <summary>Live trigger travel 0-255.</summary>
    public byte Position { get; set; }

    public TriggerGraph() { MinHeight = 150; }

    public override void Render(DrawingContext ctx)
    {
        var e = TriggerEffect ?? Core.Protocol.TriggerEffect.OffEffect;
        var curve = TriggerEffectEncoder.ForceCurve(e);
        double w = Bounds.Width, h = Bounds.Height - 22, bw = w / 10;
        bool vibrating = e.Type is TriggerEffectType.Vibration or TriggerEffectType.MultiZoneVibration or TriggerEffectType.Machine or TriggerEffectType.Galloping;
        ctx.DrawRectangle(Ui.Brush(this, "CardBg2"), null, new RoundedRect(new Rect(0, 0, w, h), 8));
        var typeface = new Typeface(FontFamily.Default);
        for (int i = 0; i < 10; i++)
        {
            double v = curve[i];
            var rect = new Rect(i * bw + 3, h - v * (h - 8) - 2, bw - 6, v * (h - 8));
            if (v > 0)
            {
                var top = vibrating ? Color.FromRgb(138, 92, 255) : Color.FromRgb(61, 123, 255);
                var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
                brush.GradientStops.Add(new GradientStop(top, 0)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, top.R, top.G, top.B), 1));
                ctx.DrawRectangle(brush, null, new RoundedRect(rect, 4));
                if (vibrating) // zig-zag to hint at vibration
                    for (double y = rect.Y + 4; y < rect.Bottom - 2; y += 6) ctx.DrawLine(new Pen(Brushes.White, 1), new Point(rect.X + 3, y), new Point(rect.Right - 3, y + 2));
            }
            var ft = new FormattedText(i.ToString(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, Ui.Brush(this, "TextMuted"));
            ctx.DrawText(ft, new Point(i * bw + bw / 2 - ft.Width / 2, h + 5));
        }
        double x = Position / 255.0 * w;
        ctx.DrawLine(new Pen(Ui.Brush(this, "Warn"), 2), new Point(x, 0), new Point(x, h));
        if (e.Type == TriggerEffectType.Off)
        {
            var ft = new FormattedText("Sin resistencia", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 12, Ui.Brush(this, "TextMuted"));
            ctx.DrawText(ft, new Point(w / 2 - ft.Width / 2, h / 2 - ft.Height / 2));
        }
    }
}

/// <summary>Bar chart preview of a haptic pattern (left motor up, right motor down).</summary>
public sealed class PatternGraph : Control
{
    public Core.Haptics.HapticPattern? Pattern { get; set; }
    public PatternGraph() { MinHeight = 90; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, mid = h / 2;
        ctx.DrawRectangle(Ui.Brush(this, "CardBg2"), null, new RoundedRect(new Rect(0, 0, w, h), 8));
        ctx.DrawLine(new Pen(Ui.Brush(this, "CardBorder"), 1), new Point(0, mid), new Point(w, mid));
        var p = Pattern;
        if (p == null || p.Steps.Count == 0) return;
        double total = p.TotalMs, x = 0;
        foreach (var s in p.Steps)
        {
            double sw = Math.Max(1, s.DurationMs) / total * w;
            ctx.DrawRectangle(Ui.Brush(this, "Accent"), null, new Rect(x + 0.5, mid - s.Left / 255.0 * (mid - 4), Math.Max(1, sw - 1), s.Left / 255.0 * (mid - 4)));
            ctx.DrawRectangle(Ui.Brush(this, "Accent2"), null, new Rect(x + 0.5, mid, Math.Max(1, sw - 1), s.Right / 255.0 * (mid - 4)));
            x += sw;
        }
    }
}
