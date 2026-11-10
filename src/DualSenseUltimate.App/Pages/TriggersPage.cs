using Avalonia.Controls;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.App.Pages;

public sealed class TriggersPage : PageBase
{
    public override string Title => "Adaptive Triggers";
    public override string Subtitle => "Editor visual de los gatillos adaptativos L2 y R2";
    private string _status = "";

    public static string TypeName(TriggerEffectType t) => t switch
    {
        TriggerEffectType.Off => "Sin efecto", TriggerEffectType.Feedback => "Resistencia constante", TriggerEffectType.SlopeFeedback => "Resistencia progresiva",
        TriggerEffectType.MultiZoneFeedback => "Resistencia por zonas", TriggerEffectType.Weapon => "Arma de fuego", TriggerEffectType.Bow => "Arco / tensión",
        TriggerEffectType.Vibration => "Vibración del gatillo", TriggerEffectType.MultiZoneVibration => "Vibración por zonas", TriggerEffectType.Galloping => "Pulsación (galope)",
        TriggerEffectType.Machine => "Máquina (vibración alterna)", _ => "Parámetros de bajo nivel",
    };

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var p = AppState.EditProfile;
        var root = new StackPanel();
        var session = AppState.Session;
        if (session == null) root.Children.Add(NoController());
        else if (!session.Device.IsDualSense) root.Children.Add(Notice("El DualShock 4 no tiene gatillos adaptativos. Estos ajustes se aplicarán cuando uses un DualSense."));
        if (p.Emulation.NativeTriggers) root.Children.Add(Notice("Modo nativo activado: la aplicación no escribe los gatillos y el juego mantiene sus propios efectos. Los efectos de abajo no se envían al mando."));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var l2 = Editor("L2 · gatillo izquierdo", () => p.L2, e => p.L2 = e, () => p.R2 = p.L2.Clone(), "R2", true);
        var r2 = Editor("R2 · gatillo derecho", () => p.R2, e => p.R2 = e, () => p.L2 = p.R2.Clone(), "L2", false);
        Grid.SetColumn(r2, 1);
        grid.Children.Add(l2); grid.Children.Add(r2);
        root.Children.Add(grid);

        root.Children.Add(Card("Biblioteca e intercambio",
            Ui.Row(8,
                Ui.Btn("Importar efecto…", async () =>
                {
                    var json = await FileIo.OpenTextAsync(this);
                    if (json == null) return;
                    var (r, summary) = rt.Profiles.Import(json);
                    _status = r.Ok ? summary! : string.Join(" ", r.Errors); Rebuild();
                }),
                Ui.Btn("Detener pruebas", () => AppState.Session?.ClearOverrides())),
            Ui.Text(_status, "muted"),
            Ui.Text($"Efectos en tu biblioteca: {rt.Profiles.Library.TriggerEffects.Count}. Los efectos guardados aparecen en la lista «Preset» de cada gatillo y en la Biblioteca comunitaria.", "muted")));

        root.Children.Add(Card("Efectos manuales y efectos dinámicos",
            Ui.Text("• Manuales: los que configuras aquí. Se aplican de forma fija mientras el perfil está activo y cambian automáticamente al cambiar de perfil (por ejemplo, al detectar un juego).", "muted"),
            Ui.Text("• Dinámicos (munición, daño recibido, velocidad del vehículo…): dependen del estado interno del juego. Ninguna aplicación externa puede leer ese estado de forma universal. " +
                    "Solo son posibles si el propio juego los envía (juegos con soporte nativo de DualSense: activa «Dejar los gatillos al juego») o mediante una integración específica " +
                    "que llame a la API local o a un plugin (telemetría de simuladores, mods, etc.).", "muted"),
            new Border { Height = 6 },
            Toggle("Dejar los gatillos al juego (modo nativo / híbrido)", p.Emulation.NativeTriggers, v => { p.Emulation.NativeTriggers = v; Rebuild(); })));
        return root;
    }

    private Control Editor(string title, Func<TriggerEffect> get, Action<TriggerEffect> set, Action copyToOther, string other, bool left)
    {
        var rt = AppState.Runtime;
        var e = get();
        var graph = new TriggerGraph { TriggerEffect = e, Height = 170 };
        var liveText = Ui.Text("", "muted");
        Live(() =>
        {
            var s = AppState.Session;
            graph.TriggerEffect = get();
            if (s != null)
            {
                var st = s.Device.State;
                graph.Position = left ? st.L2 : st.R2;
                liveText.Text = $"Recorrido: {(left ? st.L2 : st.R2) * 100 / 255} %   ·   Estado del actuador: 0x{(left ? st.L2FeedbackStatus : st.R2FeedbackStatus):X2}";
            }
            else liveText.Text = "Zona de pruebas: conecta un mando y aprieta el gatillo para sentir el efecto.";
            graph.InvalidateVisual();
        });

        var presets = rt.Profiles.AllTriggerEffects.ToList();
        var presetBox = new ComboBox { ItemsSource = presets.Select(x => x.Name).ToList(), PlaceholderText = "Elegir preset…", Width = 230 };
        presetBox.SelectionChanged += (_, _) =>
        {
            if (presetBox.SelectedIndex < 0) return;
            set(presets[presetBox.SelectedIndex].Clone()); Rebuild();
        };

        var pars = new StackPanel();
        Control S(string label, int min, int max, int value, Action<int> setter, string fmt = "0") => SliderRow(label, min, max, Math.Clamp(value, min, max), v => setter((int)v), fmt);
        switch (e.Type)
        {
            case TriggerEffectType.Feedback:
                pars.Children.Add(S("Posición inicial (zona)", 0, 9, e.Start, v => e.Start = v));
                pars.Children.Add(S("Resistencia", 1, 8, e.Strength, v => e.Strength = v)); break;
            case TriggerEffectType.SlopeFeedback:
                pars.Children.Add(S("Posición inicial (zona)", 0, 8, e.Start, v => e.Start = v));
                pars.Children.Add(S("Posición final (zona)", 1, 9, e.End, v => e.End = v));
                pars.Children.Add(S("Resistencia inicial", 1, 8, e.Strength, v => e.Strength = v));
                pars.Children.Add(S("Resistencia final", 1, 8, e.Strength2, v => e.Strength2 = v)); break;
            case TriggerEffectType.Weapon:
                pars.Children.Add(S("Inicio del punto de disparo (zona)", 2, 7, e.Start, v => e.Start = v));
                pars.Children.Add(S("Fin del punto de disparo (zona)", 3, 8, e.End, v => e.End = v));
                pars.Children.Add(S("Fuerza", 1, 8, e.Strength, v => e.Strength = v)); break;
            case TriggerEffectType.Bow:
                pars.Children.Add(S("Posición inicial (zona)", 1, 7, e.Start, v => e.Start = v));
                pars.Children.Add(S("Posición final (zona)", 2, 8, e.End, v => e.End = v));
                pars.Children.Add(S("Tensión", 1, 8, e.Strength, v => e.Strength = v));
                pars.Children.Add(S("Fuerza del retroceso", 1, 8, e.Strength2, v => e.Strength2 = v)); break;
            case TriggerEffectType.Vibration:
                pars.Children.Add(S("Posición inicial (zona)", 0, 9, e.Start, v => e.Start = v));
                pars.Children.Add(S("Intensidad", 1, 8, e.Strength, v => e.Strength = v));
                pars.Children.Add(S("Frecuencia", 1, 255, e.Frequency, v => e.Frequency = v, "0' Hz'")); break;
            case TriggerEffectType.Galloping:
                pars.Children.Add(S("Posición inicial (zona)", 0, 8, e.Start, v => e.Start = v));
                pars.Children.Add(S("Posición final (zona)", 1, 9, e.End, v => e.End = v));
                pars.Children.Add(S("Primer golpe (fase 0-6)", 0, 6, e.FirstFoot, v => e.FirstFoot = v));
                pars.Children.Add(S("Segundo golpe (fase 1-7)", 1, 7, e.SecondFoot, v => e.SecondFoot = v));
                pars.Children.Add(S("Frecuencia", 1, 255, e.Frequency, v => e.Frequency = v, "0' Hz'")); break;
            case TriggerEffectType.Machine:
                pars.Children.Add(S("Posición inicial (zona)", 1, 8, e.Start, v => e.Start = v));
                pars.Children.Add(S("Posición final (zona)", 2, 9, e.End, v => e.End = v));
                pars.Children.Add(S("Intensidad A", 0, 7, e.Strength, v => e.Strength = v));
                pars.Children.Add(S("Intensidad B", 0, 7, e.Strength2, v => e.Strength2 = v));
                pars.Children.Add(S("Frecuencia", 1, 255, e.Frequency, v => e.Frequency = v, "0' Hz'"));
                pars.Children.Add(S("Duración del ciclo (décimas de s)", 0, 255, e.Period, v => e.Period = v)); break;
            case TriggerEffectType.MultiZoneFeedback: case TriggerEffectType.MultiZoneVibration:
            {
                var zones = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
                for (int i = 0; i < 10; i++)
                {
                    int idx = i;
                    var sl = new Slider { Orientation = Avalonia.Layout.Orientation.Vertical, Minimum = 0, Maximum = 8, Value = e.Zones[i], Height = 110, MinWidth = 0, Width = 30, TickFrequency = 1, IsSnapToTickEnabled = true };
                    sl.ValueChanged += (_, a) => e.Zones[idx] = (int)a.NewValue;
                    zones.Children.Add(Stack(sl, new TextBlock { Text = i.ToString(), FontSize = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }));
                }
                pars.Children.Add(Ui.Text("Fuerza por zona (0 = libre, 8 = máxima)", "label"));
                pars.Children.Add(zones);
                if (e.Type == TriggerEffectType.MultiZoneVibration) pars.Children.Add(S("Frecuencia", 1, 255, e.Frequency, v => e.Frequency = v, "0' Hz'"));
                break;
            }
            case TriggerEffectType.Raw:
            {
                var hex = new TextBox { Text = Convert.ToHexString(e.RawBytes), Width = 260, MaxLength = 22, FontFamily = "Consolas,monospace" };
                var err = Ui.Text("11 bytes en hexadecimal: modo + 10 parámetros. Para experimentación; los valores no documentados pueden no hacer nada.", "muted");
                hex.TextChanged += (_, _) =>
                {
                    try { var b = Convert.FromHexString((hex.Text ?? "").Trim()); if (b.Length == 11) e.RawBytes = b; } catch (FormatException) { }
                };
                pars.Children.Add(Labeled("Bloque en bruto", hex)); pars.Children.Add(err);
                break;
            }
        }

        var bytes = Ui.Text("", "muted");
        void ShowBytes() => bytes.Text = "Informe HID: " + Convert.ToHexString(TriggerEffectEncoder.Encode(get()));
        ShowBytes(); OnContent(ShowBytes);

        var name = new TextBox { Text = e.Name, Watermark = "Nombre del efecto", Width = 200 };
        name.TextChanged += (_, _) => e.Name = name.Text ?? "";

        return Card(title, graph, liveText, new Border { Height = 4 },
            Wrap(Labeled("Preset", presetBox), new Border { Width = 12 },
                EnumCombo("Tipo de efecto", e.Type, v => { e.Type = v; if (v == TriggerEffectType.Raw) e.RawBytes = TriggerEffectEncoder.Off(); Rebuild(); }, TypeName, 230)),
            pars, bytes, new Border { Height = 8 },
            Wrap(name, new Border { Width = 8 },
                Ui.Btn("Guardar en biblioteca", () =>
                {
                    var copy = get().Clone(); if (copy.Name.Length == 0) copy.Name = TypeName(copy.Type);
                    rt.Profiles.Library.TriggerEffects.Add(copy); rt.Profiles.SaveLibrary(); _status = $"«{copy.Name}» guardado en la biblioteca."; Rebuild();
                }),
                new Border { Width = 8 },
                Ui.Btn("Exportar…", async () =>
                {
                    var cur = get(); string n = cur.Name.Length > 0 ? cur.Name : TypeName(cur.Type);
                    await FileIo.SaveTextAsync(this, n, JsonFile.Serialize(SharePackage.Wrap(PackageKinds.Trigger, n, cur)));
                }),
                new Border { Width = 8 },
                Ui.Btn($"Copiar a {other}", () => { copyToOther(); Rebuild(); })));
    }
}
