param([switch]$SkipDesktop)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$dotnetPath = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnetPath)) { $dotnetPath = 'dotnet' }
& $dotnetPath restore "$PSScriptRoot/tests/PoultryDesigner.Checks/PoultryDesigner.Checks.csproj" --configfile "$PSScriptRoot/NuGet.Config" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
$checkOutput = Join-Path $PSScriptRoot 'analysis/check-results.txt'
New-Item -ItemType Directory -Path (Split-Path $checkOutput) -Force | Out-Null
& $dotnetPath run --project "$PSScriptRoot/tests/PoultryDesigner.Checks/PoultryDesigner.Checks.csproj" --no-restore --configuration Release -- "$PSScriptRoot" | Tee-Object -FilePath $checkOutput
if ($LASTEXITCODE -ne 0) { throw 'Engine checks failed.' }
if (!$SkipDesktop) {
    & $dotnetPath build "$PSScriptRoot/src/PoultryDesigner.Desktop/PoultryDesigner.Desktop.csproj" --configuration Release --configfile "$PSScriptRoot/NuGet.Config" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    & $dotnetPath "$PSScriptRoot/src/PoultryDesigner.Desktop/bin/Release/net10.0-windows/ExperimentDesigner.dll" --smoke "$PSScriptRoot"
    $desktopResult = Join-Path $PSScriptRoot 'analysis/desktop-validation/check-results.txt'
    if ($LASTEXITCODE -ne 0) { Get-Content $desktopResult; throw 'Desktop smoke checks failed.' }
    Get-Content $desktopResult | Tee-Object -FilePath $checkOutput -Append
}
