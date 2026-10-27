using System.Security.Cryptography;
using System.Text.Json;
using PoultryDesigner.AppCore;

internal static class Section7Checks
{
    internal static void RunPublished(string package, string workspace)
    {
        string root = Path.Combine(workspace, "analysis", "installation-validation", "published-" + Guid.NewGuid().ToString("N"));
        var receipt = Installation.Install(package, root);
        string study = Path.Combine(root, "retained-study.poultryproj"); File.WriteAllText(study, "user file preservation fixture");
        var start = new System.Diagnostics.ProcessStartInfo(Installation.Executable(root, receipt))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("--smoke"); start.ArgumentList.Add(workspace);
        start.Environment["DOTNET_ROOT"] = start.Environment["DOTNET_ROOT_X64"] = Path.Combine(root, "absent-global-runtime");
        using var process = System.Diagnostics.Process.Start(start)!;
        if (!process.WaitForExit(120000)) { process.Kill(entireProcessTree: true); throw new Exception("Installed apphost workflow checks timed out."); }
        if (process.ExitCode != 0) throw new Exception("Installed apphost failed native workflow checks.");
        Installation.Uninstall(root);
        if (File.ReadAllText(study) != "user file preservation fixture" || Installation.ReadInstalled(root) is not null) throw new Exception("Installed package removal did not preserve the user fixture.");
        File.WriteAllText(Path.Combine(workspace, "analysis", "package-validation", "installed-app-verification.txt"), "PASS: packaged installation, installed apphost with bundled runtime, native desktop workflow and idle/scroll checks, removal and user-file preservation.\n");
        Console.WriteLine("PASS: packaged installation, installed runtime and removal.");
    }
    internal static int Run(string workspace)
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception("Section 7: " + reason); }
        void Reject(Action action, string reason) { bool rejected = false; try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or OperationCanceledException) { rejected = true; } Check(rejected, reason); }
        string root = Path.Combine(workspace, "analysis", "installation-validation", Guid.NewGuid().ToString("N"));
        string package = Path.Combine(root, "package"), installed = Path.Combine(root, "installed");
        var payload = new Dictionary<string, string> { ["App/ExperimentDesigner.exe"] = "fixture executable", ["App/ExperimentDesigner.dll"] = "fixture assembly", ["Runtime/dotnet.exe"] = "fixture runtime", ["App/User guide.html"] = "fixture guide" };
        foreach (var pair in payload) { string target = Path.Combine(package, pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, pair.Value); }
        var manifest = new PackageManifest(Installation.Product, "1.0.0", payload.Keys.Select(path => new PackageFile(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(package, path)))))).ToArray());
        void SaveManifest(PackageManifest value) => File.WriteAllText(Path.Combine(package, "package-manifest.json"), JsonSerializer.Serialize(value));
        SaveManifest(manifest); Installation.Verify(package, Installation.ReadPackage(package)); Check(true, "Valid payload verified");
        File.AppendAllText(Path.Combine(package, "Runtime/dotnet.exe"), "corrupt");
        Reject(() => Installation.Install(package, installed), "Corrupt payload refused"); Check(!Directory.Exists(installed), "Verification fails before installation creation");
        File.WriteAllText(Path.Combine(package, "Runtime/dotnet.exe"), payload["Runtime/dotnet.exe"]);
        SaveManifest(manifest with { Files = [.. manifest.Files, new("App/../../outside.txt", new('A', 64))] });
        Reject(() => Installation.ReadPackage(package), "Traversal refused");
        SaveManifest(manifest with { Files = [.. manifest.Files, manifest.Files[0] with { Path = "app/ExperimentDesigner.exe" }] });
        Reject(() => Installation.ReadPackage(package), "Ambiguous payload casing refused"); SaveManifest(manifest);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); Reject(() => Installation.Install(package, installed, token: cancellation.Token), "Cancelled verification refused");
            Check(!Directory.Exists(installed), "Cancelled verification leaves no installation");
        }
        var first = Installation.Install(package, installed);
        Check(File.ReadAllText(Installation.Executable(installed, first)) == payload["App/ExperimentDesigner.exe"], "Installed content matches source");
        Check(Installation.ReadInstalled(installed)!.ActiveRelease == first.ActiveRelease, "Receipt selects complete release");
        string project = Path.Combine(installed, "research.poultryproj"); File.WriteAllText(project, "user study");
        var second = Installation.Install(package, installed);
        Check(second.Releases.Length == 2 && second.ActiveRelease != first.ActiveRelease, "Update retains previous complete release");
        Check(File.Exists(Installation.Executable(installed, first)), "Previous executable retained after update");
        using (var cancellation = new CancellationTokenSource())
        {
            var progress = new ImmediateProgress(message => { if (message.StartsWith("Installing ")) cancellation.Cancel(); });
            Reject(() => Installation.Install(package, installed, progress, cancellation.Token), "Mid-copy cancellation rolls back");
            Check(Directory.GetDirectories(Path.Combine(installed, "releases")).Length == 2, "Incomplete release cleaned up");
            Check(Installation.ReadInstalled(installed)!.ActiveRelease == second.ActiveRelease, "Cancellation preserves active release");
        }
        string modified = Path.Combine(installed, first.ActiveRelease, "App", "User guide.html"); File.WriteAllText(modified, "user modified guide");
        string unknown = Path.Combine(installed, second.ActiveRelease, "App", "user.csv"); File.WriteAllText(unknown, "user records");
        using (var locked = new FileStream(Path.Combine(installed, second.ActiveRelease, "Runtime", "dotnet.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Reject(() => Installation.Uninstall(installed), "Busy payload blocks removal");
            Check(Installation.ReadInstalled(installed) is not null, "Failed removal retains receipt for retry");
        }
        Check(Installation.Uninstall(installed) >= 1, "Removal preserves modified payload");
        Check(File.ReadAllText(project) == "user study" && File.ReadAllText(unknown) == "user records" && File.ReadAllText(modified) == "user modified guide", "User projects, unknown files and modified files survive removal");
        Check(Installation.ReadInstalled(installed) is null, "Successful removal clears receipt");
        Check(!File.Exists(Installation.Executable(installed, second)), "Program removed after busy file retry");
        string unsafeRoot = Path.Combine(root, "unsafe"); Directory.CreateDirectory(unsafeRoot);
        File.WriteAllText(Path.Combine(unsafeRoot, "PoultryDesigner.install.json"), JsonSerializer.Serialize(first with { Releases = ["../outside"], ActiveRelease = "../outside" }));
        Reject(() => Installation.Uninstall(unsafeRoot), "Foreign receipt cannot authorize deletion");
        File.WriteAllText(Path.Combine(root, "check-results.txt"), $"PASS: {checks} installation assertions.\n");
        Console.WriteLine($"Section 7: {checks} installation assertions passed."); return checks;
    }
    private sealed class ImmediateProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
}

