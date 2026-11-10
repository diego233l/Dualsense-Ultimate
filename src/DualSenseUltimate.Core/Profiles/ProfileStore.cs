using DualSenseUltimate.Core.Diagnostics;
using DualSenseUltimate.Core.Haptics;
using DualSenseUltimate.Core.Lighting;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Storage;

namespace DualSenseUltimate.Core.Profiles;

/// <summary>Profiles on disk (one JSON file each) plus the local library of presets.</summary>
public sealed class ProfileStore
{
    private readonly AppPaths _paths;
    private readonly List<Profile> _profiles = new();
    private readonly object _lock = new();
    public Library Library { get; private set; } = new();
    public event Action? Changed;

    public ProfileStore(AppPaths paths) { _paths = paths; Load(); }

    public IReadOnlyList<Profile> Profiles { get { lock (_lock) return _profiles.ToList(); } }
    public Profile? Get(string? id) { lock (_lock) return _profiles.FirstOrDefault(p => p.Id == id); }
    public Profile? GetByName(string name) { lock (_lock) return _profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }

    public void Load()
    {
        lock (_lock)
        {
            _profiles.Clear();
            foreach (var file in Directory.GetFiles(_paths.Profiles, "*.json"))
            {
                try
                {
                    var p = JsonFile.Deserialize<Profile>(File.ReadAllText(file));
                    if (p == null) continue;
                    ProfileValidator.Sanitize(p);
                    if (_profiles.All(x => x.Id != p.Id)) _profiles.Add(p);
                }
                catch (Exception ex) { Log.Warn($"skipping unreadable profile {Path.GetFileName(file)}: {ex.Message}"); }
            }
            if (_profiles.Count == 0)
            {
                var def = new Profile { Name = "Predeterminado", Description = "Perfil global: sin remapeo, el juego ve el DualSense real." };
                _profiles.Add(def);
                JsonFile.Save(FileFor(def), def);
            }
            Library = JsonFile.Load<Library>(_paths.LibraryFile);
        }
    }

    private string FileFor(Profile p) => Path.Combine(_paths.Profiles, p.Id + ".json");

    public void Save(Profile p)
    {
        ProfileValidator.Sanitize(p);
        p.Modified = DateTime.UtcNow;
        lock (_lock)
        {
            int i = _profiles.FindIndex(x => x.Id == p.Id);
            if (i >= 0) _profiles[i] = p; else _profiles.Add(p);
            JsonFile.Save(FileFor(p), p);
        }
        Changed?.Invoke();
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            if (_profiles.Count <= 1) return false; // always keep one profile
            var p = _profiles.FirstOrDefault(x => x.Id == id);
            if (p == null) return false;
            _profiles.Remove(p);
            foreach (var f in new[] { FileFor(p), FileFor(p) + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
        Changed?.Invoke();
        return true;
    }

    public void SaveLibrary() { lock (_lock) JsonFile.Save(_paths.LibraryFile, Library); Changed?.Invoke(); }

    public string Export(Profile p) => JsonFile.Serialize(SharePackage.Wrap(PackageKinds.Profile, p.Name, p, p.Author, p.Description));

    /// <summary>Imports any package kind. Profiles always get a fresh id so an import can never overwrite local data.</summary>
    public (ValidationResult Result, string? Summary) Import(string json)
    {
        var (pkg, r) = ProfileValidator.ParsePackage(json);
        if (pkg == null) return (r, null);
        switch (pkg.Kind)
        {
            case PackageKinds.Profile:
            {
                var p = ProfileValidator.Payload<Profile>(pkg, r);
                if (p == null) return (r, null);
                r.Warnings.AddRange(ProfileValidator.Sanitize(p).Warnings);
                p.Id = Guid.NewGuid().ToString("N");
                p.ControllerId = null;
                if (GetByName(p.Name) != null) p.Name += " (importado)";
                Save(p);
                return (r, $"Perfil «{p.Name}» importado");
            }
            case PackageKinds.Trigger:
            {
                var e = ProfileValidator.Payload<TriggerEffect>(pkg, r);
                if (e == null) return (r, null);
                e = ProfileValidator.Sanitize(e);
                if (e.Name.Length == 0) e.Name = pkg.Name;
                Library.TriggerEffects.Add(e); SaveLibrary();
                return (r, $"Efecto de gatillo «{e.Name}» añadido a la biblioteca");
            }
            case PackageKinds.Lighting:
            {
                var l = ProfileValidator.Payload<LightingConfig>(pkg, r);
                if (l == null) return (r, null);
                ProfileValidator.Sanitize(l);
                Library.LightingPresets.Add(new NamedLighting { Name = pkg.Name, Config = l }); SaveLibrary();
                return (r, $"Preset de iluminación «{pkg.Name}» añadido a la biblioteca");
            }
            case PackageKinds.Haptic:
            {
                var h = ProfileValidator.Payload<HapticPattern>(pkg, r);
                if (h == null) return (r, null);
                h = ProfileValidator.Sanitize(h);
                if (h.Name.Length == 0) h.Name = pkg.Name;
                Library.HapticPatterns.Add(h); SaveLibrary();
                return (r, $"Patrón háptico «{h.Name}» añadido a la biblioteca");
            }
            case PackageKinds.Macro:
            {
                var m = ProfileValidator.Payload<Macro>(pkg, r);
                if (m == null) return (r, null);
                m = ProfileValidator.Sanitize(m, r);
                if (m.Name.Length == 0) m.Name = pkg.Name;
                Library.Macros.Add(m); SaveLibrary();
                return (r, $"Macro «{m.Name}» añadida a la biblioteca");
            }
            case PackageKinds.Gyro:
            {
                var g = ProfileValidator.Payload<GyroConfig>(pkg, r);
                if (g == null) return (r, null);
                ProfileValidator.Sanitize(g);
                Library.GyroPresets.Add(new NamedGyro { Name = pkg.Name, Config = g }); SaveLibrary();
                return (r, $"Configuración de giroscopio «{pkg.Name}» añadida a la biblioteca");
            }
        }
        r.Errors.Add("Tipo de paquete no admitido.");
        return (r, null);
    }

    public IEnumerable<TriggerEffect> AllTriggerEffects => TriggerPresets.BuiltIn.Concat(Library.TriggerEffects);
    public IEnumerable<HapticPattern> AllHapticPatterns => HapticPattern.BuiltIn.Concat(Library.HapticPatterns);
}
