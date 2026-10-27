$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$dotnetPath = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnetPath)) { $dotnetPath = 'dotnet' }
& $dotnetPath build "$PSScriptRoot/src/PoultryDesigner.Desktop/PoultryDesigner.Desktop.csproj" --configuration Release --configfile "$PSScriptRoot/NuGet.Config" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
& $dotnetPath "$PSScriptRoot/src/PoultryDesigner.Desktop/bin/Release/net10.0-windows/ExperimentDesigner.dll"
