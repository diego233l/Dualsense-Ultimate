using Avalonia.Controls;
using Avalonia.Media;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Storage;
using DualSenseUltimate.Core.Virtual;

namespace DualSenseUltimate.App.Pages;

public sealed class RemapPage : PageBase
{
    public override string Title => "Remapeo";
    public override string Subtitle => "Botones, macros, touchpad y emulación de mandos virtuales";
    private string _selected = "Cross";
    private Macro? _macro;

    public void Select(string source) { _selected = source; Rebuild(); }

    public static string SourceName(string s) => s switch
    {
        "Cross" => "✕ Cruz", "Circle" => "○ Círculo", "Square" => "□ Cuadrado", "Triangle" => "△ Triángulo", "Create" => "Create", "Options" => "Options",
        "PS" => "Botón PS", "Touchpad" => "Clic del touchpad", "Mute" => "Botón de silencio", "DpadUp" => "Cruceta arriba", "DpadDown" => "Cruceta abajo",
        "DpadLeft" => "Cruceta izquierda", "DpadRight" => "Cruceta derecha", "FnLeft" => "Fn izquierdo (Edge)", "FnRight" => "Fn derecho (Edge)",
        "PaddleLeft" => "Paleta trasera izquierda (Edge)", "PaddleRight" => "Paleta trasera derecha (Edge)",
        Sources.TouchLeft => "Touchpad · mitad izquierda", Sources.TouchRight => "Touchpad · mitad derecha", Sources.TouchTopLeft => "Touchpad · arriba izquierda",
        Sources.TouchTopRight => "Touchpad · arriba derecha", Sources.TouchBottomLeft => "Touchpad · abajo izquierda", Sources.TouchBottomRight => "Touchpad · abajo derecha",
        Sources.SwipeUp => "Gesto: deslizar arriba", Sources.SwipeDown => "Gesto: deslizar abajo", Sources.SwipeLeft => "Gesto: deslizar izquierda",
        Sources.SwipeRight => "Gesto: deslizar derecha", Sources.GyroShake => "Gesto: agitar el mando", _ => s,
    };

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var p = AppState.EditProfile;
        var m = p.Mapping;
        var session = AppState.Session;
        var root = new StackPanel();
        if (session == null) root.Children.Add(NoController());
        if (rt.Platform.Injector is not { IsAvailable: true })
            root.Children.Add(Notice("La salida de teclado y ratón solo está disponible en Windows. Puedes preparar las asignaciones igualmente."));

        IEnumerable<string> MacroNames() => m.Macros.Select(x => x.Name).Concat(rt.Profiles.Library.Macros.Select(x => x.Name)).Distinct();

        // ---- controller + binding editor
        var view = new ControllerView { Height = 340, Selected = _selected, TouchMode = m.Touchpad.Mode, Bound = m.Bindings.Keys.ToHashSet(), IsEdge = session?.Device.Model == ControllerModel.DualSenseEdge || session == null };
        view.SourceClicked += Select;
        Live(() => { if (AppState.Session is { } s) { view.State = s.Device.State; view.LightColor = Color.FromRgb(s.LastOutput.Red, s.LastOutput.Green, s.LastOutput.Blue); view.PlayerLeds = s.LastOutput.PlayerLeds; view.InvalidateVisual(); } });
        var others = new ComboBox { ItemsSource = Sources.Virtual.Select(SourceName).ToList(), PlaceholderText = "Gestos y zonas…", Width = 240 };
        others.SelectionChanged += (_, _) => { if (others.SelectedIndex >= 0) Select(Sources.Virtual[others.SelectedIndex]); };

        var editor = new StackPanel { Spacing = 4 };
        m.Bindings.TryGetValue(_selected, out var binding);
        editor.Children.Add(new TextBlock { Text = SourceName(_selected), FontSize = 18, FontWeight = FontWeight.Bold });
        if (binding == null)
        {
            bool passes = Enum.TryParse<PadButtons>(_selected, out var pb) && (pb & Sources.PassThrough) != 0;
            editor.Children.Add(Ui.Text(passes ? "Sin remapear: el botón se comporta de forma normal." : "Sin asignar: este origen no hace nada por ahora.", "muted"));
            editor.Children.Add(new Border { Height = 6 });
            editor.Children.Add(Ui.Btn("Personalizar", () =>
            {
                m.Bindings[_selected] = new ButtonBinding { Press = passes ? [new MapAction(ActionType.PadButton, _selected)] : [new MapAction(ActionType.Key, "Space")] };
                Rebuild();
            }, true));
        }
        else
        {
            var b = binding;
            void Changed() { }
            editor.Children.Add(Ui.Text("Al pulsar", "label"));
            editor.Children.Add(new ActionListEditor(b.Press, MacroNames, Changed));
            editor.Children.Add(EnumCombo("Modo", b.Mode, v => { b.Mode = v; Rebuild(); }, x => x switch { PressMode.Toggle => "Alternar (toggle)", PressMode.Turbo => "Turbo (repetición)", _ => "Normal (mientras se mantiene)" }, 290));
            if (b.Mode == PressMode.Turbo) editor.Children.Add(SliderRow("Velocidad del turbo", 1, 30, b.TurboHz, v => b.TurboHz = v, "0' Hz'"));

            void Layer(string title, string hint, Func<List<MapAction>?> get, Action<List<MapAction>?> set)
            {
                var list = get();
                var toggle = Toggle(title, list != null, v => { set(v ? [new MapAction(ActionType.Key, "Space")] : null); Rebuild(); });
                editor.Children.Add(toggle);
                if (list != null) { editor.Children.Add(Ui.Text(hint, "muted")); editor.Children.Add(new ActionListEditor(list, MacroNames, Changed)); }
            }
            Layer("Pulsación larga", "Se mantiene mientras sigas pulsando tras el tiempo indicado. «Al pulsar» pasa a ser pulsación corta.", () => b.Hold, v => b.Hold = v);
            if (b.Hold != null) editor.Children.Add(SliderRow("Tiempo de pulsación larga", 150, 1500, b.HoldMs, v => b.HoldMs = (int)v, "0' ms'", 50));
            Layer("Doble pulsación", "Se dispara al pulsar dos veces seguidas.", () => b.DoubleTap, v => b.DoubleTap = v);
            if (b.DoubleTap != null) editor.Children.Add(SliderRow("Ventana de doble pulsación", 100, 600, b.DoubleTapMs, v => b.DoubleTapMs = (int)v, "0' ms'", 10));
            Layer("Con modificador (shift)", "Sustituye a «Al pulsar» mientras mantienes el botón modificador.", () => b.Shift, v => b.Shift = v);
            editor.Children.Add(new Border { Height = 8 });
            editor.Children.Add(Ui.Btn("Restablecer este botón", () => { m.Bindings.Remove(_selected); Rebuild(); }));
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,4*") };
        var left = Card("Selecciona un botón", view, Ui.Row(10, others, Ui.Text("● naranja = personalizado", "muted")));
        var right = Card("Asignación", new ScrollViewer { MaxHeight = 420, Content = editor });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left); grid.Children.Add(right);
        root.Children.Add(grid);

        // ---- emulation / hybrid
        var e = p.Emulation;
        var emuStatus = Ui.Text("", "muted");
        Live(() => emuStatus.Text = "Estado: " + (AppState.Session?.EmulationStatus ?? "sin mando") +
                                    $"   ·   {rt.Platform.PadFactory?.Status ?? "ViGEmBus: no disponible en esta plataforma"}   ·   {rt.Platform.Hider?.Status ?? "HidHide: no disponible en esta plataforma"}");
        root.Children.Add(Card("Emulación y modo híbrido",
            Wrap(EnumCombo("El juego ve…", e.Mode, v => { e.Mode = v; Rebuild(); }, x => x switch
                {
                    EmulationMode.Xbox360 => "Mando Xbox (XInput) virtual", EmulationMode.DualShock4 => "DualShock 4 virtual", _ => "El DualSense real (híbrido)",
                }, 280)),
            e.Mode == EmulationMode.None
                ? Ui.Text("Modo híbrido: el juego habla directamente con el DualSense, así que conserva gatillos adaptativos, háptica y touchpad nativos. " +
                          "Tus asignaciones a teclado, ratón, macros, giroscopio-ratón y las paletas del Edge se añaden por encima. " +
                          "Limitación real: sin mando virtual no se puede sustituir un botón por otro botón de mando ni anular el original, porque el juego lee el dispositivo físico.", "muted")
                : Ui.Text("Emulación completa: se crea un mando virtual (requiere ViGEmBus) con todas tus asignaciones y el mando físico se oculta con HidHide para evitar la doble entrada. " +
                          "El juego ya no ve un DualSense, por lo que sus efectos nativos de gatillo/háptica se pierden; la vibración del mando virtual se reenvía al mando real y los efectos de gatillo de este perfil siguen activos. " +
                          "«Xbox One» equivale a este mando XInput: los juegos no los distinguen.", "muted"),
            new Border { Height = 4 },
            e.Mode == EmulationMode.None
                ? Wrap(Toggle("Dejar gatillos al juego", e.NativeTriggers, v => e.NativeTriggers = v), new Border { Width = 18 },
                    Toggle("Dejar vibración al juego", e.NativeRumble, v => e.NativeRumble = v), new Border { Width = 18 },
                    Toggle("Dejar luces al juego", e.NativeLighting, v => e.NativeLighting = v))
                : Wrap(Toggle("Ocultar el mando físico (HidHide)", e.HideOriginal, v => e.HideOriginal = v), new Border { Width = 18 },
                    Toggle("Reenviar vibración del juego", e.ForwardRumble, v => e.ForwardRumble = v)),
            emuStatus,
            Ui.Text("Steam: para evitar entradas duplicadas, desactiva Steam Input para este juego si usas emulación, o deja que Steam gestione el mando y usa el modo híbrido sin asignaciones de botón.", "muted")));

        // ---- touchpad
        var t = m.Touchpad;
        root.Children.Add(Card("Touchpad",
            Wrap(EnumCombo("Función", t.Mode, v => { t.Mode = v; Rebuild(); }, x => x switch
                {
                    TouchpadMode.Mouse => "Ratón", TouchpadMode.TwoRegions => "2 regiones asignables", TouchpadMode.FourRegions => "4 regiones asignables",
                    TouchpadMode.Disabled => "Desactivado", _ => "Normal (se pasa al juego)",
                }, 240),
                new Border { Width = 16 },
                SliderRow("Sensibilidad", 0.2, 4, t.Sensitivity, v => t.Sensitivity = v, "0.0'×'", 0.1)),
            Wrap(Toggle("Toque = clic", t.TapToClick, v => t.TapToClick = v), new Border { Width = 18 },
                Toggle("Desplazamiento con dos dedos", t.TwoFingerScroll, v => t.TwoFingerScroll = v), new Border { Width = 18 },
                Toggle("Gestos de deslizamiento", t.SwipeGestures, v => t.SwipeGestures = v)),
            Ui.Text("En modo ratón: mover el dedo mueve el cursor, pulsar es clic izquierdo y pulsar con dos dedos es clic derecho. En modo regiones, pulsa una zona en el dibujo para asignarla.", "muted")));

        // ---- modifier + chords
        var buttonNames = Sources.Buttons.Select(x => x.ToString()).ToList();
        var shiftBox = new ComboBox { ItemsSource = new[] { "(ninguno)" }.Concat(buttonNames.Select(SourceName)).ToList(), SelectedIndex = m.ShiftButton == null ? 0 : buttonNames.IndexOf(m.ShiftButton) + 1, Width = 240 };
        shiftBox.SelectionChanged += (_, _) => m.ShiftButton = shiftBox.SelectedIndex <= 0 ? null : buttonNames[shiftBox.SelectedIndex - 1];
        var chords = new StackPanel { Spacing = 8 };
        foreach (var ch in m.Chords.ToList())
        {
            var chord = ch;
            Control Pick(int i)
            {
                var c = new ComboBox { ItemsSource = buttonNames.Select(SourceName).ToList(), SelectedIndex = buttonNames.IndexOf(chord.Buttons.ElementAtOrDefault(i) ?? ""), Width = 190 };
                c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) { while (chord.Buttons.Count <= i) chord.Buttons.Add("L1"); chord.Buttons[i] = buttonNames[c.SelectedIndex]; } };
                return c;
            }
            chords.Children.Add(Stack(Ui.Row(6, Pick(0), Ui.Text("+"), Pick(1), Ui.Btn("✕", () => { m.Chords.Remove(chord); Rebuild(); })),
                new Border { Margin = new Avalonia.Thickness(0, 4, 0, 0), Child = new ActionListEditor(chord.Actions, MacroNames, () => { }) }));
        }
        root.Children.Add(Card("Modificador y combinaciones",
            Labeled("Botón modificador (shift)", shiftBox),
            Ui.Text("Combinaciones: al pulsar varios botones a la vez se ejecutan estas acciones y se ignoran sus funciones individuales.", "label"),
            chords,
            Ui.Btn("+ Combinación", () => { m.Chords.Add(new ChordBinding { Buttons = ["L1", "R1"], Actions = [new MapAction(ActionType.Key, "F5")] }); Rebuild(); }),
            Ui.Text("Seguridad: PS + Options + Create siempre activa/desactiva el remapeo, para que un perfil erróneo nunca te deje sin control.", "muted")));

        // ---- macros
        var macroList = new WrapPanel();
        foreach (var mac in m.Macros.ToList())
        {
            var macro = mac;
            macroList.Children.Add(Ui.Row(0, Ui.Btn(macro.Name, () => { _macro = macro; Rebuild(); }, _macro == macro), Ui.Btn("✕", () => { m.Macros.Remove(macro); if (_macro == macro) _macro = null; Rebuild(); })));
            macroList.Children.Add(new Border { Width = 8 });
        }
        var macroCard = Card("Macros", Ui.Text("Secuencias temporizadas de teclas, botones y pausas. Asígnalas a un botón con la acción «Macro».", "muted"), macroList,
            Ui.Row(8, Ui.Btn("+ Nueva macro", () =>
            {
                _macro = new Macro { Name = "Macro " + (m.Macros.Count + 1), Steps = [new MacroStep { Kind = MacroStepKind.Tap, Action = new MapAction(ActionType.Key, "A"), Ms = 50 }] };
                m.Macros.Add(_macro); Rebuild();
            }),
            Ui.Btn("Importar…", async () =>
            {
                var json = await FileIo.OpenTextAsync(this);
                if (json == null) return;
                var (r, _) = rt.Profiles.Import(json);
                if (r.Ok && rt.Profiles.Library.Macros.LastOrDefault() is { } imported) { m.Macros.Add(imported); _macro = imported; }
                Rebuild();
            })));
        if (_macro != null && m.Macros.Contains(_macro)) ((StackPanel)macroCard.Child!).Children.Add(MacroEditor(_macro, MacroNames));
        root.Children.Add(macroCard);
        return root;
    }

    private Control MacroEditor(Macro macro, Func<IEnumerable<string>> macroNames)
    {
        var name = new TextBox { Text = macro.Name, Width = 220 };
        name.LostFocus += (_, _) => { macro.Name = string.IsNullOrWhiteSpace(name.Text) ? "Macro" : name.Text!.Trim(); Rebuild(); };
        var steps = new StackPanel { Spacing = 6 };
        foreach (var st in macro.Steps.ToList())
        {
            var step = st;
            var kinds = Enum.GetValues<MacroStepKind>();
            string[] kindNames = ["Mantener", "Soltar", "Pulsar y soltar", "Esperar"];
            var kind = new ComboBox { ItemsSource = kindNames, SelectedIndex = Array.IndexOf(kinds, step.Kind), Width = 150 };
            kind.SelectionChanged += (_, _) =>
            {
                step.Kind = kinds[kind.SelectedIndex];
                if (step.Kind != MacroStepKind.Delay && step.Action == null) step.Action = new MapAction(ActionType.Key, "A");
                Rebuild();
            };
            var row = Ui.Row(6, kind);
            if (step.Kind != MacroStepKind.Delay)
            {
                var single = new List<MapAction> { step.Action ?? new MapAction(ActionType.Key, "A") };
                step.Action = single[0];
                var typeNames = new[] { ActionType.Key, ActionType.PadButton, ActionType.MouseButton };
                var tBox = new ComboBox { ItemsSource = new[] { "Tecla", "Botón de mando", "Botón de ratón" }, SelectedIndex = Math.Max(0, Array.IndexOf(typeNames, step.Action.Type)), Width = 150 };
                IEnumerable<string> Targets(ActionType t) => t switch
                {
                    ActionType.PadButton => Sources.Buttons.Where(b => (b & Sources.PassThrough) != 0).Select(b => b.ToString()),
                    ActionType.MouseButton => ["Left", "Right", "Middle", "X1", "X2"],
                    _ => KeyNames.VirtualKeys.Keys.OrderBy(k => k.Length > 1).ThenBy(k => k),
                };
                var target = new ComboBox { ItemsSource = Targets(step.Action.Type).ToList(), SelectedItem = step.Action.Target, Width = 150 };
                target.SelectionChanged += (_, _) => { if (target.SelectedItem is string s) step.Action!.Target = s; };
                tBox.SelectionChanged += (_, _) => { step.Action!.Type = typeNames[tBox.SelectedIndex]; step.Action.Target = Targets(step.Action.Type).First(); Rebuild(); };
                row.Children.Add(tBox); row.Children.Add(target);
            }
            if (step.Kind is MacroStepKind.Delay or MacroStepKind.Tap)
            {
                var ms = new NumericUpDown { Value = step.Ms, Minimum = 1, Maximum = 60000, Increment = 10, Width = 140, FormatString = "0" };
                ms.ValueChanged += (_, _) => step.Ms = (int)(ms.Value ?? 50);
                row.Children.Add(ms); row.Children.Add(Ui.Text("ms", "muted"));
            }
            row.Children.Add(Ui.Btn("✕", () => { macro.Steps.Remove(step); Rebuild(); }));
            steps.Children.Add(row);
        }
        return Stack(new Border { Height = 10 }, Ui.Row(12, Labeled("Nombre", name),
                new Border { Margin = new Avalonia.Thickness(0, 22, 0, 0), Child = Toggle("Repetir mientras se mantiene", macro.RepeatWhileHeld, v => macro.RepeatWhileHeld = v) }),
            Ui.Text("Pasos", "label"), steps, new Border { Height = 6 },
            Ui.Row(8, Ui.Btn("+ Paso", () => { if (macro.Steps.Count < 256) { macro.Steps.Add(new MacroStep { Kind = MacroStepKind.Delay, Ms = 100 }); Rebuild(); } }),
                Ui.Btn("Exportar…", async () => await FileIo.SaveTextAsync(this, macro.Name, JsonFile.Serialize(SharePackage.Wrap(PackageKinds.Macro, macro.Name, macro))))));
    }
}
