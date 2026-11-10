using DualSenseUltimate.Core.Community;
using DualSenseUltimate.Core.Devices;
using DualSenseUltimate.Core.Profiles;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Runtime;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.Tests;

public class CommunityCatalogTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DualSenseUltimate.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void Every_package_in_the_community_catalog_is_listed_and_imports_cleanly()
    {
        string community = Path.Combine(RepoRoot(), "community");
        var index = JsonFile.Deserialize<CommunityIndex>(File.ReadAllText(Path.Combine(community, "index.json")))!;
        Assert.NotEmpty(index.Entries);
        var files = Directory.GetFiles(Path.Combine(community, "packages"), "*.json").Select(Path.GetFileName).ToHashSet();
        Assert.Equal(files.Order(), index.Entries.Select(e => Path.GetFileName(e.Url)).Order());
        using var tmp = new TempDir();
        var store = new ProfileStore(tmp.Paths);
        foreach (var e in index.Entries)
        {
            Assert.Contains(e.Kind, PackageKinds.All);
            var (result, summary) = store.Import(File.ReadAllText(Path.Combine(community, e.Url)));
            Assert.True(result.Ok, $"{e.Url}: {string.Join("; ", result.Errors)}");
            Assert.True(result.Warnings.Count == 0, $"{e.Url}: {string.Join("; ", result.Warnings)}");
            Assert.NotNull(summary);
        }
        Assert.Equal(index.Entries.Count(e => e.Kind == PackageKinds.Profile) + 1, store.Profiles.Count);
    }

    [Fact]
    public async Task Community_client_rejects_non_https_sources()
    {
        var client = new CommunityClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchIndexAsync("http://example.com/index.json"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.FetchIndexAsync("file:///etc/passwd"));
    }
}

public class PluginTests
{
    private static string? FindSamplePlugin(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DualSenseUltimate.slnx"))) dir = dir.Parent;
        if (dir == null) return null;
        string bin = Path.Combine(dir.FullName, "samples", "plugins", name, "bin");
        return Directory.Exists(bin) ? Directory.GetFiles(bin, name + ".dll", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
    }

    private static void Install(string dll, AppPaths paths, string? manifestOverride = null)
    {
        string target = Path.Combine(paths.Plugins, Path.GetFileNameWithoutExtension(dll));
        Directory.CreateDirectory(target);
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(dll)!)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
        if (manifestOverride != null) File.WriteAllText(Path.Combine(target, "plugin.json"), manifestOverride);
    }

    [Fact]
    public void Sample_plugin_loads_only_when_enabled_receives_events_and_unloads()
    {
        string? dll = FindSamplePlugin("BatteryLoggerPlugin");
        Assert.True(dll != null, "build the solution first (the sample plugin is part of it)");
        using var tmp = new TempDir();
        Install(dll!, tmp.Paths);
        using var rt = new AppRuntime(tmp.Paths, new PlatformServices());
        rt.Start();
        var entry = Assert.Single(rt.Plugins.Entries);
        Assert.False(entry.Loaded);                                  // never runs without explicit consent
        rt.Plugins.SetEnabled(entry, true);
        Assert.True(entry.Loaded, entry.Error);

        var sim = new SimulatedController("p", ConnectionType.Usb, mac: "AA:00:00:00:00:10", autoReportHz: 100);
        sim.Input.BatteryPercent = 5;
        rt.Simulator.Plug(sim); rt.Devices.Scan();
        string csv = Path.Combine(tmp.Paths.Plugins, ".data", "samples.battery-logger", "battery.csv");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 5000 && !(File.Exists(csv) && sim.LastOutput is { Red: 255, Green: 0 })) Thread.Sleep(20);
        Assert.True(File.Exists(csv), "plugin wrote its log");
        Assert.Contains("AA:00:00:00:00:10", File.ReadAllText(csv));
        Assert.True(sim.LastOutput is { Red: 255, Green: 0 }, "plugin flashed the light bar red through the host API");

        rt.Plugins.SetEnabled(entry, false);
        Assert.False(entry.Loaded);
        Assert.DoesNotContain("samples.battery-logger", rt.Settings.EnabledPlugins);
    }

    [Fact]
    public void Plugin_calling_an_api_without_declaring_the_permission_is_refused()
    {
        string? dll = FindSamplePlugin("BatteryLoggerPlugin");
        Assert.True(dll != null, "build the solution first");
        using var tmp = new TempDir();
        // Same plugin, but its manifest no longer declares ReadState: Start() touches host.Controllers and must be refused.
        Install(dll!, tmp.Paths, """{ "id":"samples.battery-logger", "name":"x", "assembly":"BatteryLoggerPlugin.dll", "permissions":["Events"] }""");
        using var rt = new AppRuntime(tmp.Paths, new PlatformServices());
        rt.Start();
        var entry = Assert.Single(rt.Plugins.Entries);
        rt.Plugins.SetEnabled(entry, true);
        Assert.False(entry.Loaded);
        Assert.Contains("ReadState", entry.Error);
    }

    [Theory]
    [InlineData("""{ "id":"../evil", "name":"x", "assembly":"a.dll" }""")]
    [InlineData("""{ "id":"ok", "name":"x", "assembly":"..\\..\\outside.dll" }""")]
    [InlineData("""{ "id":"ok", "name":"x", "assembly":"/abs/path.dll" }""")]
    [InlineData("""not json""")]
    public void Malformed_or_escaping_manifests_are_ignored(string manifest)
    {
        using var tmp = new TempDir();
        string dir = Path.Combine(tmp.Paths.Plugins, "bad");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), manifest);
        using var rt = new AppRuntime(tmp.Paths, new PlatformServices());
        rt.Plugins.Discover();
        Assert.Empty(rt.Plugins.Entries);
    }
}
