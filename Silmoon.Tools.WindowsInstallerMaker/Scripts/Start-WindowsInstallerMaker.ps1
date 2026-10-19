[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$package = Get-AppxPackage -Name 'Silmoon.Tools.WindowsInstallerMaker' | Select-Object -First 1
if ($null -eq $package) { throw '请先运行同目录的 Build.ps1 -Run 完成开发构建与注册。此脚本不安装证书或更改系统设置。' }
$aumid = $package.PackageFamilyName + '!App'
Start-Process -FilePath explorer.exe -ArgumentList @('shell:AppsFolder\' + $aumid) -WindowStyle Hidden
