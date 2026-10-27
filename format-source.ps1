param([switch]$Check)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$dotnet = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
foreach ($project in @('src/PoultryDesigner.Desktop/PoultryDesigner.Desktop.csproj', 'src/PoultryDesigner.Setup/PoultryDesigner.Setup.csproj', 'tests/PoultryDesigner.Checks/PoultryDesigner.Checks.csproj')) {
    $arguments = @('format', 'whitespace', (Join-Path $PSScriptRoot $project), '--no-restore', '--verbosity', 'minimal')
    if ($Check) { $arguments += '--verify-no-changes' }
    & $dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Formatting failed: $project" }
}
