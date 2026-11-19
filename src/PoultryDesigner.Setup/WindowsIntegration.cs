using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using PoultryDesigner.AppCore;

namespace PoultryDesigner.Setup;

// Windows shell registration belongs to Setup, never to the allocation application.
internal sealed class WindowsIntegration(string registryName, string programs, string desktop, string menuFolder = "Experiment Designer", string language = "en")
{
    private const string RegistryParent = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
    private const string RecordName = "maintenance.json";
    private sealed record OwnedFile(string Path, string Hash);
    private sealed record Maintenance(string Product, string Root, string RegistryName, string Programs, string Desktop, OwnedFile[] Files, string MenuFolder = "Experiment Designer", string Language = "en");
    internal static WindowsIntegration CurrentUser => new("ExperimentDesigner", Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    internal static string DefaultRoot
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryParent + "ExperimentDesigner");
            return key?.GetValue("InstallLocation") as string ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExperimentDesigner");
        }
    }
    internal static (string MenuFolder, string Language) Preferences(string root)
    {
        var previous = ReadMaintenance(root);
        return (previous?.MenuFolder ?? "Experiment Designer", previous?.Language ?? "en");
    }
    internal static WindowsIntegration ForInstallation(string menuFolder, string language)
    {
        ValidateMenuFolder(menuFolder);
        return new("ExperimentDesigner", Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), menuFolder, language);
    }
    internal static void ValidateMenuFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (folder.Length == 0) return;
        if (Path.IsPathRooted(folder) || folder.Split('/', '\\').Any(part =>
            string.IsNullOrWhiteSpace(part) || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
            part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            System.Text.RegularExpressions.Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])($|\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new ArgumentException("Choose a valid relative Start Menu folder.");
        if (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), folder, "Experiment Designer.lnk").Length >= 260)
            throw new ArgumentException("Choose a shorter Start Menu folder.");
    }
    internal void ValidateRegistration(string root, bool desktopShortcut)
    {
        ValidateMenuFolder(menuFolder);
        using var existing = Registry.CurrentUser.OpenSubKey(RegistryPath);
        if (existing is not null && !string.Equals(existing.GetValue("InstallLocation") as string, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Another Experiment Designer installation is registered. Choose its installation folder to update it.");
        if (menuFolder.Length > 0 && !OwnsLink(StartLink, root) || desktopShortcut && !OwnsLink(DesktopLink, root))
            throw new IOException("A different Experiment Designer shortcut already exists.");
        // Resolve the old registration now, so a damaged receipt fails before copying files.
        _ = ReadMaintenance(root);
    }
    private string RegistryPath => RegistryParent + registryName;
    private string StartLink => Path.Combine(programs, menuFolder, "Experiment Designer.lnk");
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
        ValidateMenuFolder(record.MenuFolder);
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
    // Windows shell shortcuts and the bundled native font loader have MAX_PATH
    // limits even when .NET can copy longer files. Reject these roots up front.
    internal static void ValidateRoot(string root, string version)
    {
        string target = Path.Combine(Path.GetFullPath(root), "releases", version + "-" + new string('0', 32), "App", "PoultryDesigner.Desktop.exe");
        string fonts = Path.Combine(Path.GetFullPath(root), "releases", version + "-" + new string('0', 32), "App", "Assets", "Fonts", "Manrope-SemiBold.otf");
        if (Math.Max(target.Length, fonts.Length) >= 260) throw new ArgumentException("Choose a shorter installation folder. The resulting Windows shortcut or font path is too long.");
    }
    internal void Register(string package, string root, InstallReceipt receipt, bool desktopShortcut)
    {
        ValidateRoot(root, receipt.Version);
        root = Path.GetFullPath(root);
        using (var existing = Registry.CurrentUser.OpenSubKey(RegistryPath))
            if (existing is not null && !string.Equals(existing.GetValue("InstallLocation") as string, root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Another Experiment Designer installation is registered. Its entry was preserved.");
        ValidateRegistration(root, desktopShortcut);
        if ((menuFolder.Length > 0 && !OwnsLink(StartLink, root)) || (desktopShortcut && !OwnsLink(DesktopLink, root))) throw new IOException("A different Experiment Designer shortcut was preserved.");
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
        File.WriteAllText(Under(maintenance, RecordName), JsonSerializer.Serialize(new Maintenance(Installation.Product, root, registryName, programs, desktop, files.ToArray(), menuFolder, language)));
        string targetApp = Installation.Executable(root, receipt);
        if (menuFolder.Length > 0) WriteLink(StartLink, root, targetApp);
        if (previous is not null && previous.MenuFolder.Length > 0)
        {
            string oldLink = Path.GetFullPath(Path.Combine(previous.Programs, previous.MenuFolder, "Experiment Designer.lnk"));
            if (menuFolder.Length == 0 || !string.Equals(oldLink, Path.GetFullPath(StartLink), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(oldLink) && OwnsLink(oldLink, root)) File.Delete(oldLink);
                string oldFolder = Path.GetDirectoryName(oldLink)!;
                if (Directory.Exists(oldFolder) && !Directory.EnumerateFileSystemEntries(oldFolder).Any()) Directory.Delete(oldFolder);
            }
        }
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
        return new(record.RegistryName, record.Programs, record.Desktop, record.MenuFolder, record.Language);
    }
    internal void Unregister(string root)
    {
        root = Path.GetFullPath(root);
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
        {
            if (key is not null && string.Equals(key.GetValue("InstallLocation") as string, root, StringComparison.OrdinalIgnoreCase)) Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
        }
        foreach (string link in (menuFolder.Length > 0 ? new[] { StartLink, DesktopLink } : new[] { DesktopLink })) if (File.Exists(link) && OwnsLink(link, root)) File.Delete(link);
        string menuDirectory = Path.GetDirectoryName(StartLink)!; if (menuFolder.Length > 0 && Directory.Exists(menuDirectory) && !Directory.EnumerateFileSystemEntries(menuDirectory).Any()) Directory.Delete(menuDirectory);
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
        string fixture = Path.Combine(Path.GetTempPath(), "ExperimentDesigner-integration-" + Guid.NewGuid().ToString("N"));
        string tooLong = Path.Combine(fixture, new string('x', 200));
        bool rejectedLongPath = false;
        try { ValidateRoot(tooLong, Installation.ReadPackage(package).Version); } catch (ArgumentException) { rejectedLongPath = true; }
        Check(rejectedLongPath && !Directory.Exists(tooLong), "Overlong shortcut roots are rejected before creating an installation");
        string root = Path.Combine(fixture, "Installed app with spaces – Unicode");
        string testRegistry = "ExperimentDesigner.Test-" + Guid.NewGuid().ToString("N");
        string testPrograms = Path.Combine(fixture, "Programs"), testDesktop = Path.Combine(fixture, "Desktop");
        var integration = new WindowsIntegration(testRegistry, testPrograms, testDesktop);
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
            // Maintenance records written before the wizard omit these optional fields.
            string maintenancePath = Path.Combine(root, "Installer", RecordName);
            string currentRecord = File.ReadAllText(maintenancePath);
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(currentRecord)!.AsObject();
            legacy.Remove("MenuFolder"); legacy.Remove("Language");
            File.WriteAllText(maintenancePath, legacy.ToJsonString());
            Check(Preferences(root) == ("Experiment Designer", "en"), "Legacy maintenance records retain default shortcuts and English");
            File.WriteAllText(maintenancePath, currentRecord);
            var custom = new WindowsIntegration(testRegistry, testPrograms, testDesktop, "Research tools", "es-MX");
            custom.Register(package, root, updated, false);
            Check(File.Exists(custom.StartLink) && !File.Exists(integration.StartLink), "Custom Start Menu folder replaces the owned previous shortcut");
            Check(Preferences(root) == ("Research tools", "es-MX"), "Chosen shortcut folder and Mexican Spanish are persisted");
            var caseOnly = new WindowsIntegration(testRegistry, testPrograms, testDesktop, "research TOOLS", "es-MX");
            caseOnly.Register(package, root, updated, false);
            Check(File.Exists(custom.StartLink), "Changing only folder letter case preserves the shortcut");
            var withoutMenu = new WindowsIntegration(testRegistry, testPrograms, testDesktop, "", "es-ES");
            withoutMenu.Register(package, root, updated, false);
            Check(!File.Exists(custom.StartLink) && Preferences(root) == ("", "es-ES"), "Disabling Start Menu removes the previous owned shortcut");
            integration.Register(package, root, updated, false);
            foreach (string invalid in new[] { "../outside", @"C:\outside", "CON", "bad.", "bad ", @"Research\..\outside" })
            {
                bool rejected = false;
                try { ValidateMenuFolder(invalid); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Invalid Start Menu folder is rejected: " + invalid);
            }
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
