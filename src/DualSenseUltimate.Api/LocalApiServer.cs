using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Haptics;
using DualSenseUltimate.Core.Lighting;
using DualSenseUltimate.Core.Plugins;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Runtime;
using DualSenseUltimate.Core.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DualSenseUltimate.Api;

/// <summary>Local REST API. Listens on 127.0.0.1 only and requires a bearer token, so neither other
/// machines nor web pages opened in a browser can drive the controller.</summary>
public sealed class LocalApiServer : IAsyncDisposable
{
    private readonly AppRuntime _rt;
    private WebApplication? _app;
    private readonly ConcurrentDictionary<Guid, Channel<string>> _streams = new();
    private IDisposable? _subscription;
    public int Port { get; private set; }
    public bool Running => _app != null;
    public string? Error { get; private set; }

    public LocalApiServer(AppRuntime runtime) => _rt = runtime;

    public record LightbarRequest(string? Color, int? R, int? G, int? B, int DurationMs = 0);
    public record RumbleRequest(int Left, int Right, int DurationMs = 500);
    public record TriggerRequest(string Side = "both", string? Preset = null, TriggerEffect? Effect = null);
    public record HapticRequest(string? Pattern = null, HapticPattern? Custom = null);
    public record ProfileRequest(string Profile);
    public record NotifyRequest(string Title);

    public async Task StartAsync(int port)
    {
        if (_app != null) return;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(o => { o.ListenLocalhost(port); o.Limits.MaxRequestBodySize = 256 * 1024; });
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.SerializerOptions.PropertyNameCaseInsensitive = true;
                o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                o.SerializerOptions.TypeInfoResolverChain.Add(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
            });
            var app = builder.Build();

            app.Use(async (ctx, next) =>
            {
                // No CORS headers are ever sent and a token is mandatory: browsers cannot call this API cross-site.
                if (ctx.Request.Path == "/api/v1/ping") { await next(); return; }
                string header = ctx.Request.Headers.Authorization.ToString();
                string token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : ctx.Request.Query["token"].ToString();
                byte[] a = Encoding.UTF8.GetBytes(token), b = Encoding.UTF8.GetBytes(_rt.Settings.ApiToken);
                if (a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b))
                {
                    ctx.Response.StatusCode = 401;
                    await ctx.Response.WriteAsJsonAsync(new { error = "missing or invalid token" });
                    return;
                }
                await next();
            });

            var api = app.MapGroup("/api/v1");
            api.MapGet("/ping", () => Results.Ok(new { name = "DualSense Ultimate", api = 1 }));
            api.MapGet("/controllers", () => _rt.Sessions.Select(Describe));
            api.MapGet("/controllers/{id}", (string id) => Find(id) is { } s ? Results.Ok(Describe(s)) : NotFound());
            api.MapGet("/controllers/{id}/battery", (string id) => Find(id) is { } s
                ? Results.Ok(new { percent = s.Device.State.BatteryPercent, status = s.Device.State.BatteryStatus.ToString() }) : NotFound());
            api.MapGet("/controllers/{id}/input", (string id) => Find(id) is { } s ? Results.Ok(DescribeInput(s.Device.State)) : NotFound());

            api.MapPost("/controllers/{id}/lightbar", (string id, LightbarRequest req) =>
            {
                if (Find(id) is not { } s) return NotFound();
                Rgb c = req.Color != null ? Rgb.Parse(req.Color) : new Rgb(Byte(req.R), Byte(req.G), Byte(req.B));
                s.SetLightbarOverride(c, Math.Clamp(req.DurationMs, 0, 3_600_000));
                return Ok();
            });
            api.MapPost("/controllers/{id}/rumble", (string id, RumbleRequest req) =>
            {
                if (Find(id) is not { } s) return NotFound();
                s.Rumble(Byte(req.Left), Byte(req.Right), req.DurationMs);
                return Ok();
            });
            api.MapPost("/controllers/{id}/triggers", (string id, TriggerRequest req) =>
            {
                if (Find(id) is not { } s) return NotFound();
                var effect = req.Effect != null ? ProfileValidator.Sanitize(req.Effect)
                    : _rt.Profiles.AllTriggerEffects.FirstOrDefault(e => e.Name.Equals(req.Preset ?? "", StringComparison.OrdinalIgnoreCase));
                if (effect == null) return Results.BadRequest(new { error = "unknown preset; see GET /api/v1/trigger-presets" });
                string side = (req.Side ?? "both").ToLowerInvariant();
                if (side is not ("l2" or "r2" or "both")) return Results.BadRequest(new { error = "side must be L2, R2 or both" });
                s.SetTriggerOverride(side != "r2", side != "l2", effect);
                return Ok();
            });
            api.MapPost("/controllers/{id}/haptics", (string id, HapticRequest req) =>
            {
                if (Find(id) is not { } s) return NotFound();
                var pattern = req.Custom != null ? ProfileValidator.Sanitize(req.Custom)
                    : _rt.Profiles.AllHapticPatterns.FirstOrDefault(p => p.Name.Equals(req.Pattern ?? "", StringComparison.OrdinalIgnoreCase));
                if (pattern == null || pattern.Steps.Count == 0) return Results.BadRequest(new { error = "unknown pattern; see GET /api/v1/haptic-patterns" });
                s.PlayHaptic(pattern);
                return Ok();
            });
            api.MapPost("/controllers/{id}/profile", (string id, ProfileRequest req) =>
                Find(id) == null ? NotFound() : _rt.ApplyProfileByName(Find(id)!.Id, req.Profile) ? Ok() : Results.BadRequest(new { error = "unknown profile" }));
            api.MapPost("/controllers/{id}/reset", (string id) =>
            {
                if (Find(id) is not { } s) return NotFound();
                s.StopEffects();
                return Ok();
            });

            api.MapGet("/profiles", () => _rt.Profiles.Profiles.Select(p => new { p.Id, p.Name, p.Description, p.Processes, emulation = p.Emulation.Mode.ToString() }));
            api.MapGet("/trigger-presets", () => _rt.Profiles.AllTriggerEffects.Select(e => new { e.Name, type = e.Type.ToString() }));
            api.MapGet("/haptic-patterns", () => _rt.Profiles.AllHapticPatterns.Select(p => new { p.Name, durationMs = p.TotalMs, p.Repeat }));
            api.MapPost("/notify", (NotifyRequest req) => { _rt.Notify(req.Title ?? ""); return Ok(); });

            // Server-sent events: one JSON object per event.
            api.MapGet("/events", async (HttpContext ctx) =>
            {
                ctx.Response.Headers.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache";
                var id = Guid.NewGuid();
                var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest });
                _streams[id] = channel;
                try
                {
                    await ctx.Response.WriteAsync(": connected\n\n", ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                    await foreach (var line in channel.Reader.ReadAllAsync(ctx.RequestAborted))
                    {
                        await ctx.Response.WriteAsync($"data: {line}\n\n", ctx.RequestAborted);
                        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                    }
                }
                catch (OperationCanceledException) { }
                finally { _streams.TryRemove(id, out _); }
            });

            _subscription = _rt.Events.Subscribe(e =>
            {
                if (_streams.IsEmpty) return;
                var d = EventMapper.ToDsu(e);
                string json = JsonSerializer.Serialize(new { type = d.Type, controllerId = d.ControllerId, detail = d.Detail, time = d.Time });
                foreach (var ch in _streams.Values) ch.Writer.TryWrite(json);
            });

            await app.StartAsync();
            _app = app; Port = port; Error = null;
            Log.Info($"local API listening on http://127.0.0.1:{port}");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Error("local API failed to start", ex);
        }
    }

    private static byte Byte(int? v) => (byte)Math.Clamp(v ?? 0, 0, 255);
    private static IResult Ok() => Results.Ok(new { ok = true });
    private static IResult NotFound() => Results.NotFound(new { error = "controller not found" });

    /// <summary>Accepts a controller id, or "first"/"0", "1"... as an index for convenience.</summary>
    private ControllerSession? Find(string id)
    {
        var sessions = _rt.Sessions;
        if (id.Equals("first", StringComparison.OrdinalIgnoreCase)) return sessions.FirstOrDefault();
        if (int.TryParse(id, out int index)) return index >= 0 && index < sessions.Count ? sessions[index] : null;
        return sessions.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private object Describe(ControllerSession s) => new
    {
        id = s.Id,
        name = _rt.DisplayName(s),
        model = s.Device.Model.ToString(),
        connection = s.Device.Connection.ToString(),
        simulated = s.Device.IsSimulated,
        firmware = s.Device.FirmwareVersion,
        health = s.Device.Health.ToString(),
        reportRateHz = Math.Round(s.Device.ReportRate),
        battery = new { percent = s.Device.State.BatteryPercent, status = s.Device.State.BatteryStatus.ToString() },
        profile = new { id = s.Profile.Id, name = s.Profile.Name, reason = s.ProfileReason },
        emulation = s.EmulationStatus,
        lightbar = new Rgb(s.LastOutput.Red, s.LastOutput.Green, s.LastOutput.Blue).ToHex(),
    };

    private static object DescribeInput(InputState s) => new
    {
        buttons = Enum.GetValues<PadButtons>().Where(b => b != PadButtons.None && s.IsDown(b)).Select(b => b.ToString()),
        leftStick = new { x = s.LeftX, y = s.LeftY }, rightStick = new { x = s.RightX, y = s.RightY },
        l2 = s.L2, r2 = s.R2,
        gyro = new { x = s.GyroX, y = s.GyroY, z = s.GyroZ }, accel = new { x = s.AccelX, y = s.AccelY, z = s.AccelZ },
        touch = new[] { s.Touch0, s.Touch1 }.Where(t => t.Active).Select(t => new { t.Id, t.X, t.Y }),
    };

    public async Task StopAsync()
    {
        _subscription?.Dispose(); _subscription = null;
        foreach (var ch in _streams.Values) ch.Writer.TryComplete();
        if (_app != null)
        {
            try { using var cts = new CancellationTokenSource(2000); await _app.StopAsync(cts.Token); await _app.DisposeAsync(); } catch { }
            _app = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
