[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [switch]$Run)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repo 'Silmoon.Tools.WindowsInstallerMaker\Silmoon.Tools.WindowsInstallerMaker.csproj'
& dotnet build (Join-Path $repo 'Silmoon.Tools.Installer.slnx') -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
& dotnet run --project (Join-Path $repo 'Silmoon.Tools.WindowsInstallerMaker.Tests') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Core tests failed: $LASTEXITCODE" }
if ($Run) {
    & winapp run $project -c $Configuration --arch x64 --no-build --detach --json
    if ($LASTEXITCODE -ne 0) { throw "Launch failed: $LASTEXITCODE" }
}
