using System.Net.Http.Json;
using System.Text.Json;
using DualSenseUltimate.Plugins;

namespace HomeAssistantPlugin;

/// <summary>Forwards application events to a Home Assistant webhook
/// (https://www.home-assistant.io/docs/automation/trigger/#webhook-trigger).
/// Create config.json in the plugin data folder: { "webhookUrl": "http://homeassistant.local:8123/api/webhook/your-id" }</summary>
public sealed class HomeAssistant : IDsuPlugin
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private IDisposable? _subscription;
    private string? _url;
    private IDsuHost? _host;

    public void Start(IDsuHost host)
    {
        _host = host;
        string config = Path.Combine(host.DataDirectory, "config.json");
        if (!File.Exists(config))
        {
            File.WriteAllText(config, "{\n  \"webhookUrl\": \"\"\n}\n");
            host.Log("created " + config + " - set webhookUrl and re-enable the plugin");
            return;
        }
        try { _url = JsonDocument.Parse(File.ReadAllText(config)).RootElement.GetProperty("webhookUrl").GetString(); }
        catch (Exception ex) { host.Log("invalid config.json: " + ex.Message); return; }
        if (string.IsNullOrWhiteSpace(_url) || !Uri.TryCreate(_url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        { host.Log("webhookUrl is empty or not an http(s) URL"); _url = null; return; }
        _subscription = host.Subscribe(OnEvent);
        host.Log("forwarding events to Home Assistant");
    }

    private async void OnEvent(DsuEvent e)
    {
        if (_url == null) return;
        try { await _http.PostAsJsonAsync(_url, new { type = e.Type, controller = e.ControllerId, detail = e.Detail, time = e.Time }); }
        catch (Exception ex) { _host?.Log("webhook failed: " + ex.Message); }
    }

    public void Stop() { _subscription?.Dispose(); _http.Dispose(); }
}
