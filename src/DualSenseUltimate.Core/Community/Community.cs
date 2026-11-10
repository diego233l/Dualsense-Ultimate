using System.Net.Http.Headers;
using System.Text.Json;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.Core.Community;

public sealed class CommunityEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = PackageKinds.Profile;
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Game { get; set; } = "";
    public string Version { get; set; } = "1.0";
    /// <summary>Package URL, absolute or relative to the index.</summary>
    public string Url { get; set; } = "";
}

public sealed class CommunityIndex
{
    public int SchemaVersion { get; set; } = 1;
    public List<CommunityEntry> Entries { get; set; } = [];
}

/// <summary>Read-only client for the community library: a static JSON index plus package files hosted in
/// a Git repository. Contributions are made through pull requests, so no account system or server is needed.</summary>
public sealed class CommunityClient
{
    private readonly HttpClient _http;
    public CommunityClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0) _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DualSenseUltimate", "1.0"));
    }

    public async Task<CommunityIndex> FetchIndexAsync(string indexUrl, CancellationToken ct = default)
    {
        string json = await GetLimitedAsync(RequireHttps(indexUrl), ct);
        var index = JsonFile.Deserialize<CommunityIndex>(json) ?? new CommunityIndex();
        index.Entries = (index.Entries ?? []).Where(e => e != null && e.Url.Length > 0 && PackageKinds.All.Contains(e.Kind)).Take(2000).ToList();
        return index;
    }

    /// <summary>Downloads a package as text. The caller imports it through <see cref="ProfileStore.Import"/>, which validates it.</summary>
    public Task<string> DownloadAsync(string indexUrl, CommunityEntry entry, CancellationToken ct = default)
    {
        var uri = new Uri(new Uri(indexUrl), entry.Url);
        return GetLimitedAsync(RequireHttps(uri.ToString()), ct);
    }

    private static string RequireHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? url
            : throw new InvalidOperationException("La biblioteca comunitaria solo admite direcciones HTTPS.");

    private async Task<string> GetLimitedAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > ProfileValidator.MaxImportBytes) throw new InvalidOperationException("El archivo remoto es demasiado grande.");
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[8192]; int n;
        while ((n = await stream.ReadAsync(buf, ct)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > ProfileValidator.MaxImportBytes) throw new InvalidOperationException("El archivo remoto es demasiado grande.");
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}

/// <summary>A published release. InstallerUrl is null when the release carries no Windows installer.</summary>
public sealed record UpdateInfo(string Version, string Url, bool IsNewer, string Notes = "", string? InstallerUrl = null, long InstallerSize = 0, string? InstallerSha256 = null);

/// <summary>Update system built on GitHub Releases. Checking is optional; nothing is ever downloaded or
/// installed without the user asking for it. Downloads are restricted to the project's own release
/// assets over HTTPS and are verified (size and, when GitHub publishes it, SHA-256) before use.</summary>
public static class UpdateChecker
{
    public const long MaxInstallerBytes = 400L * 1024 * 1024;

    public static async Task<UpdateInfo?> CheckAsync(string repository, Version current, HttpClient? http = null, CancellationToken ct = default)
    {
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("DualSenseUltimate", current.ToString()));
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        return Parse(await resp.Content.ReadAsStringAsync(ct), repository, current);
    }

    public static UpdateInfo? Parse(string json, string repository, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;
        if (!Version.TryParse(Str(root, "tag_name").TrimStart('v', 'V'), out var latest)) return null;
        string notes = Str(root, "body");
        if (notes.Length > 6000) notes = notes[..6000] + "…";
        string? url = null, sha = null; long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                string name = Str(a, "name"), link = Str(a, "browser_download_url");
                if (!name.StartsWith("DualSenseUltimate-Setup", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsTrustedAssetUrl(link, repository)) continue;
                url = link; size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out long n) ? n : 0;
                string digest = Str(a, "digest");
                if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) sha = digest[7..];
                break;
            }
        }
        return new UpdateInfo(latest.ToString(), Str(root, "html_url"), Normalize(latest) > Normalize(current), notes, url, size, sha);
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>Only release assets of the configured repository are accepted.</summary>
    public static bool IsTrustedAssetUrl(string url, string repository) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && u.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase) && !u.AbsolutePath.Contains("..");

    /// <summary>Downloads and verifies the installer. Returns the local file path. Throws on any mismatch
    /// (the partial file is deleted).</summary>
    public static async Task<string> DownloadInstallerAsync(UpdateInfo info, string repository, string directory, IProgress<double>? progress = null, HttpClient? http = null, CancellationToken ct = default)
    {
        if (info.InstallerUrl == null || !IsTrustedAssetUrl(info.InstallerUrl, repository)) throw new InvalidOperationException("Esta versión no incluye un instalador descargable.");
        if (info.InstallerSize > MaxInstallerBytes) throw new InvalidOperationException("El instalador publicado es demasiado grande.");
        http ??= new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"DualSenseUltimate-Setup-{info.Version}.exe"), tmp = file + ".part";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, info.InstallerUrl);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("DualSenseUltimate", info.Version));
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long total = info.InstallerSize > 0 ? info.InstallerSize : resp.Content.Headers.ContentLength ?? 0, read = 0;
            using var sha = System.Security.Cryptography.SHA256.Create();
            await using (var source = await resp.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(tmp))
            {
                var buffer = new byte[81920]; int n;
                while ((n = await source.ReadAsync(buffer, ct)) > 0)
                {
                    read += n;
                    if (read > MaxInstallerBytes) throw new InvalidOperationException("La descarga supera el tamaño máximo permitido.");
                    await target.WriteAsync(buffer.AsMemory(0, n), ct);
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    if (total > 0) progress?.Report(Math.Min(1, (double)read / total));
                }
            }
            sha.TransformFinalBlock([], 0, 0);
            if (info.InstallerSize > 0 && read != info.InstallerSize) throw new InvalidOperationException("La descarga está incompleta o no coincide con el tamaño publicado.");
            string hash = Convert.ToHexString(sha.Hash!);
            if (info.InstallerSha256 != null && !hash.Equals(info.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La comprobación de integridad (SHA-256) ha fallado. La descarga se ha descartado.");
            File.Move(tmp, file, true);
            progress?.Report(1);
            return file;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }
}
