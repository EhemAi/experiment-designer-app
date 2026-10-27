$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$dotnet = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
foreach ($project in @('src/PoultryDesigner.Desktop/PoultryDesigner.Desktop.csproj', 'src/PoultryDesigner.Setup/PoultryDesigner.Setup.csproj', 'tests/PoultryDesigner.Checks/PoultryDesigner.Checks.csproj')) {
    & $dotnet restore (Join-Path $PSScriptRoot $project) --locked-mode --configfile "$PSScriptRoot/NuGet.Config" --nologo
    if ($LASTEXITCODE -ne 0) { throw "Locked restore failed: $project" }
    & $dotnet build (Join-Path $PSScriptRoot $project) --no-restore --configuration Release --nologo -m:1 -p:EnforceCodeStyleInBuild=true
    if ($LASTEXITCODE -ne 0) { throw "Source analysis failed: $project" }
}
& "$PSScriptRoot/format-source.ps1" -Check
