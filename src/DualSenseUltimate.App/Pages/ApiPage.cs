using Avalonia.Controls;
using DualSenseUltimate.App.Controls;

namespace DualSenseUltimate.App.Pages;

public sealed class ApiPage : PageBase
{
    public override string Title => "API local";
    public override string Subtitle => "Controla tus mandos desde otros programas, scripts o Home Assistant";
    protected override bool RebuildOnControllers => false;
    private bool _showToken;

    protected override Control Build()
    {
        var rt = AppState.Runtime;
        var s = rt.Settings;
        var api = AppState.Api;
        var root = new StackPanel();
        var status = Ui.Text("", "muted");
        Live(() => status.Text = api.Running ? $"En ejecución en http://127.0.0.1:{api.Port}" : s.ApiEnabled ? "No se pudo iniciar: " + (api.Error ?? "iniciando…") : "Desactivada");
        var port = new NumericUpDown { Value = s.ApiPort, Minimum = 1024, Maximum = 65535, Increment = 1, Width = 150, FormatString = "0" };
        async Task Restart() { await api.StopAsync(); if (s.ApiEnabled) await api.StartAsync(s.ApiPort); }
        port.LostFocus += async (_, _) => { int v = (int)(port.Value ?? 26760); if (v != s.ApiPort) { s.ApiPort = v; rt.SaveSettings(); await Restart(); Rebuild(); } };
        var token = new TextBox { Text = _showToken ? s.ApiToken : new string('•', 32), IsReadOnly = true, Width = 330, FontFamily = "Consolas,monospace" };
        root.Children.Add(Card("Servidor",
            Toggle("API activada", s.ApiEnabled, async v => { s.ApiEnabled = v; rt.SaveSettings(); await Restart(); }),
            status,
            Wrap(Labeled("Puerto", port), new Border { Width = 14 }, Labeled("Token de acceso", token)),
            new Border { Height = 6 },
            Ui.Row(8, Ui.Btn(_showToken ? "Ocultar" : "Mostrar", () => { _showToken = !_showToken; Rebuild(); }), Ui.Btn("Copiar token", async () => await FileIo.CopyAsync(this, s.ApiToken)),
                Ui.Btn("Generar token nuevo", () => { s.ApiToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(); rt.SaveSettings(); Rebuild(); })),
            Ui.Text("Solo escucha en este equipo (127.0.0.1) y exige el token en cada petición (cabecera «Authorization: Bearer …»), por lo que ni otros equipos de la red ni páginas web pueden usarla.", "muted")));

        string t = _showToken ? s.ApiToken : "TU_TOKEN";
        string baseUrl = $"http://127.0.0.1:{s.ApiPort}/api/v1";
        Control Code(string text) => new Border
        {
            Background = Ui.Brush(this, "CardBg2"), CornerRadius = new Avalonia.CornerRadius(8), Padding = new Avalonia.Thickness(12), Margin = new Avalonia.Thickness(0, 4, 0, 10),
            Child = new SelectableTextBlock { Text = text, FontFamily = "Consolas,Cascadia Mono,monospace", FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
        };
        root.Children.Add(Card("Ejemplos",
            Ui.Text("curl", "label"), Code($"curl -H \"Authorization: Bearer {t}\" {baseUrl}/controllers\ncurl -X POST -H \"Authorization: Bearer {t}\" -H \"Content-Type: application/json\" \\\n     -d \"{{\\\"color\\\":\\\"#FF0000\\\"}}\" {baseUrl}/controllers/first/lightbar"),
            Ui.Text("Python", "label"), Code($"import requests\nH = {{\"Authorization\": \"Bearer {t}\"}}\nB = \"{baseUrl}\"\nprint(requests.get(f\"{{B}}/controllers/first/battery\", headers=H).json())\nrequests.post(f\"{{B}}/controllers/first/triggers\", headers=H, json={{\"side\": \"R2\", \"preset\": \"Pistola\"}})"),
            Ui.Text("JavaScript (Node 18+)", "label"), Code($"const H = {{ Authorization: \"Bearer {t}\", \"Content-Type\": \"application/json\" }};\nawait fetch(\"{baseUrl}/controllers/first/haptics\", {{ method: \"POST\", headers: H, body: JSON.stringify({{ pattern: \"Latido\" }}) }});"),
            Ui.Text("Más ejemplos completos en la carpeta examples/ del proyecto y referencia en docs/API.md.", "muted")));

        (string, string)[] endpoints =
        [
            ("GET  /ping", "Comprobación sin token"), ("GET  /controllers", "Lista de mandos con batería, conexión y perfil"),
            ("GET  /controllers/{id}", "Estado de un mando ({id} = MAC, «first» o índice)"), ("GET  /controllers/{id}/battery", "Porcentaje y estado de carga"),
            ("GET  /controllers/{id}/input", "Botones, sticks, gatillos, giroscopio y touchpad"), ("POST /controllers/{id}/lightbar", "{ color | r,g,b, durationMs }"),
            ("POST /controllers/{id}/rumble", "{ left, right, durationMs }"), ("POST /controllers/{id}/triggers", "{ side: L2|R2|both, preset | effect }"),
            ("POST /controllers/{id}/haptics", "{ pattern | custom }"), ("POST /controllers/{id}/profile", "{ profile: nombre o id }"),
            ("POST /controllers/{id}/reset", "Detiene efectos y quita ajustes temporales"), ("GET  /profiles", "Perfiles disponibles"),
            ("GET  /trigger-presets  ·  /haptic-patterns", "Nombres disponibles"), ("POST /notify", "{ title } → evento de notificación"),
            ("GET  /events", "Flujo de eventos en tiempo real (Server-Sent Events)"),
        ];
        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("330,*") };
        for (int i = 0; i < endpoints.Length; i++)
        {
            table.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var a = new TextBlock { Text = endpoints[i].Item1, FontFamily = "Consolas,Cascadia Mono,monospace", FontSize = 12, Margin = new Avalonia.Thickness(0, 3) };
            var b = Ui.Text(endpoints[i].Item2, "muted");
            Grid.SetRow(a, i); Grid.SetRow(b, i); Grid.SetColumn(b, 1);
            table.Children.Add(a); table.Children.Add(b);
        }
        root.Children.Add(Card("Referencia rápida (prefijo /api/v1)", table));
        return root;
    }
}
