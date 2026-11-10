using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace DualSenseUltimate.App.Controls;

public static class FileIo
{
    private static readonly FilePickerFileType Json = new("Paquete DualSense Ultimate (JSON)") { Patterns = ["*.json"] };

    public static async Task<bool> SaveTextAsync(Visual v, string suggestedName, string text)
    {
        var top = TopLevel.GetTopLevel(v);
        if (top == null) return false;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = Sanitize(suggestedName) + ".json", FileTypeChoices = [Json], DefaultExtension = "json" });
        if (file == null) return false;
        await using var stream = await file.OpenWriteAsync();
        await using var w = new StreamWriter(stream);
        await w.WriteAsync(text);
        return true;
    }

    public static async Task<string?> OpenTextAsync(Visual v)
    {
        var top = TopLevel.GetTopLevel(v);
        if (top == null) return null;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = [Json] });
        if (files.Count == 0) return null;
        var props = await files[0].GetBasicPropertiesAsync();
        if (props.Size > Core.Profiles.ProfileValidator.MaxImportBytes) return new string(' ', Core.Profiles.ProfileValidator.MaxImportBytes + 1); // rejected by the validator
        await using var stream = await files[0].OpenReadAsync();
        using var r = new StreamReader(stream);
        return await r.ReadToEndAsync();
    }

    public static string Sanitize(string name)
    {
        var s = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' or ':' ? '_' : c).ToArray()).Trim();
        return s.Length == 0 ? "export" : s;
    }

    public static void OpenFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    public static void OpenUrl(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public static Task CopyAsync(Visual v, string text) => TopLevel.GetTopLevel(v)?.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
}
