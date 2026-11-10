using System.Text.Json;
using DualSenseUltimate.Core.Haptics;
using DualSenseUltimate.Core.Lighting;
using DualSenseUltimate.Core.Mapping;
using DualSenseUltimate.Core.Protocol;
using DualSenseUltimate.Core.Storage;
using DualSenseUltimate.Core.Virtual;

namespace DualSenseUltimate.Core.Profiles;

public sealed class Profile
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Nuevo perfil";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public LightingConfig Lighting { get; set; } = new();
    public TriggerEffect L2 { get; set; } = TriggerEffect.OffEffect;
    public TriggerEffect R2 { get; set; } = TriggerEffect.OffEffect;
    public HapticsConfig Haptics { get; set; } = new();
    public MappingConfig Mapping { get; set; } = new();
    public EmulationConfig Emulation { get; set; } = new();
    /// <summary>Executable names (without path, case-insensitive, ".exe" optional) that activate this profile.</summary>
    public List<string> Processes { get; set; } = [];
    /// <summary>Restrict the profile to one controller id (null = any controller).</summary>
    public string? ControllerId { get; set; }
    public DateTime Modified { get; set; } = DateTime.UtcNow;

    public Profile Clone() => JsonFile.Deserialize<Profile>(JsonFile.Serialize(this))!;
}

/// <summary>Named reusable building blocks (local library).</summary>
public sealed class Library
{
    public List<TriggerEffect> TriggerEffects { get; set; } = [];
    public List<NamedLighting> LightingPresets { get; set; } = [];
    public List<HapticPattern> HapticPatterns { get; set; } = [];
    public List<Macro> Macros { get; set; } = [];
    public List<NamedGyro> GyroPresets { get; set; } = [];
}

public sealed class NamedLighting { public string Name { get; set; } = ""; public LightingConfig Config { get; set; } = new(); }
public sealed class NamedGyro { public string Name { get; set; } = ""; public GyroConfig Config { get; set; } = new(); }

public static class PackageKinds
{
    public const string Profile = "profile", Trigger = "trigger", Lighting = "lighting", Haptic = "haptic", Macro = "macro", Gyro = "gyro";
    public static readonly string[] All = [Profile, Trigger, Lighting, Haptic, Macro, Gyro];
}

/// <summary>Versioned envelope for anything shared between users. Pure data.</summary>
public sealed class SharePackage
{
    public const string FormatId = "dualsense-ultimate";
    public string Format { get; set; } = FormatId;
    public int SchemaVersion { get; set; } = Profile.CurrentSchema;
    public string Kind { get; set; } = PackageKinds.Profile;
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public JsonElement Payload { get; set; }

    public static SharePackage Wrap<T>(string kind, string name, T payload, string author = "", string description = "") => new()
    {
        Kind = kind, Name = name, Author = author, Description = description,
        Payload = JsonSerializer.SerializeToElement(payload, JsonFile.Options),
    };
}

public static class TriggerPresets
{
    private static TriggerEffect T(string name, TriggerEffectType type, int start = 0, int end = 8, int s = 4, int s2 = 4, int freq = 20, int period = 2, int f1 = 1, int f2 = 4) =>
        new() { Name = name, Type = type, Start = start, End = end, Strength = s, Strength2 = s2, Frequency = freq, Period = period, FirstFoot = f1, SecondFoot = f2 };

    public static IReadOnlyList<TriggerEffect> BuiltIn { get; } =
    [
        T("Off", TriggerEffectType.Off),
        T("Resistencia suave", TriggerEffectType.Feedback, 0, 9, 2),
        T("Resistencia media", TriggerEffectType.Feedback, 1, 9, 5),
        T("Resistencia rígida", TriggerEffectType.Feedback, 0, 9, 8),
        T("Resistencia progresiva", TriggerEffectType.SlopeFeedback, 0, 9, 1, 8),
        T("Resistencia decreciente", TriggerEffectType.SlopeFeedback, 0, 9, 8, 1),
        T("Pistola", TriggerEffectType.Weapon, 2, 5, 8),
        T("Rifle semiautomático", TriggerEffectType.Weapon, 3, 6, 6),
        T("Gatillo sensible", TriggerEffectType.Weapon, 2, 3, 3),
        T("Arco", TriggerEffectType.Bow, 1, 6, 6, 8),
        T("Arma automática", TriggerEffectType.Vibration, 3, 9, 6, freq: 12),
        T("Ametralladora pesada", TriggerEffectType.Machine, 2, 9, 7, 3, 14, 2),
        T("Vibración fina", TriggerEffectType.Vibration, 0, 9, 3, freq: 60),
        T("Galope", TriggerEffectType.Galloping, 0, 9, f1: 1, f2: 5, freq: 4),
        T("Freno (coche)", TriggerEffectType.SlopeFeedback, 2, 8, 3, 8),
        T("Acelerador (coche)", TriggerEffectType.Feedback, 3, 9, 3),
        T("ABS", TriggerEffectType.Vibration, 5, 9, 7, freq: 30),
    ];
}
