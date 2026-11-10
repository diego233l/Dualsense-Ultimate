using System.Net;
using System.Security.Cryptography;
using DualSenseUltimate.Core.Community;
using DualSenseUltimate.Core.Devices;
using DualSenseUltimate.Core.Protocol;

namespace DualSenseUltimate.Tests;

public class BatteryEstimatorTests
{
    private const BatteryStatus D = BatteryStatus.Discharging, C = BatteryStatus.Charging;

    [Fact]
    public void First_reading_is_the_middle_of_the_step()
    {
        Assert.Equal(75, new BatteryEstimator().Update(7, D, 0));
        Assert.Equal(45, new BatteryEstimator().Update(4, C, 0));
        Assert.Equal(100, new BatteryEstimator().Update(10, D, 0));
        Assert.Equal(100, new BatteryEstimator().Update(3, BatteryStatus.Full, 0));
        Assert.Equal(5, new BatteryEstimator().Update(0, D, 0));
    }

    [Fact]
    public void Jitter_between_neighbouring_steps_does_not_move_the_value()
    {
        var e = new BatteryEstimator();
        int first = e.Update(6, D, 0);
        for (int t = 1; t < 120; t++) // raw value flickers 6/5/7 every second (rumble sag, noise)
            Assert.InRange(e.Update(t % 3 == 0 ? 5 : t % 7 == 0 ? 7 : 6, D, t), first - 1, first);
    }

    [Fact]
    public void A_real_drop_is_accepted_after_a_few_seconds_and_never_goes_back_up()
    {
        var e = new BatteryEstimator();
        e.Update(6, D, 0);
        Assert.Equal(65, e.Update(5, D, 1));       // not yet trusted
        int after = e.Update(5, D, 6);             // stable for > 4 s: step 5 entered from above
        Assert.Equal(59, after);
        Assert.True(e.Calibrated);
        int later = e.Update(6, D, 30);            // brief bounce back up is ignored
        Assert.InRange(later, 55, 59);
        Assert.True(e.Update(6, D, 200) > 59);     // ...unless it persists for a minute (genuine correction)
    }

    [Fact]
    public void Value_declines_smoothly_inside_a_step_and_is_monotonic()
    {
        var e = new BatteryEstimator { Rates = new BatteryRates { DischargeMinutesPerLevel = 40 } };
        e.Update(8, D, 0); e.Update(7, D, 1); e.Update(7, D, 6);
        int prev = 100; var seen = new HashSet<int>();
        for (double t = 6; t < 6 + 40 * 60; t += 30)
        {
            int v = e.Update(7, D, t);
            Assert.True(v <= prev); prev = v; seen.Add(v);
        }
        Assert.Equal(70, prev);
        Assert.True(seen.Count >= 9, "about 1 % resolution inside the step");
    }

    [Fact]
    public void Charging_rises_and_never_drops()
    {
        var e = new BatteryEstimator();
        int prev = e.Update(3, C, 0);
        for (double t = 1; t < 3000; t += 20)
        {
            int raw = t > 1500 ? 4 : (t % 100 < 20 ? 2 : 3);
            int v = e.Update(raw, C, t);
            Assert.True(v >= prev, $"dropped from {prev} to {v} at {t}");
            prev = v;
        }
        Assert.InRange(prev, 40, 49);
        Assert.True(prev < 100);
    }

    [Fact]
    public void Rates_are_learned_from_observed_transitions()
    {
        var rates = new BatteryRates { DischargeMinutesPerLevel = 45 };
        var e = new BatteryEstimator { Rates = rates };
        double t = 0;
        e.Update(9, D, t);
        for (int level = 8; level >= 4; level--)
        {
            t += 30 * 60;                              // this controller really loses a step every 30 minutes
            e.Update(level, D, t); e.Update(level, D, t + 5);
        }
        Assert.True(rates.Samples >= 3);
        Assert.InRange(rates.DischargeMinutesPerLevel, 29, 36);
        Assert.InRange(e.MinutesRemaining!.Value, 100, 180); // ~4.9 steps * ~31 min
    }

    [Fact]
    public void Plugging_the_charger_keeps_the_value_continuous()
    {
        var e = new BatteryEstimator();
        e.Update(5, D, 0); e.Update(4, D, 1); int before = e.Update(4, D, 6); // 49
        int after = e.Update(4, C, 7);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Device_reports_estimated_percentage()
    {
        var sim = new SimulatedController("b", ConnectionType.Usb);
        sim.Input.BatteryPercent = 85; sim.Input.BatteryStatus = D;
        using var dev = new ControllerDevice(sim.Descriptor, sim);
        dev.Start(); sim.Push();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (dev.ReportCount == 0 && sw.ElapsedMilliseconds < 3000) Thread.Sleep(10);
        Assert.Equal(85, dev.State.BatteryPercent);
        Assert.Equal(8, dev.State.BatteryLevelRaw);
    }
}

public class UpdateTests
{
    private const string Repo = "diego233l/Dualsense-Ultimate";

    private static string Release(string tag, string asset = "DualSenseUltimate-Setup-1.2.0.exe", string? url = null, long size = 5, string? digest = null, bool prerelease = false) => $$"""
        { "tag_name": "{{tag}}", "html_url": "https://github.com/{{Repo}}/releases/tag/{{tag}}", "body": "Notas", "draft": false, "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [ { "name": "other.zip", "browser_download_url": "https://github.com/{{Repo}}/releases/download/{{tag}}/other.zip", "size": 1 },
                      { "name": "{{asset}}", "browser_download_url": "{{url ?? $"https://github.com/{Repo}/releases/download/{tag}/{asset}"}}", "size": {{size}}{{(digest == null ? "" : $", \"digest\": \"sha256:{digest}\"")}} } ] }
        """;

    [Fact]
    public void Newer_release_is_detected_with_its_installer()
    {
        var info = UpdateChecker.Parse(Release("v1.2.0", digest: "ABCD"), Repo, new Version(1, 0, 0))!;
        Assert.True(info.IsNewer); Assert.Equal("1.2.0", info.Version); Assert.Equal("Notas", info.Notes);
        Assert.EndsWith("DualSenseUltimate-Setup-1.2.0.exe", info.InstallerUrl); Assert.Equal("ABCD", info.InstallerSha256); Assert.Equal(5, info.InstallerSize);
    }

    [Theory]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v1.0.0", "1.0.0.0", false)]
    [InlineData("v0.9.0", "1.0.0", false)]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("v2.0", "1.9.9", true)]
    public void Version_comparison(string tag, string current, bool newer) =>
        Assert.Equal(newer, UpdateChecker.Parse(Release(tag), Repo, Version.Parse(current))!.IsNewer);

    [Fact]
    public void Prereleases_and_unparseable_tags_are_ignored()
    {
        Assert.Null(UpdateChecker.Parse(Release("v9.0.0", prerelease: true), Repo, new Version(1, 0, 0)));
        Assert.Null(UpdateChecker.Parse(Release("nightly"), Repo, new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("http://github.com/diego233l/Dualsense-Ultimate/releases/download/v2/DualSenseUltimate-Setup.exe")]
    [InlineData("https://evil.example/diego233l/Dualsense-Ultimate/releases/download/v2/DualSenseUltimate-Setup.exe")]
    [InlineData("https://github.com/someone-else/fork/releases/download/v2/DualSenseUltimate-Setup.exe")]
    [InlineData("https://github.com/diego233l/Dualsense-Ultimate/releases/download/../../../x/DualSenseUltimate-Setup.exe")]
    public void Installers_from_untrusted_locations_are_not_offered(string url)
    {
        var info = UpdateChecker.Parse(Release("v2.0.0", url: url), Repo, new Version(1, 0, 0))!;
        Assert.True(info.IsNewer);
        Assert.Null(info.InstallerUrl);
    }

    private sealed class FakeHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }

    [Fact]
    public async Task Download_is_verified_by_size_and_hash()
    {
        using var tmp = new TempDir();
        byte[] payload = [1, 2, 3, 4, 5];
        string sha = Convert.ToHexString(SHA256.HashData(payload));
        string url = $"https://github.com/{Repo}/releases/download/v1.2.0/DualSenseUltimate-Setup-1.2.0.exe";
        using var http = new HttpClient(new FakeHandler(payload));
        var progress = new List<double>();

        var good = new UpdateInfo("1.2.0", "", true, "", url, 5, sha.ToLowerInvariant());
        string file = await UpdateChecker.DownloadInstallerAsync(good, Repo, tmp.Path, new SyncProgress(progress.Add), http);
        Assert.Equal(payload, File.ReadAllBytes(file));
        Assert.Equal(1, progress[^1]);

        var badHash = good with { InstallerSha256 = new string('0', 64), Version = "1.2.1" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateChecker.DownloadInstallerAsync(badHash, Repo, tmp.Path, null, http));
        var badSize = good with { InstallerSize = 999, Version = "1.2.2" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateChecker.DownloadInstallerAsync(badSize, Repo, tmp.Path, null, http));
        var badUrl = good with { InstallerUrl = "https://evil.example/x.exe" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateChecker.DownloadInstallerAsync(badUrl, Repo, tmp.Path, null, http));
        Assert.Single(Directory.GetFiles(tmp.Path, "DualSenseUltimate-Setup-*")); // rejected downloads leave nothing behind
    }

    private sealed class SyncProgress(Action<double> a) : IProgress<double> { public void Report(double v) => a(v); }
}
