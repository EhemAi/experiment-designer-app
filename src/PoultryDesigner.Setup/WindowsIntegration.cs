using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using PoultryDesigner.AppCore;

namespace PoultryDesigner.Setup;

// Windows shell registration belongs to Setup, never to the allocation application.
internal sealed class WindowsIntegration(string registryName, string programs, string desktop)
{
    private const string RegistryParent = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
    private const string RecordName = "maintenance.json";
    private sealed record OwnedFile(string Path, string Hash);
    private sealed record Maintenance(string Product, string Root, string RegistryName, string Programs, string Desktop, OwnedFile[] Files);
    internal static WindowsIntegration CurrentUser => new("ExperimentDesigner", Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    private string RegistryPath => RegistryParent + registryName;
    private string StartLink => Path.Combine(programs, "Experiment Designer", "Experiment Designer.lnk");
    private string DesktopLink => Path.Combine(desktop, "Experiment Designer.lnk");
    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string Under(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Split('/', '\\').Any(p => p is ".." or "." or "")) throw new InvalidDataException("Unsafe maintenance path.");
        string absolute = Path.GetFullPath(Path.Combine(root, relative));
        if (!absolute.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Maintenance path leaves its folder.");
        for (string? part = absolute; part is not null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && File.GetAttributes(part).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked installation paths are not supported.");
        return absolute;
    }
    private static Maintenance? ReadMaintenance(string root)
    {
        string path = Under(root, "Installer/" + RecordName);
        if (!File.Exists(path)) return null;
        var record = JsonSerializer.Deserialize<Maintenance>(File.ReadAllText(path)) ?? throw new InvalidDataException("Missing maintenance record.");
        if (record.Product != Installation.Product || !string.Equals(record.Root, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) || record.Files is null) throw new InvalidDataException("Foreign maintenance record.");
        foreach (var file in record.Files) _ = Under(Under(root, "Installer"), file.Path);
        return record;
    }
    private static void WithLink(string path, Action<dynamic> action)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? link = null;
        try { link = ((dynamic)shell).CreateShortcut(path); action((dynamic)link); }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
    }
    private static bool OwnsLink(string path, string root)
    {
        if (!File.Exists(path)) return true;
        bool owned = false;
        WithLink(path, link => owned = ((string)link.TargetPath).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        return owned;
    }
    private static void WriteLink(string path, string root, string target)
    {
        if (!OwnsLink(path, root)) throw new IOException("A different shortcut named Experiment Designer was preserved: " + path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WithLink(path, link => { link.TargetPath = target; link.WorkingDirectory = Path.GetDirectoryName(target); link.Description = "Experiment Designer"; link.IconLocation = target + ",0"; link.Save(); });
    }
    internal void Register(string package, string root, InstallReceipt receipt, bool desktopShortcut)
    {
        root = Path.GetFullPath(root);
        using (var existing = Registry.CurrentUser.OpenSubKey(RegistryPath))
            if (existing is not null && !string.Equals(existing.GetValue("InstallLocation") as string, root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Another Experiment Designer installation is registered. Its entry was preserved.");
        if (!OwnsLink(StartLink, root) || (desktopShortcut && !OwnsLink(DesktopLink, root))) throw new IOException("A different Experiment Designer shortcut was preserved.");
        string maintenance = Under(root, "Installer");
        var previous = ReadMaintenance(root);
        var owned = previous?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase) ?? [];
        var files = new List<OwnedFile>();
        var sources = Directory.EnumerateFiles(Path.Combine(package, "Setup"), "*", SearchOption.AllDirectories).Select(source => (Source: source, Relative: "Setup/" + Path.GetRelativePath(Path.Combine(package, "Setup"), source).Replace('\\', '/'))).ToArray();
        foreach (var file in sources)
        {
            string target = Under(maintenance, file.Relative);
            if (File.Exists(target) && (!owned.TryGetValue(file.Relative, out var old) || Hash(target) != old.Hash)) throw new IOException("A modified maintenance file was preserved: " + target);
        }
        string scriptPath = Under(maintenance, "Uninstall.ps1");
        if (File.Exists(scriptPath) && (!owned.TryGetValue("Uninstall.ps1", out var script) || Hash(scriptPath) != script.Hash)) throw new IOException("A modified uninstall script was preserved.");
        foreach (var file in sources)
        {
            string target = Under(maintenance, file.Relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file.Source, target, true);
            string hash = Hash(target); if (hash != Hash(file.Source)) throw new IOException("Maintenance copy verification failed."); files.Add(new(file.Relative, hash));
        }
        File.WriteAllText(scriptPath, Bootstrap(root, registryName.StartsWith("ExperimentDesigner.Test-", StringComparison.Ordinal)), new System.Text.UTF8Encoding(true));
        files.Add(new("Uninstall.ps1", Hash(scriptPath)));
        Directory.CreateDirectory(maintenance);
        File.WriteAllText(Under(maintenance, RecordName), JsonSerializer.Serialize(new Maintenance(Installation.Product, root, registryName, programs, desktop, files.ToArray())));
        string targetApp = Installation.Executable(root, receipt);
        WriteLink(StartLink, root, targetApp);
        if (desktopShortcut || File.Exists(DesktopLink) && OwnsLink(DesktopLink, root)) WriteLink(DesktopLink, root, targetApp);
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue("DisplayName", "Experiment Designer"); key.SetValue("DisplayVersion", receipt.Version);
        key.SetValue("Publisher", "Emmanuel Alagbe"); key.SetValue("InstallLocation", root);
        key.SetValue("DisplayIcon", targetApp + ",0"); key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        key.SetValue("UninstallString", $"\"{powershell}\" -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        long bytes = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, (bytes + 1023) / 1024), RegistryValueKind.DWord);
    }
    // Run removal from a temporary copy, so Windows never locks the files it is removing.
    // The bundled runtime is copied only while uninstalling, not duplicated permanently.
    private static string Bootstrap(string root, bool test) => $$"""
        $ErrorActionPreference = 'Stop'
        $installRoot = '{{root.Replace("'", "''")}}'
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $scratch = Join-Path $temporaryRoot ('ExperimentDesigner-uninstall-' + [Guid]::NewGuid().ToString('N'))
        function Copy-OwnedDirectory([string]$source, [string]$destination) {
            if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked maintenance folders are not supported.' }
            # Robocopy supports long Windows paths; no mirror, move or deletion switches are used.
            & (Join-Path $env:SystemRoot 'System32/robocopy.exe') $source $destination /E /R:0 /W:0 /XJ /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw 'Could not prepare the temporary uninstaller.' }
            $global:LASTEXITCODE = 0
        }
        try {
            $record = Get-Content -LiteralPath (Join-Path $installRoot 'PoultryDesigner.install.json') -Raw | ConvertFrom-Json
            if ($record.Product -ne 'PoultryExperimentDesigner' -or $record.ActiveRelease -notmatch '^releases/[a-fA-F0-9]{32}$') { throw 'Unrecognized installation.' }
            $runtime = [IO.Path]::GetFullPath((Join-Path $installRoot ($record.ActiveRelease + '/Runtime')))
            if (!$runtime.StartsWith($installRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid runtime path.' }
            New-Item -ItemType Directory -Path $scratch | Out-Null
            Copy-OwnedDirectory (Join-Path $installRoot 'Installer/Setup') (Join-Path $scratch 'Setup')
            Copy-OwnedDirectory $runtime (Join-Path $scratch 'Runtime')
            $uninstaller = Join-Path $scratch 'Setup/ExperimentDesigner.Setup.exe'
            $child = Start-Process -FilePath $uninstaller -ArgumentList @('{{(test ? "--uninstall-check" : "--uninstall")}}', ('"' + $installRoot + '"')) -WindowStyle {{(test ? "Hidden" : "Normal")}} -PassThru -Wait
            if ($child.ExitCode -ne 0) { throw 'Removal did not complete. Run Setup to retry.' }
        } finally {
            $resolvedScratch = [IO.Path]::GetFullPath($scratch)
            if ($resolvedScratch.StartsWith($temporaryRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolvedScratch) -match '^ExperimentDesigner-uninstall-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $resolvedScratch)) {
                Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
            }
        }
        exit 0
        """;
    internal static WindowsIntegration ForRemoval(string root, bool test = false)
    {
        var record = ReadMaintenance(root);
        if (record is null) { if (test) throw new InvalidDataException("No test maintenance registration."); return CurrentUser; } // Older installations.
        if (test && !record.RegistryName.StartsWith("ExperimentDesigner.Test-", StringComparison.Ordinal)) throw new InvalidDataException("Only a test installation may be removed without confirmation.");
        if (!test && record.RegistryName != "ExperimentDesigner") throw new InvalidDataException("Unexpected installation registration.");
        return new(record.RegistryName, record.Programs, record.Desktop);
    }
    internal void Unregister(string root)
    {
        root = Path.GetFullPath(root);
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
        {
            if (key is not null && string.Equals(key.GetValue("InstallLocation") as string, root, StringComparison.OrdinalIgnoreCase)) Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
        }
        foreach (string link in new[] { StartLink, DesktopLink }) if (File.Exists(link) && OwnsLink(link, root)) File.Delete(link);
        string menuFolder = Path.GetDirectoryName(StartLink)!; if (Directory.Exists(menuFolder) && !Directory.EnumerateFileSystemEntries(menuFolder).Any()) Directory.Delete(menuFolder);
        var record = ReadMaintenance(root); if (record is null) return;
        string maintenance = Under(root, "Installer");
        foreach (var file in record.Files) { string path = Under(maintenance, file.Path); if (File.Exists(path) && Hash(path) == file.Hash) File.Delete(path); }
        File.Delete(Under(maintenance, RecordName));
        foreach (string folder in record.Files.Select(f => Path.GetDirectoryName(Under(maintenance, f.Path))!).Append(maintenance).SelectMany(f => Ancestors(f, maintenance)).Distinct().OrderByDescending(f => f.Length))
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
    }
    private static IEnumerable<string> Ancestors(string folder, string root)
    { for (string? current = folder; current is not null && (current == root || current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)); current = Path.GetDirectoryName(current)) yield return current; }

    internal static int Check(string package, string workspace)
    {
        int checks = 0; void Check(bool pass, string label) { checks++; if (!pass) throw new Exception(label); }
        string fixture = Path.Combine(workspace, "analysis", "windows-integration", Guid.NewGuid().ToString("N"));
        string root = Path.Combine(fixture, "Installed app with spaces – Unicode");
        var integration = new WindowsIntegration("ExperimentDesigner.Test-" + Guid.NewGuid().ToString("N"), Path.Combine(fixture, "Programs"), Path.Combine(fixture, "Desktop"));
        var receipt = Installation.Install(package, root);
        try
        {
            integration.Register(package, root, receipt, false);
            Check(File.Exists(integration.StartLink) && !File.Exists(integration.DesktopLink), "Start is registered even when optional desktop shortcut is off");
            integration.Register(package, root, receipt, true);
            using (var key = Registry.CurrentUser.OpenSubKey(integration.RegistryPath))
            {
                Check(key?.GetValue("DisplayName") as string == "Experiment Designer", "Installed apps display name");
                Check(key?.GetValue("DisplayVersion") as string == receipt.Version, "Installed version");
                Check(key?.GetValue("Publisher") as string == "Emmanuel Alagbe", "Creator/publisher");
                Check(key?.GetValue("InstallLocation") as string == root, "Install folder");
                Check((key?.GetValue("UninstallString") as string)?.Contains("Uninstall.ps1") == true, "Durable removal command");
            }
            Check(File.Exists(integration.StartLink) && File.Exists(integration.DesktopLink), "Start and desktop shortcuts");
            WithLink(integration.StartLink, link => Check((string)link.TargetPath == Installation.Executable(root, receipt), "Start shortcut launches current release"));
            var updated = Installation.Install(package, root); integration.Register(package, root, updated, false);
            WithLink(integration.StartLink, link => Check((string)link.TargetPath == Installation.Executable(root, updated), "Update redirects Start shortcut"));
            WithLink(integration.DesktopLink, link => Check((string)link.TargetPath == Installation.Executable(root, updated), "Update refreshes an existing desktop shortcut"));
            WithLink(integration.StartLink, link => { link.TargetPath = Path.Combine(Environment.SystemDirectory, "notepad.exe"); link.Save(); });
            bool refused = false; try { integration.Register(package, root, updated, false); } catch (IOException) { refused = true; }
            Check(refused && !OwnsLink(integration.StartLink, root), "Foreign shortcut is preserved rather than overwritten");
            WithLink(integration.StartLink, link => { link.TargetPath = Installation.Executable(root, updated); link.Save(); });
            File.WriteAllText(Path.Combine(root, "retain-study.poultryproj"), "user study");
            File.WriteAllText(Path.Combine(root, "Installer", "keep.txt"), "unknown maintenance file");
            // Invoke the exact registered bootstrap without the original ZIP or a global runtime.
            var launch = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(root, "Installer", "Uninstall.ps1") }) launch.ArgumentList.Add(argument);
            launch.Environment["DOTNET_ROOT"] = launch.Environment["DOTNET_ROOT_X64"] = Path.Combine(root, "absent-global-runtime");
            using var process = Process.Start(launch)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120000)) { process.Kill(true); throw new Exception("Uninstall bootstrap timed out."); }
            Check(process.ExitCode == 0 && File.Exists(Path.Combine(root, "uninstall-check.txt")), "Registered uninstaller completes using its temporary bundled runtime: " + stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult() + (File.Exists(Path.Combine(root, "uninstall-check.txt")) ? File.ReadAllText(Path.Combine(root, "uninstall-check.txt")) : "no child result"));
            using var removed = Registry.CurrentUser.OpenSubKey(integration.RegistryPath);
            Check(removed is null && !File.Exists(integration.StartLink) && !File.Exists(integration.DesktopLink), "Removal clears Windows registration and owned shortcuts");
            Check(Installation.ReadInstalled(root) is null && !File.Exists(Installation.Executable(root, updated)), "Removal deletes verified installed program");
            Check(File.ReadAllText(Path.Combine(root, "retain-study.poultryproj")) == "user study" && File.ReadAllText(Path.Combine(root, "Installer", "keep.txt")) == "unknown maintenance file", "Removal preserves user/unknown files");
        }
        finally
        {
            if (Installation.ReadInstalled(root) is not null) Installation.Uninstall(root);
            integration.Unregister(root);
        }
        Directory.CreateDirectory(Path.Combine(workspace, "analysis", "package-validation"));
        File.WriteAllText(Path.Combine(workspace, "analysis", "package-validation", "windows-integration.txt"), $"PASS: {checks} Windows integration assertions: registry, shortcuts, updates, durable uninstall, runtime independence and file preservation.\n");
        return checks;
    }
}
