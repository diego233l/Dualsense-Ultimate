using Avalonia.Controls;
using Avalonia.Media;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Runtime;

namespace DualSenseUltimate.App.Pages;

public sealed class ControllersPage : PageBase
{
    public override string Title => "Mandos conectados";
    public override string Subtitle => "Identificación, estado y perfil de cada mando";

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var root = new StackPanel();
        if (rt.Sessions.Count == 0) root.Children.Add(NoController());
        foreach (var s in rt.Sessions) root.Children.Add(Describe(s));
        root.Children.Add(Card("Detección",
            Ui.Text("Se admiten DualSense (054C:0CE6), DualSense Edge (054C:0DF2) y DualShock 4 (054C:05C4 / 09CC / adaptador 0BA0) por USB y Bluetooth. " +
                    "Los mandos se abren en modo compartido: Steam y los juegos pueden seguir usándolos.", "muted"),
            new Border { Height = 8 },
            Ui.Row(8, Ui.Btn("Buscar ahora", () => Task.Run(rt.Devices.Scan)),
                Toggle("Mando simulado (solo para probar la interfaz)", rt.Settings.SimulationEnabled, v => Task.Run(() => rt.EnableSimulation(v))))));
        return root;
    }

    private Control Describe(ControllerSession s)
    {
        var rt = AppState.Runtime;
        var d = s.Device;
        var name = new TextBox { Text = rt.Settings.ControllerNames.GetValueOrDefault(s.Id, ""), Watermark = AppRuntime.ModelName(d.Model), Width = 240 };
        name.LostFocus += (_, _) =>
        {
            rt.Settings.ControllerNames[s.Id] = name.Text?.Trim() ?? "";
            rt.SaveSettings(); AppState.SelectController(AppState.SelectedControllerId);
        };
        var profiles = rt.Profiles.Profiles.OrderBy(p => p.Name).ToList();
        var picker = new ComboBox { ItemsSource = profiles.Select(p => p.Name).ToList(), SelectedIndex = profiles.FindIndex(p => p.Id == s.Profile.Id), Width = 240 };
        picker.SelectionChanged += (_, _) => { if (picker.SelectedIndex >= 0) rt.AssignProfile(s.Id, profiles[picker.SelectedIndex].Id); };

        var live = Ui.Text("", "muted");
        Live(() =>
        {
            var st = d.State;
            live.Text = $"Batería: {Ui.BatteryText(st)}   ·   Informes: {d.ReportCount:N0} ({d.ReportRate:0} Hz)   ·   Errores: {d.ErrorCount}   ·   Estado: {d.Health}   ·   Escrituras de la app: {d.WriteCount:N0}" +
                        (st.HeadphonesConnected ? "   ·   Auriculares conectados" : "") + (st.MicMuted ? "   ·   Micrófono silenciado" : "");
        });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        void Row(int r, string k, string v)
        {
            var a = Ui.Text(k, "muted"); var b = new TextBlock { Text = v, FontSize = 13, Margin = new Avalonia.Thickness(0, 3) };
            Grid.SetRow(a, r); Grid.SetRow(b, r); Grid.SetColumn(b, 1);
            grid.Children.Add(a); grid.Children.Add(b);
        }
        Row(0, "Modelo", AppRuntime.ModelName(d.Model) + (d.IsSimulated ? "  (SIMULADO: no es hardware real)" : ""));
        Row(1, "Identificador (MAC)", s.Id);
        Row(2, "Conexión", d.Connection == ConnectionType.Usb ? "USB" : "Bluetooth");
        Row(3, "Firmware", d.FirmwareVersion == null ? "No disponible" : d.FirmwareVersion + (d.FirmwareDate is { Length: > 0 } fd ? $"  ({fd})" : ""));
        Row(4, "Calibración IMU de fábrica", d.Calibration.Valid ? "Leída del mando" : "No disponible (se usan valores nominales)");
        Row(5, "Temperatura de batería", "No disponible: el mando no expone este dato por HID");
        Row(6, "Emulación", s.EmulationStatus);

        var card = Card(rt.DisplayName(s), grid, new Border { Height = 6 }, live, new Border { Height = 8 },
            Wrap(Labeled("Nombre personalizado", name), new Border { Width = 16 }, Labeled("Perfil asignado", picker)),
            new Border { Height = 8 },
            Ui.Row(8, Ui.Btn("Identificar (vibrar y parpadear)", () => { s.PlayHaptic(Core.Haptics.HapticPattern.BuiltIn[1]); s.SetLightbarOverride(new Core.Lighting.Rgb(255, 255, 255), 900); }),
                Ui.Btn("Detener todos los efectos", s.StopEffects)));
        if (AppState.Session?.Id == s.Id) card.BorderBrush = Ui.Brush(this, "Accent");
        return card;
    }
}
