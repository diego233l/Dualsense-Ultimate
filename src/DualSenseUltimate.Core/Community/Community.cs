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

public sealed record UpdateInfo(string Version, string Url, bool IsNewer);

/// <summary>Optional update check against GitHub Releases. Never downloads or installs anything by itself.</summary>
public static class UpdateChecker
{
    public static async Task<UpdateInfo?> CheckAsync(string repository, Version current, HttpClient? http = null, CancellationToken ct = default)
    {
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("DualSenseUltimate", current.ToString()));
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        return Version.TryParse(tag.TrimStart('v', 'V'), out var latest) ? new UpdateInfo(latest.ToString(), url, latest > current) : null;
    }
}
