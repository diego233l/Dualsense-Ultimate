using Avalonia.Controls;
using Avalonia.Media;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Lighting;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.App.Pages;

public sealed class LightingPage : PageBase
{
    public override string Title => "Iluminación RGB";
    public override string Subtitle => "Barra de luz, LEDs de jugador y LED del micrófono";
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private Control Color(string label, string hex, Action<string> set)
    {
        var picker = new ColorPicker { Color = Ui.ToColor(Rgb.Parse(hex)), IsAlphaEnabled = false, IsAlphaVisible = false, Width = 90, Height = 32 };
        picker.ColorChanged += (_, e) => set(Ui.ToRgb(e.NewColor).ToHex());
        return Labeled(label, picker);
    }

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var p = AppState.EditProfile;
        var l = p.Lighting;
        var root = new StackPanel();
        if (AppState.Session == null) root.Children.Add(NoController());

        // ---- live preview
        var preview = new Border { Height = 46, CornerRadius = new Avalonia.CornerRadius(12) };
        var leds = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 10, 0, 0) };
        var dots = Enumerable.Range(0, 5).Select(_ => new Border { Width = 12, Height = 12, CornerRadius = new Avalonia.CornerRadius(6) }).ToList();
        foreach (var d in dots) leds.Children.Add(d);
        Live(() =>
        {
            var s = AppState.Session;
            var ctx = s == null ? LightingContext.Default : new LightingContext(s.Device.State.BatteryPercent, s.Device.State.BatteryStatus == BatteryStatus.Charging, s.AudioLevel, double.MaxValue);
            Rgb c = s != null ? new Rgb(s.LastOutput.Red, s.LastOutput.Green, s.LastOutput.Blue) : LightingEngine.Compute(l, _clock.Elapsed.TotalSeconds, ctx);
            preview.Background = new SolidColorBrush(Ui.ToColor(c));
            byte bits = s?.LastOutput.PlayerLeds ?? LightingEngine.PlayerLedBits(l, ctx);
            for (int i = 0; i < 5; i++) dots[i].Background = (bits & (1 << i)) != 0 ? Brushes.White : Ui.Brush(this, "CardBorder");
        });

        // ---- light bar
        var bar = new StackPanel();
        bar.Children.Add(Wrap(
            EnumCombo("Efecto", l.Mode, v => { l.Mode = v; Rebuild(); }, DashboardPage.ModeName),
            new Border { Width = 16 },
            SliderRow("Brillo", 0, 100, l.Brightness, v => l.Brightness = (int)v, "0' %'")));
        var colors = new WrapPanel();
        if (l.Mode is LightingMode.Static or LightingMode.Breathing or LightingMode.Pulse or LightingMode.AudioReactive)
            colors.Children.Add(Color(l.Mode == LightingMode.AudioReactive ? "Color con sonido fuerte" : "Color", l.Color, v => l.Color = v));
        if (l.Mode is LightingMode.Pulse or LightingMode.AudioReactive)
        { colors.Children.Add(new Border { Width = 16 }); colors.Children.Add(Color(l.Mode == LightingMode.AudioReactive ? "Color en silencio" : "Color secundario", l.Color2, v => l.Color2 = v)); }
        if (l.Mode is LightingMode.Breathing or LightingMode.Rainbow or LightingMode.ColorCycle or LightingMode.Pulse or LightingMode.Custom)
        { colors.Children.Add(new Border { Width = 16 }); colors.Children.Add(SliderRow("Duración del ciclo", 0.5, 30, l.PeriodSeconds, v => l.PeriodSeconds = v, "0.0' s'", 0.5)); }
        bar.Children.Add(colors);

        if (l.Mode == LightingMode.ColorCycle)
        {
            var pal = new WrapPanel();
            for (int i = 0; i < l.Palette.Count; i++)
            {
                int idx = i;
                pal.Children.Add(Stack(Color($"Color {i + 1}", l.Palette[i], v => l.Palette[idx] = v), Ui.Btn("Quitar", () => { if (l.Palette.Count > 2) { l.Palette.RemoveAt(idx); Rebuild(); } })));
                pal.Children.Add(new Border { Width = 10 });
            }
            if (l.Palette.Count < 12) pal.Children.Add(new Border { Margin = new Avalonia.Thickness(0, 26, 0, 0), Child = Ui.Btn("+ Color", () => { l.Palette.Add("#FFFFFF"); Rebuild(); }) });
            bar.Children.Add(pal);
        }
        if (l.Mode == LightingMode.Custom)
        {
            bar.Children.Add(Ui.Text("Editor RGB: define colores clave a lo largo del ciclo; la barra interpola entre ellos.", "muted"));
            var frames = new StackPanel { Spacing = 2 };
            foreach (var k in l.Keyframes.OrderBy(k => k.Time).ToList())
            {
                var kf = k;
                frames.Children.Add(Ui.Row(12, SliderRow("Posición en el ciclo", 0, 100, kf.Time * 100, v => kf.Time = v / 100, "0' %'"), Color("Color", kf.Color, v => kf.Color = v),
                    new Border { Margin = new Avalonia.Thickness(0, 26, 0, 0), Child = Ui.Btn("Quitar", () => { l.Keyframes.Remove(kf); Rebuild(); }) }));
            }
            bar.Children.Add(frames);
            if (l.Keyframes.Count < 16)
                bar.Children.Add(Ui.Btn("+ Añadir color clave", () => { l.Keyframes.Add(new LightingKeyframe { Time = l.Keyframes.Count == 0 ? 0 : Math.Min(1, l.Keyframes.Max(k => k.Time) + 0.2), Color = "#FF00AA" }); Rebuild(); }));
        }
        if (l.Mode == LightingMode.AudioReactive)
            bar.Children.Add(Ui.Text(rt.Platform.AudioSource is { IsAvailable: true }
                ? "Usa el audio que suena en el sistema (captura WASAPI loopback)."
                : "La captura de audio del sistema solo está disponible en Windows.", "muted"));
        if (l.Mode == LightingMode.Battery)
            bar.Children.Add(Ui.Text("Verde con batería alta, amarillo a media carga y rojo parpadeante por debajo del 15 %. Late suavemente mientras carga.", "muted"));

        root.Children.Add(Card("Barra de luz", preview, leds, new Border { Height = 6 }, bar));

        // ---- player LEDs, mic LED, notifications
        var mask = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        for (int i = 0; i < 5; i++)
        {
            int bit = 1 << i;
            var tb = new Avalonia.Controls.Primitives.ToggleButton { Content = (i + 1).ToString(), IsChecked = (l.PlayerLedMask & bit) != 0, Width = 38 };
            tb.IsCheckedChanged += (_, _) => l.PlayerLedMask = tb.IsChecked == true ? l.PlayerLedMask | bit : l.PlayerLedMask & ~bit;
            mask.Children.Add(tb);
        }
        var ledCard = Card("LEDs de jugador",
            EnumCombo("Modo", l.PlayerLeds, v => { l.PlayerLeds = v; Rebuild(); }, m => m switch
            {
                PlayerLedMode.Off => "Apagados", PlayerLedMode.Player1 => "Jugador 1", PlayerLedMode.Player2 => "Jugador 2", PlayerLedMode.Player3 => "Jugador 3",
                PlayerLedMode.Player4 => "Jugador 4", PlayerLedMode.All => "Todos", PlayerLedMode.Battery => "Nivel de batería", _ => "Personalizado",
            }),
            EnumCombo("Brillo", l.PlayerLedBrightness, v => l.PlayerLedBrightness = v, b => b switch { LedBrightness.High => "Alto", LedBrightness.Medium => "Medio", _ => "Bajo" }));
        if (l.PlayerLeds == PlayerLedMode.Custom) ((StackPanel)ledCard.Child!).Children.Add(Labeled("LEDs encendidos", mask));
        ledCard.Width = 300;

        var micCard = Card("LED del micrófono",
            EnumCombo("Estado", l.MicLed, v => l.MicLed = v, m => m switch { MicLedMode.Off => "Apagado", MicLedMode.On => "Encendido", _ => "Parpadeo lento" }),
            Ui.Text("Solo DualSense y DualSense Edge.", "muted"));
        micCard.Width = 300;

        var notifCard = Card("Notificaciones",
            Toggle("Parpadear al recibir una notificación", l.NotificationFlash, v => l.NotificationFlash = v),
            Color("Color del aviso", l.NotificationColor, v => l.NotificationColor = v),
            new Border { Height = 6 }, Ui.Btn("Probar", () => rt.Notify("Prueba de notificación")),
            Ui.Text("Las notificaciones llegan desde la API local, plugins o automatizaciones.", "muted"));
        notifCard.Width = 300;
        root.Children.Add(Wrap(ledCard, micCard, notifCard));

        // ---- presets
        var status = Ui.Text("", "muted");
        var presets = new WrapPanel();
        (string, LightingConfig)[] builtIn =
        [
            ("Azul PlayStation", new LightingConfig { Mode = LightingMode.Static, Color = "#0A4BFF" }),
            ("Arcoíris", new LightingConfig { Mode = LightingMode.Rainbow, PeriodSeconds = 6 }),
            ("Respiración cálida", new LightingConfig { Mode = LightingMode.Breathing, Color = "#FF7A00", PeriodSeconds = 5 }),
            ("Batería", new LightingConfig { Mode = LightingMode.Battery, PlayerLeds = PlayerLedMode.Battery }),
            ("Pulso neón", new LightingConfig { Mode = LightingMode.Pulse, Color = "#FF00C8", Color2 = "#1900FF", PeriodSeconds = 1.5 }),
        ];
        void Apply(LightingConfig c) { p.Lighting = c.Clone(); Rebuild(); }
        foreach (var (name, cfg) in builtIn) { presets.Children.Add(Ui.Btn(name, () => Apply(cfg))); presets.Children.Add(new Border { Width = 6 }); }
        foreach (var named in rt.Profiles.Library.LightingPresets.ToList())
        {
            var n = named;
            presets.Children.Add(Ui.Row(0, Ui.Btn("★ " + n.Name, () => Apply(n.Config)), Ui.Btn("✕", () => { rt.Profiles.Library.LightingPresets.Remove(n); rt.Profiles.SaveLibrary(); Rebuild(); })));
            presets.Children.Add(new Border { Width = 6 });
        }
        var presetName = new TextBox { Watermark = "Nombre del preset", Width = 200 };
        root.Children.Add(Card("Presets", presets, new Border { Height = 10 },
            Ui.Row(8, presetName,
                Ui.Btn("Guardar actual", () =>
                {
                    string name = string.IsNullOrWhiteSpace(presetName.Text) ? "Mi preset" : presetName.Text!.Trim();
                    rt.Profiles.Library.LightingPresets.Add(new NamedLighting { Name = name, Config = l.Clone() }); rt.Profiles.SaveLibrary(); Rebuild();
                }),
                Ui.Btn("Exportar…", async () =>
                {
                    string name = string.IsNullOrWhiteSpace(presetName.Text) ? "Iluminación " + p.Name : presetName.Text!.Trim();
                    if (await FileIo.SaveTextAsync(this, name, JsonFile.Serialize(SharePackage.Wrap(PackageKinds.Lighting, name, l)))) status.Text = "Preset exportado.";
                }),
                Ui.Btn("Importar…", async () =>
                {
                    var json = await FileIo.OpenTextAsync(this);
                    if (json == null) return;
                    var (r, summary) = rt.Profiles.Import(json);
                    status.Text = r.Ok ? summary! : string.Join(" ", r.Errors);
                    if (r.Ok) Rebuild();
                })),
            status));
        return root;
    }
}
