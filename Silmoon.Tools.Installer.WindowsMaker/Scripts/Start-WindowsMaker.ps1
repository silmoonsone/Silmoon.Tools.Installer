[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'Silmoon.Tools.Installer.WindowsMaker.csproj'
& winapp run $project -c $Configuration --arch x64 -p Platform=x64 --no-build --detach --json
if ($LASTEXITCODE -ne 0) { throw "Launch failed: $LASTEXITCODE. 请先运行同目录的 Build.ps1 完成构建。" }
