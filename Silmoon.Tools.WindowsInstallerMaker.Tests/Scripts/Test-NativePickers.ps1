[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$ProjectFile, [Parameter(Mandatory)][string]$ArtifactDirectory)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [Guid]::NewGuid().ToString('N')
$projectPath = [IO.Path]::GetFullPath($ProjectFile)
$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$copyPath = Join-Path $artifactRoot 'roundtrip-copy.installer.json'
$results = [Collections.Generic.List[object]]::new()

function Invoke-Checked([string[]]$Arguments) {
    $global:LASTEXITCODE = $null
    $output = @(& winapp @Arguments)
    if ($null -eq $global:LASTEXITCODE -or $global:LASTEXITCODE -ne 0) { throw "winapp failed ($global:LASTEXITCODE): $($Arguments -join ' ') $($output -join "`n")" }
    $output
}
function Nodes($items) { foreach($item in $items) { if ($null -ne $item) { $item; Nodes $item.children } } }
function Pick-File([string]$Command, [string]$Path) {
    Invoke-Checked @('ui','invoke',$Command,'-a',"$AppPid") | Out-Null
    $picker = $null
    for($attempt = 0; $attempt -lt 15; $attempt++) {
        $windows = @(Invoke-Checked @('ui','list-windows','-a',"$AppPid",'--json') | ConvertFrom-Json)
        $picker = $windows | Where-Object { $_.className -eq '#32770' -and $_.ownerHwnd } | Select-Object -First 1
        if ($picker) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $picker) { throw 'Expected native picker did not open.' }
    $tree = Invoke-Checked @('ui','inspect','-w',"$($picker.hwnd)",'--interactive','--json') | ConvertFrom-Json
    $nodes = @(Nodes @($tree.windows | ForEach-Object { $_.elements }))
    $fileName = $nodes | Where-Object { $_.automationId -eq 'FileNameControlHost' -or ($_.type -eq 'Edit' -and $_.automationId -eq '1148') } | Select-Object -First 1
    if (-not $fileName) { throw 'Native picker filename field not found.' }
    Invoke-Checked @('ui','set-value',$fileName.selector,$Path,'-w',"$($picker.hwnd)") | Out-Null
    $submit = $nodes | Where-Object { $_.type -eq 'Button' -and $_.automationId -eq '1' } | Select-Object -First 1
    if (-not $submit) { throw 'Native picker submit button not found.' }
    Invoke-Checked @('ui','invoke',$submit.selector,'-w',"$($picker.hwnd)") | Out-Null
    Invoke-Checked @('ui','wait-for','ConfigPath','--value',$Path,'-t','10000','-a',"$AppPid") | Out-Null
}

function Select-Architecture([string]$Value) {
    Invoke-Checked @('ui','invoke','Architecture','-a',"$AppPid") | Out-Null
    $search = Invoke-Checked @('ui','search',$Value,'-a',"$AppPid",'--json') | ConvertFrom-Json
    $option = $search.matches | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $Value } | Select-Object -First 1
    if (-not $option) { throw "Architecture option missing: $Value" }
    Invoke-Checked @('ui','invoke',$option.selector,'-a',"$AppPid") | Out-Null
}
try {
    Pick-File 'OpenButton' $projectPath
    $results.Add(@{ name = 'Native Open loads demo configuration'; passed = $true })
    Pick-File 'SaveAsButton' $copyPath
    $original = Get-Content -LiteralPath $projectPath -Raw | ConvertFrom-Json
    $copy = Get-Content -LiteralPath $copyPath -Raw | ConvertFrom-Json
    foreach ($name in @('publishDirectory','outputDirectory','releaseNotesPath')) {
        $left = [IO.Path]::GetFullPath($original.$name, (Split-Path -Parent $projectPath))
        $right = [IO.Path]::GetFullPath($copy.$name, (Split-Path -Parent $copyPath))
        if ($left -ne $right) { throw "Save As changed $name target." }
    }
    if ($copy.productId -ne $original.productId -or $copy.version -ne $original.version) { throw 'Save As changed identity/version.' }
    $results.Add(@{ name = 'Native Save As and relative-path rebase'; passed = $true })
    Pick-File 'OpenButton' $projectPath
    $results.Add(@{ name = 'Native Open reloads original configuration'; passed = $true })
    Invoke-Checked @('ui','invoke','NavProduct','-a',"$AppPid") | Out-Null
    Select-Architecture 'x86'
    Invoke-Checked @('ui','invoke','SaveButton','-a',"$AppPid") | Out-Null
    Invoke-Checked @('ui','wait-for','DocumentState','--value','已保存 / 无修改','-t','5000','-a',"$AppPid") | Out-Null
    if ((Get-Content -LiteralPath $projectPath -Raw | ConvertFrom-Json).architecture -ne 'x86') { throw 'Architecture choice did not update model.' }
    Select-Architecture 'x64'
    Invoke-Checked @('ui','invoke','SaveButton','-a',"$AppPid") | Out-Null
    Invoke-Checked @('ui','wait-for','DocumentState','--value','已保存 / 无修改','-t','5000','-a',"$AppPid") | Out-Null
    if ((Get-Content -LiteralPath $projectPath -Raw | ConvertFrom-Json).architecture -ne 'x64') { throw 'Architecture was not restored.' }
    $results.Add(@{ name = 'Architecture selection updates saved enum and restores x64'; passed = $true })
} catch { $results.Add(@{ name = 'Native picker/selection test'; passed = $false; error = "$_" }) }
finally {
    try { Invoke-Checked @('ui','yield') | Out-Null }
    finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
    $report = [ordered]@{ target = 'local'; appPid = $AppPid; passed = @($results | Where-Object passed).Count; failed = @($results | Where-Object { -not $_.passed }).Count; results = $results.ToArray() }
    [IO.File]::WriteAllText((Join-Path $artifactRoot 'picker-results.json'), (($report | ConvertTo-Json -Depth 8) -replace '\r?\n', "`r`n") + "`r`n", [Text.UTF8Encoding]::new($true))
}
$report | ConvertTo-Json -Depth 6
if ($report.failed) { exit 1 }
