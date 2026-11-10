using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DualSenseUltimate.App.Controls;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Runtime;

namespace DualSenseUltimate.App.Pages;

public sealed class DashboardPage(MainWindow window) : PageBase
{
    public override string Title => "Dashboard";
    public override string Subtitle => "Estado de tus mandos en tiempo real";

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var sessions = rt.Sessions;
        var root = new StackPanel();

        if (sessions.Count == 0)
        {
            root.Children.Add(Card("Ningún mando conectado",
                Ui.Text("Conecta un DualSense, DualSense Edge o DualShock 4 por USB, o emparéjalo por Bluetooth desde la configuración de Windows. Se detectará automáticamente.", "muted"),
                new Border { Height = 10 },
                Ui.Row(8, Ui.Btn("Activar mando simulado", () => rt.EnableSimulation(true)),
                    Ui.Text("Para explorar la interfaz sin hardware. Siempre se muestra marcado como simulado.", "muted"))));
        }

        var cards = new WrapPanel();
        foreach (var s in sessions) cards.Children.Add(ControllerCard(s));
        root.Children.Add(cards);

        var session = AppState.Session;
        if (session != null)
        {
            var view = new ControllerView { Height = 330, IsEdge = session.Device.Model == ControllerModel.DualSenseEdge };
            view.SourceClicked += src => { window.Navigate(5); window.Page<RemapPage>()?.Select(src); };
            Live(() =>
            {
                view.State = session.Device.State;
                var o = session.LastOutput;
                view.LightColor = Color.FromRgb(o.Red, o.Green, o.Blue); view.PlayerLeds = o.PlayerLeds;
                view.TouchMode = session.Profile.Mapping.Touchpad.Mode;
                view.InvalidateVisual();
            });
            var activity = new StackPanel { Spacing = 5 };
            void FillActivity()
            {
                activity.Children.Clear();
                foreach (var a in rt.Activity.Entries.Take(9))
                    activity.Children.Add(Ui.Row(10, Ui.Text(a.Time.ToString("HH:mm:ss"), "muted"), new TextBlock { Text = a.Text, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 330 }));
            }
            FillActivity();
            int lastCount = -1;
            Live(() => { int c = rt.Activity.Entries.Count; var first = rt.Activity.Entries.FirstOrDefault(); int key = c * 31 + (first?.Time.Second ?? 0); if (key != lastCount) { lastCount = key; FillActivity(); } });

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*") };
            var left = Card("Vista interactiva", view, Ui.Text("Pulsa cualquier botón, gatillo, joystick o zona del touchpad para configurarlo.", "muted"));
            var right = Card("Actividad reciente", activity);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left); grid.Children.Add(right);
            root.Children.Add(grid);
        }
        return root;
    }

    private Control ControllerCard(ControllerSession s)
    {
        var rt = AppState.Runtime;
        var battery = Ui.Text("", "big");
        var batteryState = Ui.Text("", "muted");
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6, Width = 250, Margin = new Avalonia.Thickness(0, 6, 0, 8) };
        var health = Ui.Text("", "muted");
        var remaining = Ui.Text("", "muted");
        var triggers = Ui.Text("", "muted");
        var swatch = new Border { Width = 16, Height = 16, CornerRadius = new Avalonia.CornerRadius(8) };
        var profile = Ui.Text("", "muted");
        Live(() =>
        {
            var st = s.Device.State;
            battery.Text = st.BatteryStatus == BatteryStatus.Unknown ? "—" : st.BatteryPercent + " %";
            batteryState.Text = st.BatteryStatus switch
            {
                BatteryStatus.Charging => "Cargando", BatteryStatus.Full => "Carga completa", BatteryStatus.Discharging => "En batería",
                BatteryStatus.Error => "Error de carga", _ => "Estado desconocido",
            };
            if (s.Device.Battery.MinutesRemaining is { } min && min >= 1 && st.BatteryStatus is BatteryStatus.Discharging or BatteryStatus.Charging)
                remaining.Text = (st.BatteryStatus == BatteryStatus.Charging ? "Carga completa en ≈ " : "Autonomía restante ≈ ") + Ui.Duration(min);
            else remaining.Text = "";
            bar.Value = st.BatteryPercent;
            bar.Foreground = Ui.Brush(this, st.BatteryPercent <= 20 && st.BatteryStatus == BatteryStatus.Discharging ? "Bad" : "Good");
            health.Text = s.Device.Health switch
            {
                Core.Devices.DeviceHealth.Ok => $"Comunicación correcta · {s.Device.ReportRate:0} Hz",
                Core.Devices.DeviceHealth.Degraded => "Comunicación inestable",
                _ => "Sin respuesta",
            };
            var o = s.LastOutput;
            swatch.Background = new SolidColorBrush(Color.FromRgb(o.Red, o.Green, o.Blue));
            triggers.Text = $"L2: {TriggerName(s.Profile.L2)}   ·   R2: {TriggerName(s.Profile.R2)}";
            profile.Text = "Perfil: " + s.Profile.Name + (s.ProfileReason.StartsWith("game:") ? $" (juego: {s.ProfileReason[5..]})" : "");
        });
        var pill = new Border { Child = new TextBlock { Text = s.Device.Connection == ConnectionType.Usb ? "USB" : "Bluetooth", FontSize = 11, FontWeight = FontWeight.SemiBold } };
        pill.Classes.Add("pill");
        var header = new DockPanel();
        DockPanel.SetDock(pill, Dock.Right);
        header.Children.Add(pill);
        header.Children.Add(Stack(new TextBlock { Text = rt.DisplayName(s), FontSize = 16, FontWeight = FontWeight.SemiBold },
            Ui.Text(AppRuntime.ModelName(s.Device.Model) + (s.Device.IsSimulated ? " · SIMULADO" : ""), "muted")));
        var card = Card(null, header, new Border { Height = 8 }, Ui.Row(10, battery, batteryState), bar, remaining, profile, triggers,
            Ui.Row(8, swatch, Ui.Text("Iluminación: " + ModeName(s.Profile.Lighting.Mode), "muted")), health);
        card.Width = 290;
        card.PointerPressed += (_, _) => AppState.SelectController(s.Id);
        card.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        if (AppState.Session?.Id == s.Id) card.BorderBrush = Ui.Brush(this, "Accent");
        return card;
    }

    public static string TriggerName(TriggerEffect e) => e.Type == TriggerEffectType.Off ? "sin efecto" : e.Name.Length > 0 ? e.Name : e.Type.ToString();

    public static string ModeName(Core.Lighting.LightingMode m) => m switch
    {
        Core.Lighting.LightingMode.Off => "Apagada", Core.Lighting.LightingMode.Static => "Color estático", Core.Lighting.LightingMode.Breathing => "Respiración",
        Core.Lighting.LightingMode.Rainbow => "Arcoíris", Core.Lighting.LightingMode.ColorCycle => "Ciclo de colores", Core.Lighting.LightingMode.Pulse => "Pulso",
        Core.Lighting.LightingMode.AudioReactive => "Reactiva a la música", Core.Lighting.LightingMode.Battery => "Nivel de batería", _ => "Personalizada",
    };
}
