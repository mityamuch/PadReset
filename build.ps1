param(
    [switch]$Test,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory = 'dist\PadReset'
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.local\dotnet'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.local\nuget'

dotnet build "$PSScriptRoot\PadReset.slnx" -c $Configuration --nologo -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    dotnet "$PSScriptRoot\tests\PadReset.Tests\bin\$Configuration\net10.0-windows\PadReset.Tests.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
$publishDirectory = Join-Path $PSScriptRoot $OutputDirectory
dotnet publish "$PSScriptRoot\src\PadReset\PadReset.csproj" -c $Configuration --no-build --no-restore --self-contained false -o $publishDirectory --nologo -m:1
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath "$PSScriptRoot\README.md" -Destination $publishDirectory
Write-Output "Ready: $publishDirectory\PadReset.exe"
