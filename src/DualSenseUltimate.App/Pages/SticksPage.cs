using Avalonia.Controls;
using Avalonia.Threading;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.App.Pages;

public sealed class SticksPage : PageBase
{
    public override string Title => "Joysticks y giroscopio";
    public override string Subtitle => "Zonas muertas, curvas, calibración y control por movimiento";
    private string _status = "";

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var p = AppState.EditProfile;
        var m = p.Mapping;
        var root = new StackPanel();
        if (AppState.Session == null) root.Children.Add(NoController());
        if (p.Emulation.Mode == Core.Virtual.EmulationMode.None)
            root.Children.Add(Notice("En modo híbrido el juego lee los joysticks directamente del mando, así que zonas muertas y curvas solo afectan a las salidas de esta aplicación (stick como ratón/teclas, giroscopio). " +
                                     "Para que también afecten al juego, elige un mando virtual en Remapeo → Emulación."));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var l = Stick("Joystick izquierdo", m.LeftStick, false);
        var r = Stick("Joystick derecho", m.RightStick, true);
        Grid.SetColumn(r, 1);
        grid.Children.Add(l); grid.Children.Add(r);
        root.Children.Add(grid);

        root.Children.Add(Card("Gatillos analógicos y opciones",
            Wrap(SliderRow("L2 · zona muerta", 0, 0.9, m.L2.DeadZone, v => m.L2.DeadZone = v, "0%", 0.01), new Border { Width = 16 },
                SliderRow("L2 · recorrido máximo", 0.1, 1, m.L2.MaxZone, v => m.L2.MaxZone = v, "0%", 0.01), new Border { Width = 16 },
                SliderRow("R2 · zona muerta", 0, 0.9, m.R2.DeadZone, v => m.R2.DeadZone = v, "0%", 0.01), new Border { Width = 16 },
                SliderRow("R2 · recorrido máximo", 0.1, 1, m.R2.MaxZone, v => m.R2.MaxZone = v, "0%", 0.01)),
            Toggle("Intercambiar joysticks", m.SwapSticks, v => m.SwapSticks = v)));

        // ---- gyro
        var g = m.Gyro;
        var gyroLive = Ui.Text("", "muted");
        Live(() =>
        {
            if (AppState.Session is not { } s) { gyroLive.Text = "Sin mando."; return; }
            var (x, y, z) = s.Device.Calibration.GyroDps(s.Device.State);
            var (ax, ay, az) = s.Device.Calibration.AccelG(s.Device.State);
            gyroLive.Text = $"Giroscopio (°/s): cabeceo {x - g.BiasX,7:0.0}  guiñada {y - g.BiasY,7:0.0}  alabeo {z - g.BiasZ,7:0.0}     Acelerómetro (g): {ax,5:0.00} {ay,5:0.00} {az,5:0.00}";
        });
        var buttons = Sources.Buttons.Select(b => b.ToString()).ToList();
        var act = new ComboBox { ItemsSource = new[] { "(siempre activo)" }.Concat(buttons.Select(RemapPage.SourceName)).ToList(), SelectedIndex = g.ActivationButton == null ? 0 : buttons.IndexOf(g.ActivationButton) + 1, Width = 220 };
        act.SelectionChanged += (_, _) => g.ActivationButton = act.SelectedIndex <= 0 ? null : buttons[act.SelectedIndex - 1];
        root.Children.Add(Card("Giroscopio",
            Wrap(EnumCombo("Usar el giroscopio como", g.Mode, v => g.Mode = v, x => x switch
                {
                    GyroMode.Mouse => "Ratón / cámara", GyroMode.LeftStick => "Joystick izquierdo", GyroMode.RightStick => "Joystick derecho (cámara)", _ => "Desactivado",
                }, 240),
                new Border { Width = 16 }, Labeled("Botón de activación", act), new Border { Width = 16 },
                new Border { Margin = new Avalonia.Thickness(0, 22, 0, 0), Child = Toggle("El botón desactiva en vez de activar", g.ActivationDisables, v => g.ActivationDisables = v) }),
            Wrap(SliderRow("Sensibilidad horizontal", 0.5, 40, g.SensitivityX, v => g.SensitivityX = v, "0.0", 0.5), new Border { Width = 16 },
                SliderRow("Sensibilidad vertical", 0.5, 40, g.SensitivityY, v => g.SensitivityY = v, "0.0", 0.5), new Border { Width = 16 },
                SliderRow("Zona muerta", 0, 15, g.DeadZone, v => g.DeadZone = v, "0.0' °/s'", 0.1), new Border { Width = 16 },
                SliderRow("Filtrado (suavizado)", 0, 0.95, g.Smoothing, v => g.Smoothing = v, "0%", 0.05)),
            Wrap(Toggle("Invertir horizontal", g.InvertX, v => g.InvertX = v), new Border { Width = 18 }, Toggle("Invertir vertical", g.InvertY, v => g.InvertY = v), new Border { Width = 18 },
                Toggle("Girar con alabeo (mando en horizontal)", g.UseRollForX, v => g.UseRollForX = v), new Border { Width = 18 },
                Toggle("Gesto de agitar", g.ShakeGesture, v => g.ShakeGesture = v)),
            g.ShakeGesture ? SliderRow("Umbral del gesto de agitar", 0.5, 5, g.ShakeThresholdG, v => g.ShakeThresholdG = v, "0.0' g'", 0.1) : new Border(),
            gyroLive, new Border { Height = 6 },
            Ui.Row(8,
                Ui.Btn("Calibrar (deja el mando quieto 2 s)", () =>
                {
                    if (AppState.Session is not { } s) return;
                    _status = "Calibrando giroscopio…"; 
                    Task.Run(async () =>
                    {
                        double sx = 0, sy = 0, sz = 0; int n = 0;
                        for (int i = 0; i < 100; i++) { var (x, y, z) = s.Device.Calibration.GyroDps(s.Device.State); sx += x; sy += y; sz += z; n++; await Task.Delay(20); }
                        g.BiasX = sx / n; g.BiasY = sy / n; g.BiasZ = sz / n;
                        Dispatcher.UIThread.Post(() => { _status = $"Giroscopio calibrado (desviación: {g.BiasX:0.00}, {g.BiasY:0.00}, {g.BiasZ:0.00} °/s)."; Rebuild(); });
                    });
                }),
                Ui.Btn("Exportar configuración…", async () => await FileIo.SaveTextAsync(this, "Giroscopio " + p.Name, JsonFile.Serialize(SharePackage.Wrap(PackageKinds.Gyro, "Giroscopio " + p.Name, g))))),
            Ui.Text(_status, "muted"),
            Ui.Text("Para juegos con soporte nativo de giroscopio, usa el modo híbrido (ven el DualSense real) o el DualShock 4 virtual, que reenvía los datos de movimiento.", "muted")));
        return root;
    }

    private Control Stick(string title, StickConfig c, bool right)
    {
        var graph = new StickGraph { Config = c, Height = 230 };
        var live = Ui.Text("", "muted");
        Live(() =>
        {
            if (AppState.Session is { } s)
            {
                var st = s.Device.State;
                graph.RawX = right ? st.RightX : st.LeftX; graph.RawY = right ? st.RightY : st.LeftY;
                var (x, y) = StickProcessor.Apply(c, graph.RawX, graph.RawY);
                live.Text = $"Entrada: {StickProcessor.Normalize(graph.RawX),5:0.00}, {-StickProcessor.Normalize(graph.RawY),5:0.00}    Salida: {x,5:0.00}, {y,5:0.00}";
            }
            graph.InvalidateVisual();
        });
        var keys = string.Join(",", c.Keys);
        var keysBox = new TextBox { Text = keys, Width = 160, Watermark = "W,S,A,D" };
        keysBox.LostFocus += (_, _) =>
        {
            var parts = (keysBox.Text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts.All(KeyNames.VirtualKeys.ContainsKey)) c.Keys = parts; else keysBox.Text = string.Join(",", c.Keys);
        };
        var extras = new StackPanel();
        if (c.Curve == ResponseCurve.Custom) extras.Children.Add(SliderRow("Exponente de la curva", 0.2, 5, c.CurveExponent, v => c.CurveExponent = v, "0.00", 0.05));
        if (c.Output == StickOutput.Mouse) extras.Children.Add(SliderRow("Velocidad del cursor", 100, 5000, c.MouseSpeed, v => c.MouseSpeed = v, "0' px/s'", 50));
        if (c.Output == StickOutput.Keys) extras.Children.Add(Labeled("Teclas (arriba, abajo, izquierda, derecha)", keysBox));

        return Card(title, graph, live,
            Ui.Text("Gris: posición real · Azul: posición procesada · Rojo: zona muerta · Verde: saturación · Violeta: curva de respuesta", "muted"),
            Wrap(SliderRow("Zona muerta", 0, 0.6, c.DeadZone, v => c.DeadZone = v, "0%", 0.01), new Border { Width = 12 },
                SliderRow("Anti-zona muerta", 0, 0.6, c.AntiDeadZone, v => c.AntiDeadZone = v, "0%", 0.01), new Border { Width = 12 },
                SliderRow("Saturación (recorrido máximo)", 0.3, 1, c.MaxZone, v => c.MaxZone = v, "0%", 0.01), new Border { Width = 12 },
                SliderRow("Sensibilidad", 0.2, 3, c.Sensitivity, v => c.Sensitivity = v, "0.00'×'", 0.05)),
            Wrap(EnumCombo("Curva de respuesta", c.Curve, v => { c.Curve = v; Rebuild(); }, x => x switch
                {
                    ResponseCurve.Quadratic => "Cuadrática (precisa)", ResponseCurve.Cubic => "Cúbica (muy precisa)", ResponseCurve.EaseOut => "Rápida", ResponseCurve.Custom => "Personalizada", _ => "Lineal",
                }, 190),
                new Border { Width = 12 },
                EnumCombo("Función", c.Output, v => { c.Output = v; Rebuild(); }, x => x switch
                {
                    StickOutput.Mouse => "Ratón", StickOutput.Keys => "Teclas", StickOutput.Disabled => "Desactivado", _ => "Joystick",
                }, 160)),
            extras,
            Wrap(Toggle("Invertir X", c.InvertX, v => c.InvertX = v), new Border { Width = 18 }, Toggle("Invertir Y", c.InvertY, v => c.InvertY = v)),
            new Border { Height = 6 },
            Ui.Row(8,
                Ui.Btn("Calibrar centro / compensar drift", () =>
                {
                    if (AppState.Session is not { } s) return;
                    var cal = new StickCalibrator();
                    s.CalibrateRightStick = right; s.ActiveCalibrator = cal;
                    _status = "Suelta el joystick y no lo toques…";
                    Task.Run(async () =>
                    {
                        await Task.Delay(1500);
                        s.ActiveCalibrator = null;
                        if (cal.SampleCount < 20) { Dispatcher.UIThread.Post(() => { _status = "No se recibieron datos suficientes."; Rebuild(); }); return; }
                        var (cx, cy, dz) = cal.Compute();
                        c.CenterX = cx; c.CenterY = cy; c.DeadZone = Math.Max(dz, 0.04);
                        Dispatcher.UIThread.Post(() => { _status = $"Centro ajustado ({cx:0.000}, {cy:0.000}); zona muerta recomendada {c.DeadZone:0%}."; Rebuild(); });
                    });
                }),
                Ui.Btn("Restablecer centro", () => { c.CenterX = c.CenterY = 0; Rebuild(); })));
    }
}
