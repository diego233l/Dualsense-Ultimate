using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using DualSenseUltimate.Core.Diagnostics;

namespace DualSenseUltimate.Core.Storage;

public sealed class AppPaths
{
    public string Root { get; }
    public string Profiles => Path.Combine(Root, "profiles");
    public string Logs => Path.Combine(Root, "logs");
    public string Plugins => Path.Combine(Root, "plugins");
    public string Backups => Path.Combine(Root, "backups");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LibraryFile => Path.Combine(Root, "library.json");
    public string RulesFile => Path.Combine(Root, "automations.json");
    public string HiddenDevicesFile => Path.Combine(Root, "hidden-devices.json");

    public AppPaths(string? root = null)
    {
        Root = root ?? Environment.GetEnvironmentVariable("DSU_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DualSenseUltimate");
        foreach (var d in new[] { Root, Profiles, Logs, Plugins, Backups }) Directory.CreateDirectory(d);
    }
}

/// <summary>JSON persistence with atomic writes and automatic recovery from the previous good copy.</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Save<T>(string path, T value)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(tmp, path, true);
    }

    /// <summary>Loads the file; on corruption falls back to the .bak copy, then to a new instance.</summary>
    public static T Load<T>(string path) where T : new()
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var v = JsonSerializer.Deserialize<T>(File.ReadAllText(candidate), Options);
                if (v != null)
                {
                    if (candidate != path) Log.Warn($"{Path.GetFileName(path)} was unreadable; recovered from backup");
                    return v;
                }
            }
            catch (Exception ex) { Log.Warn($"cannot read {Path.GetFileName(candidate)}: {ex.Message}"); }
        }
        return new T();
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool ApiEnabled { get; set; } = true;
    public int ApiPort { get; set; } = 26760;
    public string ApiToken { get; set; } = "";
    public bool CheckUpdates { get; set; } = true;
    public bool SimulationEnabled { get; set; }
    public bool VerboseLogging { get; set; }
    /// <summary>Custom names per controller id.</summary>
    public Dictionary<string, string> ControllerNames { get; set; } = new();
    /// <summary>Manually assigned profile per controller id.</summary>
    public Dictionary<string, string> ControllerProfiles { get; set; } = new();
    public string? DefaultProfileId { get; set; }
    public string CommunityIndexUrl { get; set; } = "https://raw.githubusercontent.com/diego233l/Dualsense-Ultimate/main/community/index.json";
    public string UpdateRepository { get; set; } = "diego233l/Dualsense-Ultimate";
    /// <summary>Plugins the user explicitly enabled (by id).</summary>
    public List<string> EnabledPlugins { get; set; } = [];
}

/// <summary>Zipped snapshots of the configuration so a bad change can be rolled back.</summary>
public static class ConfigBackup
{
    public static string? Create(AppPaths paths, int keep = 10)
    {
        try
        {
            string file = Path.Combine(paths.Backups, $"config-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                foreach (var f in new[] { paths.SettingsFile, paths.LibraryFile, paths.RulesFile }.Where(File.Exists))
                    zip.CreateEntryFromFile(f, Path.GetFileName(f));
                foreach (var f in Directory.GetFiles(paths.Profiles, "*.json"))
                    zip.CreateEntryFromFile(f, "profiles/" + Path.GetFileName(f));
            }
            foreach (var old in Directory.GetFiles(paths.Backups, "config-*.zip").OrderByDescending(f => f).Skip(keep)) File.Delete(old);
            return file;
        }
        catch (Exception ex) { Log.Warn("backup failed: " + ex.Message); return null; }
    }

    public static IReadOnlyList<string> List(AppPaths paths) =>
        Directory.GetFiles(paths.Backups, "config-*.zip").OrderByDescending(f => f).ToList();

    /// <summary>Restores a snapshot over the current configuration (a new snapshot is taken first).</summary>
    public static bool Restore(AppPaths paths, string zipFile)
    {
        try
        {
            Create(paths);
            using var zip = ZipFile.OpenRead(zipFile);
            foreach (var e in zip.Entries)
            {
                // Only known, flat file names are accepted: no path traversal.
                string name = e.FullName.Replace('\\', '/');
                string? target = name is "settings.json" or "library.json" or "automations.json" ? Path.Combine(paths.Root, name)
                    : name.StartsWith("profiles/") && !name[9..].Contains('/') && name.EndsWith(".json") ? Path.Combine(paths.Profiles, Path.GetFileName(name)) : null;
                if (target != null) e.ExtractToFile(target, true);
            }
            return true;
        }
        catch (Exception ex) { Log.Error("restore failed", ex); return false; }
    }
}
