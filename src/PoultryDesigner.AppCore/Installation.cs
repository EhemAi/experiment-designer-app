using System.Security.Cryptography;
using System.Text.Json;

namespace PoultryDesigner.AppCore;

public sealed record PackageFile(string Path, string Sha256);
public sealed record PackageManifest(string Product, string Version, PackageFile[] Files);
public sealed record InstallReceipt(string Product, string Version, string ActiveRelease, string[] Releases);
public static class Installation
{
    public const string Product = "PoultryExperimentDesigner";
    private const string ReceiptName = "PoultryDesigner.install.json";
    public static PackageManifest ReadPackage(string packageRoot)
    {
        var manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(System.IO.Path.Combine(packageRoot, "package-manifest.json")))
            ?? throw new InvalidDataException("Missing package manifest."); Validate(manifest); return manifest;
    }
    private static void Validate(PackageManifest manifest)
    {
        if (manifest.Product != Product || !Version.TryParse(manifest.Version, out _) || manifest.Files is null || manifest.Files.Length == 0) throw new InvalidDataException("Unsupported package.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files) {
            _ = Relative(file.Path);
            if (!paths.Add(file.Path) || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid package file/hash.");
        }
        foreach (string required in new[] { "Runtime/dotnet.exe" })
            if (!paths.Contains(required)) throw new InvalidDataException("Incomplete package: " + required);
        if (!(paths.Contains("App/ExperimentDesigner.exe") && paths.Contains("App/ExperimentDesigner.dll"))
            && !(paths.Contains("App/PoultryDesigner.Desktop.exe") && paths.Contains("App/PoultryDesigner.Desktop.dll")))
            throw new InvalidDataException("Incomplete application payload.");
    }
    private static string Relative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || !(path.StartsWith("App/", StringComparison.Ordinal) || path.StartsWith("Runtime/", StringComparison.Ordinal))) throw new InvalidDataException("Unsupported payload path.");
        if (path.Split('/').Any(s => s.Length == 0 || s is "." or ".." || s.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)) throw new InvalidDataException("Unsafe payload path.");
        return path.Replace('/', System.IO.Path.DirectorySeparatorChar);
    }
    private static string Under(string root, string relative)
    {
        string basePath = System.IO.Path.GetFullPath(root), target = System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath, relative));
        if (!target.StartsWith(basePath.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path leaves installation root.");
        for (string? current = target; current is not null; current = System.IO.Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked paths are not supported.");
        return target;
    }
    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static void Verify(string packageRoot, PackageManifest manifest, CancellationToken token = default)
    {
        Validate(manifest);
        foreach (var item in manifest.Files) { token.ThrowIfCancellationRequested(); string file = Under(packageRoot, Relative(item.Path));
            if (!File.Exists(file) || !string.Equals(Hash(file), item.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package verification failed: " + item.Path); }
    }
    public static InstallReceipt? ReadInstalled(string root)
    {
        string path = Under(root, ReceiptName); if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<InstallReceipt>(File.ReadAllText(path));
        if (receipt is null || receipt.Product != Product || receipt.Releases is null || !receipt.Releases.Contains(receipt.ActiveRelease)
            || receipt.Releases.Distinct().Count() != receipt.Releases.Length) throw new InvalidDataException("Unrecognized installation receipt.");
        foreach (string release in receipt.Releases) { if (release is null || release.Length != 41 || !release.StartsWith("releases/", StringComparison.Ordinal)
                || !Guid.TryParseExact(release[9..], "N", out _)) throw new InvalidDataException("Unsafe release path."); _ = Under(root, release.Replace('/', System.IO.Path.DirectorySeparatorChar)); }
        return receipt;
    }
    public static string Executable(string root, InstallReceipt receipt)
    {
        string folder = receipt.ActiveRelease.Replace('/', System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar + "App" + System.IO.Path.DirectorySeparatorChar;
        string current = Under(root, folder + "ExperimentDesigner.exe");
        return File.Exists(current) ? current : Under(root, folder + "PoultryDesigner.Desktop.exe");
    }
    public static InstallReceipt Install(string packageRoot, string root, IProgress<string>? progress = null, CancellationToken token = default)
    {
        var manifest = ReadPackage(packageRoot); progress?.Report("Verifying package..."); Verify(packageRoot, manifest, token);
        Directory.CreateDirectory(root); using var mutex = new FileStream(Under(root, ".poultry-install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = ReadInstalled(root); string relative = "releases/" + Guid.NewGuid().ToString("N"); string release = Under(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(release); bool committed = false;
        try {
            foreach (var item in manifest.Files) {
                token.ThrowIfCancellationRequested(); progress?.Report("Installing " + item.Path);
                string destination = Under(release, Relative(item.Path)); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                File.Copy(Under(packageRoot, Relative(item.Path)), destination, false);
                if (!string.Equals(Hash(destination), item.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Copied file verification failed.");
            }
            File.WriteAllText(Under(release, "installed-manifest.json"), JsonSerializer.Serialize(manifest));
            var receipt = new InstallReceipt(Product, manifest.Version, relative, (previous?.Releases ?? []).Append(relative).ToArray());
            string temporary = Under(root, ".receipt-" + Guid.NewGuid().ToString("N") + ".tmp");
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(receipt)); token.ThrowIfCancellationRequested(); File.Move(temporary, Under(root, ReceiptName), true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            committed = true; progress?.Report("Installation complete."); return receipt;
        }
        finally { if (!committed) { _ = Under(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)); Directory.Delete(release, true); } }
    }
    public static int Uninstall(string root, IProgress<string>? progress = null)
    {
        var receipt = ReadInstalled(root) ?? throw new InvalidDataException("No recognized installation here.");
        using var mutex = new FileStream(Under(root, ".poultry-install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // Preflight all manifests/paths before removing any files. Unknown or modified files remain.
        var releases = receipt.Releases.Select(r => { string folder = Under(root, r.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(Under(folder, "installed-manifest.json"))) ?? throw new InvalidDataException("Missing installed manifest.");
            Validate(manifest); foreach (var file in manifest.Files) _ = Under(folder, Relative(file.Path)); return (Folder: folder, Manifest: manifest); }).ToArray();
        int retained = 0;
        foreach (var release in releases) {
            var directories = new HashSet<string>();
            foreach (var item in release.Manifest.Files) {
                string path = Under(release.Folder, Relative(item.Path)); if (!File.Exists(path)) continue;
                if (string.Equals(Hash(path), item.Sha256, StringComparison.OrdinalIgnoreCase)) { progress?.Report("Removing " + item.Path); File.Delete(path); } else retained++;
                for (string? directory = System.IO.Path.GetDirectoryName(path); directory is not null && directory.StartsWith(release.Folder, StringComparison.OrdinalIgnoreCase); directory = System.IO.Path.GetDirectoryName(directory)) directories.Add(directory);
            }
        }
        // Keep every manifest until all file removals succeed, so a busy file can be retried.
        File.Delete(Under(root, ReceiptName));
        foreach (var release in releases) {
            File.Delete(Under(release.Folder, "installed-manifest.json"));
            var directories = release.Manifest.Files.SelectMany(item => {
                var result = new List<string>();
                for (string? directory = System.IO.Path.GetDirectoryName(Under(release.Folder, Relative(item.Path)));
                     directory is not null && directory.StartsWith(release.Folder, StringComparison.OrdinalIgnoreCase);
                     directory = System.IO.Path.GetDirectoryName(directory)) result.Add(directory);
                return result;
            }).Distinct().OrderByDescending(x => x.Length);
            foreach (string directory in directories) if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        return retained;
    }
}
