param([switch]$SkipChecks, [ValidatePattern('^[a-zA-Z0-9-]*$')][string]$PackageLabel = '')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli-home'
$dotnetPath = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$dotnetRoot = Split-Path -Parent $dotnetPath
$frameworkVersion = (Get-Content -LiteralPath "$PSScriptRoot/runtime-version.txt" -Raw).Trim()
foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
    if (!(Test-Path -LiteralPath "$dotnetRoot/shared/$framework/$frameworkVersion")) {
        throw "Install the pinned Windows SDK/runtime first. Missing $framework $frameworkVersion under $dotnetRoot. See README.md."
    }
}
if (!$SkipChecks) { & "$PSScriptRoot/build-and-check.ps1"; if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' } }
$releaseVersion = ([xml](Get-Content -LiteralPath "$PSScriptRoot/src/PoultryDesigner.Desktop/PoultryDesigner.Desktop.csproj" -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$packageName = 'Experiment-Designer-' + $(if ($PackageLabel) { $PackageLabel + '-' }) + $releaseVersion + '-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
$releasePath = Join-Path $PSScriptRoot "dist/$packageName"
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
foreach ($project in @(@{Name='Desktop';Folder='App'}, @{Name='Setup';Folder='Setup'})) {
    & $dotnetPath publish "$PSScriptRoot/src/PoultryDesigner.$($project.Name)/PoultryDesigner.$($project.Name).csproj" --configuration Release --self-contained false --configfile "$PSScriptRoot/NuGet.Config" --output "$releasePath/$($project.Folder)" -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=../Runtime -p:DebugType=None -p:DebugSymbols=false --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($project.Name)" }
}
$runtimePath = Join-Path $releasePath 'Runtime'
New-Item -ItemType Directory -Path $runtimePath -Force | Out-Null
foreach ($filename in @('dotnet.exe', 'LICENSE.txt', 'ThirdPartyNotices.txt')) { Copy-Item -LiteralPath "$dotnetRoot/$filename" -Destination $runtimePath }
New-Item -ItemType Directory -Path "$runtimePath/host/fxr" -Force | Out-Null
Copy-Item -LiteralPath "$dotnetRoot/host/fxr/$frameworkVersion" -Destination "$runtimePath/host/fxr" -Recurse
foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
    New-Item -ItemType Directory -Path "$runtimePath/shared/$framework" -Force | Out-Null
    Copy-Item -LiteralPath "$dotnetRoot/shared/$framework/$frameworkVersion" -Destination "$runtimePath/shared/$framework" -Recurse
}
Copy-Item -LiteralPath "$PSScriptRoot/User guide.html" -Destination "$releasePath/App/User guide.html"
Copy-Item -LiteralPath "$PSScriptRoot/PACKAGE-README.txt" -Destination "$releasePath/README.txt"
$packageFiles = @(foreach ($folder in @('App', 'Runtime')) { Get-ChildItem -LiteralPath (Join-Path $releasePath $folder) -File -Recurse | ForEach-Object {
    @{Path=[IO.Path]::GetRelativePath($releasePath, $_.FullName).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
} })
@{Product='PoultryExperimentDesigner';Version=$releaseVersion;Files=$packageFiles} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$releasePath/package-manifest.json" -Encoding utf8
# Run the published apphosts with global .NET discovery disabled: each must use its adjacent bundled runtime.
$savedRoot = $env:DOTNET_ROOT
$savedRootX64 = $env:DOTNET_ROOT_X64
try {
    $env:DOTNET_ROOT = Join-Path $releasePath 'missing-global-runtime'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $verifyResult = Join-Path $PSScriptRoot 'analysis/package-validation/installer-verification.txt'
    New-Item -ItemType Directory -Path (Split-Path $verifyResult) -Force | Out-Null
    $setupProcess = Start-Process -FilePath "$releasePath/Setup/ExperimentDesigner.Setup.exe" -ArgumentList @('--verify', ('"' + $releasePath + '"'), ('"' + $verifyResult + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($setupProcess.ExitCode -ne 0) { throw 'Published installer verification failed.' }
    $integrationProcess = Start-Process -FilePath "$releasePath/Setup/ExperimentDesigner.Setup.exe" -ArgumentList @('--integration-check', ('"' + $releasePath + '"'), ('"' + $PSScriptRoot + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($integrationProcess.ExitCode -ne 0) { throw 'Windows app registration or uninstall checks failed. See analysis/package-validation/windows-integration.txt.' }
    $appProcess = Start-Process -FilePath "$releasePath/App/ExperimentDesigner.exe" -ArgumentList @('--smoke', ('"' + $PSScriptRoot + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($appProcess.ExitCode -ne 0) { throw 'Published app smoke checks failed.' }
    $startupResult = Join-Path $PSScriptRoot 'analysis/package-validation/startup-verification.txt'
    $startupProcess = Start-Process -FilePath "$releasePath/App/ExperimentDesigner.exe" -ArgumentList @('--startup-check', ('"' + $startupResult + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($startupProcess.ExitCode -ne 0) { throw 'Published startup transition failed.' }
    $renderPath = Join-Path $PSScriptRoot 'analysis/package-validation/setup.png'
    $renderProcess = Start-Process -FilePath "$releasePath/Setup/ExperimentDesigner.Setup.exe" -ArgumentList @('--render', ('"' + $renderPath + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($renderProcess.ExitCode -ne 0) { throw 'Installer rendering failed.' }
    & $dotnetPath "$PSScriptRoot/tests/PoultryDesigner.Checks/bin/Release/net10.0/PoultryDesigner.Checks.dll" --package-check $releasePath $PSScriptRoot
    if ($LASTEXITCODE -ne 0) { throw 'Installed package checks failed.' }
} finally { $env:DOTNET_ROOT = $savedRoot; $env:DOTNET_ROOT_X64 = $savedRootX64 }
$archivePath = "$releasePath.zip"
Compress-Archive -LiteralPath $releasePath -DestinationPath $archivePath -CompressionLevel Optimal
(Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash | Set-Content -LiteralPath "$archivePath.sha256.txt"
Set-Content -LiteralPath "$PSScriptRoot/dist/latest-package.txt" -Value $archivePath
Write-Output "Package: $archivePath"
Write-Output "Bundled runtime: $frameworkVersion. Apphost, installer hashes and published GUI checks passed."
