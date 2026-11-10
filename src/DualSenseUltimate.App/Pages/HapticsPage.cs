using Avalonia.Controls;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Haptics;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.App.Pages;

public sealed class HapticsPage : PageBase
{
    public override string Title => "Háptica";
    public override string Subtitle => "Vibración, patrones y audio háptico";
    private HapticPattern? _editing;
    private string _status = "";

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var p = AppState.EditProfile;
        var h = p.Haptics;
        var root = new StackPanel();
        if (AppState.Session == null) root.Children.Add(NoController());
        if (p.Emulation.NativeRumble) root.Children.Add(Notice("Modo nativo activado: la aplicación no escribe la vibración y el juego mantiene su háptica original."));

        // ---- classic rumble
        double left = 0, right = 0;
        void Send() => AppState.Session?.Rumble((byte)left, (byte)right, 1500);
        root.Children.Add(Wrap(
            WithWidth(Card("Vibración",
                SliderRow("Intensidad global", 0, 100, h.Intensity, v => h.Intensity = (int)v, "0' %'"),
                SliderRow("Probar motor izquierdo (grave)", 0, 255, 0, v => { left = v; Send(); }),
                SliderRow("Probar motor derecho (agudo)", 0, 255, 0, v => { right = v; Send(); }),
                new Border { Height = 6 },
                Ui.Row(8, Ui.Btn("Detener", () => AppState.Session?.StopEffects())),
                Ui.Text("En el DualSense ambos «motores» son actuadores de bobina que emulan la vibración clásica. Los canales se controlan por separado.", "muted"),
                Toggle("Dejar la vibración al juego (modo nativo)", p.Emulation.NativeRumble, v => { p.Emulation.NativeRumble = v; Rebuild(); })), 400),
            WithWidth(Card("Reactivo al sonido",
                Toggle("Convertir el audio del sistema en vibración", h.AudioReactive, v => h.AudioReactive = v),
                SliderRow("Ganancia", 0, 400, h.AudioReactiveGain, v => h.AudioReactiveGain = (int)v, "0' %'", 5),
                SliderRow("Filtro paso bajo", 40, 1000, h.AudioCutoffHz, v => h.AudioCutoffHz = (int)v, "0' Hz'", 10),
                SliderRow("Ecualizador · graves", -24, 24, h.BassGainDb, v => h.BassGainDb = (int)v, "0' dB'"),
                SliderRow("Ecualizador · medios", -24, 24, h.MidGainDb, v => h.MidGainDb = (int)v, "0' dB'"),
                SliderRow("Ecualizador · agudos", -24, 24, h.TrebleGainDb, v => h.TrebleGainDb = (int)v, "0' dB'"),
                Ui.Text(rt.Platform.AudioSource is { IsAvailable: true } ? "Captura WASAPI loopback disponible." : "No disponible en esta plataforma (requiere Windows).", "muted")), 400)));

        // ---- native audio haptics
        var native = rt.Platform.HapticAudio;
        var nativeStatus = Ui.Text(native == null ? "No disponible en esta plataforma (requiere Windows)." : "Pulsa «Comprobar» con el mando conectado por USB.", "muted");
        void Tone(bool l, bool r, double f0, double f1)
        {
            if (native == null) return;
            Task.Run(() => { bool ok = native.IsAvailable; if (ok) native.Play(HapticAudioProcessor.TestTone(48000, 1.0, f0, f1), 48000, l, r); Avalonia.Threading.Dispatcher.UIThread.Post(() => nativeStatus.Text = native.Status); });
        }
        root.Children.Add(Card("Audio háptico nativo (USB)",
            Ui.Text("Reproduce una señal de audio directamente en los actuadores, como hacen los juegos de PS5. Es distinto de la vibración clásica: transmite textura y frecuencia, no solo intensidad. " +
                    "Por USB el DualSense es una tarjeta de sonido de 4 canales; los canales 3 y 4 son los actuadores izquierdo y derecho.", "muted"),
            new Border { Height = 6 },
            Toggle("Enviar el audio del sistema (filtrado) a los actuadores", h.NativeAudioHaptics, v => h.NativeAudioHaptics = v),
            Ui.Row(8, Ui.Btn("Comprobar", () => Tone(false, false, 0, 0)), Ui.Btn("Probar izquierdo", () => Tone(true, false, 80, 80)), Ui.Btn("Probar derecho", () => Tone(false, true, 80, 80)),
                Ui.Btn("Barrido 40–300 Hz", () => Tone(true, true, 40, 300)), Ui.Btn("Parar", () => native?.Stop())),
            nativeStatus,
            Ui.Text("Bluetooth: Windows no expone los actuadores como dispositivo de audio y el protocolo propietario de Sony para háptica por Bluetooth no está implementado; por Bluetooth se usa la vibración clásica.", "muted")));

        // ---- patterns
        var list = new WrapPanel();
        foreach (var pat in rt.Profiles.AllHapticPatterns.ToList())
        {
            var pattern = pat;
            bool user = rt.Profiles.Library.HapticPatterns.Contains(pattern);
            var buttons = Ui.Row(6, Ui.Btn("▶ Probar", () => AppState.Session?.PlayHaptic(pattern)));
            if (user)
            {
                buttons.Children.Add(Ui.Btn("Editar", () => { _editing = pattern; Rebuild(); }));
                buttons.Children.Add(Ui.Btn("✕", () => { rt.Profiles.Library.HapticPatterns.Remove(pattern); if (_editing == pattern) _editing = null; rt.Profiles.SaveLibrary(); Rebuild(); }));
            }
            var card = Card(null, new TextBlock { Text = pattern.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                Ui.Text($"{pattern.TotalMs} ms · {(pattern.Repeat == 0 ? "bucle" : "×" + pattern.Repeat)}{(user ? " · personalizado" : "")}", "muted"),
                new PatternGraph { Pattern = pattern, Height = 60, Margin = new Avalonia.Thickness(0, 6) }, buttons);
            card.Width = 250; card.Padding = new Avalonia.Thickness(12);
            list.Children.Add(card);
        }
        root.Children.Add(Card("Patrones", list,
            Ui.Row(8,
                Ui.Btn("+ Nuevo patrón", () =>
                {
                    _editing = HapticPattern.Make("Mi patrón", 1, (100, 200, 0), (100, 0, 0), (100, 0, 200));
                    rt.Profiles.Library.HapticPatterns.Add(_editing); rt.Profiles.SaveLibrary(); Rebuild();
                }, true),
                Ui.Btn("Importar…", async () =>
                {
                    var json = await FileIo.OpenTextAsync(this);
                    if (json == null) return;
                    var (r, summary) = rt.Profiles.Import(json);
                    _status = r.Ok ? summary! : string.Join(" ", r.Errors); Rebuild();
                })),
            Ui.Text(_status, "muted")));

        if (_editing != null && rt.Profiles.Library.HapticPatterns.Contains(_editing)) root.Children.Add(PatternEditor(_editing));
        return root;
    }

    private static Control WithWidth(Border b, double w) { b.Width = w; return b; }

    private Control PatternEditor(HapticPattern pat)
    {
        var rt = AppState.Runtime;
        var graph = new PatternGraph { Pattern = pat, Height = 90 };
        void Changed() { graph.InvalidateVisual(); rt.Profiles.SaveLibrary(); }
        var name = new TextBox { Text = pat.Name, Width = 220 };
        name.LostFocus += (_, _) => { pat.Name = string.IsNullOrWhiteSpace(name.Text) ? "Patrón" : name.Text!.Trim(); Changed(); };
        var repeat = new NumericUpDown { Value = pat.Repeat, Minimum = 0, Maximum = 100, Increment = 1, Width = 130, FormatString = "0" };
        repeat.ValueChanged += (_, _) => { pat.Repeat = (int)(repeat.Value ?? 1); Changed(); };
        var steps = new StackPanel { Spacing = 2 };
        foreach (var st in pat.Steps.ToList())
        {
            var step = st;
            NumericUpDown N(int value, int max, Action<int> set)
            {
                var n = new NumericUpDown { Value = value, Minimum = 0, Maximum = max, Increment = max > 255 ? 10 : 5, Width = 130, FormatString = "0" };
                n.ValueChanged += (_, _) => { set((int)(n.Value ?? 0)); graph.InvalidateVisual(); };
                n.LostFocus += (_, _) => Changed();
                return n;
            }
            steps.Children.Add(Ui.Row(8, Ui.Text("ms", "muted"), N(step.DurationMs, 10000, v => step.DurationMs = Math.Max(1, v)), Ui.Text("izq.", "muted"), N(step.Left, 255, v => step.Left = v),
                Ui.Text("der.", "muted"), N(step.Right, 255, v => step.Right = v), Ui.Btn("✕", () => { pat.Steps.Remove(step); Changed(); Rebuild(); })));
        }
        return Card("Editor de patrones", graph, new Border { Height = 6 },
            Ui.Row(12, Labeled("Nombre", name), Labeled("Repeticiones (0 = bucle)", repeat)),
            Ui.Text("Pasos: duración e intensidad (0-255) de cada actuador", "label"), steps,
            Ui.Row(8, Ui.Btn("+ Paso", () => { if (pat.Steps.Count < 256) { pat.Steps.Add(new HapticStep { DurationMs = 100, Left = 128, Right = 128 }); Changed(); Rebuild(); } }),
                Ui.Btn("▶ Probar", () => AppState.Session?.PlayHaptic(pat)),
                Ui.Btn("Exportar…", async () => await FileIo.SaveTextAsync(this, pat.Name, JsonFile.Serialize(SharePackage.Wrap(PackageKinds.Haptic, pat.Name, pat)))),
                Ui.Btn("Cerrar editor", () => { _editing = null; Rebuild(); })));
    }
}
