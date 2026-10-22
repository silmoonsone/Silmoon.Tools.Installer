[CmdletBinding()]
param([Parameter(Mandatory)][string]$ProjectFile, [Parameter(Mandatory)][string]$BuildDirectory)

$ErrorActionPreference = 'Stop'
$projectPath = [IO.Path]::GetFullPath($ProjectFile)
$config = Get-Content -LiteralPath $projectPath -Raw | ConvertFrom-Json
$publish = [IO.Path]::GetFullPath($config.publishDirectory, (Split-Path -Parent $projectPath))
$build = [IO.Path]::GetFullPath($BuildDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $build 'build-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.velopackVersion -ne '1.2.161') { throw 'This verifier models the reviewed vpk 1.2.161 exclusion rules only.' }
$package = @(Get-ChildItem -LiteralPath $build -Filter '*-full.nupkg' -File)
if ($package.Count -ne 1) { throw 'Expected exactly one full package.' }
$setup = Join-Path $build ($config.productId + '-' + $config.channel + '-Setup.exe')
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Setup missing.' }
$archive = [IO.Compression.ZipFile]::OpenRead($package[0].FullName)
$checked = [Collections.Generic.List[object]]::new()
$excluded = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
try {
    foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($publish, $file.FullName).Replace('\', '/')
        # Exact upstream defaults at commit 92d6a1c91716729d449034df5c50307dcce39493.
        # Built-in exclusions ignore case; the default manual PDB regex is case-sensitive.
        $reason = ''
        if ([regex]::IsMatch($file.FullName, '.*[\\\/]createdump.*|.*\.vshost\..*|.*\.nupkg$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)) { $reason = 'Velopack built-in exclusion' }
        elseif ([regex]::IsMatch($file.FullName, '.*\.pdb')) { $reason = 'Velopack default --exclude' }
        if ($reason) { $excluded.Add([ordered]@{ file = $relative; bytes = $file.Length; reason = $reason }); continue }
        $entry = $archive.GetEntry('lib/app/' + $relative)
        if ($null -eq $entry) { $failures.Add("Missing: $relative"); continue }
        $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $stream = $entry.Open()
        try { $packageHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($sourceHash -ne $packageHash) { $failures.Add("Hash mismatch: $relative") }
        $checked.Add([ordered]@{ file = $relative; bytes = $file.Length; sha256 = $sourceHash; matches = $sourceHash -eq $packageHash })
    }
} finally { $archive.Dispose() }
$setupInfo = Get-Item -LiteralPath $setup
$report = [ordered]@{
    verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    projectFile = $projectPath
    setup = $setupInfo.FullName
    setupBytes = $setupInfo.Length
    setupSha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
    checkedFileCount = $checked.Count
    excludedDefaultFiles = $excluded.ToArray()
    exclusionSourceCommit = '92d6a1c91716729d449034df5c50307dcce39493'
    failures = $failures.ToArray()
    files = $checked.ToArray()
    installedOrLaunchedTargetApplication = $false
}
$output = Join-Path $build 'package-content-verification.json'
[IO.File]::WriteAllText($output, (($report | ConvertTo-Json -Depth 8) -replace '\r?\n', "`r`n") + "`r`n", [Text.UTF8Encoding]::new($true))
if ($failures.Count) { throw "Package verification failed: $($failures -join '; ')" }
Write-Output "Verified $($checked.Count) published files. Setup: $($setupInfo.Length) bytes. Report: $output"
