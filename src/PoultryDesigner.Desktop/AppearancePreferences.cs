using System.Text.Json;

namespace PoultryDesigner.Desktop;

internal static class AppearancePreferences
{
    private sealed record Preference(bool Dark);
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExperimentDesigner", "appearance.json");
    internal static bool? Load(string? path = null)
    {
        try { return File.Exists(path ?? FilePath) ? JsonSerializer.Deserialize<Preference>(File.ReadAllText(path ?? FilePath))?.Dark : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or JsonException) { return null; }
    }
    internal static void Save(bool dark, string? path = null)
    {
        string target = path ?? FilePath; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(new Preference(dark))); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
