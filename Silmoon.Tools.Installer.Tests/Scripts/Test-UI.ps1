[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$ProjectFile,
    [string]$ArtifactDirectory = (Join-Path $PSScriptRoot '../artifacts/ui'),
    [switch]$BuildDemo
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$workflow = [Guid]::NewGuid().ToString('N')
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = $workflow
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
$projectPath = [IO.Path]::GetFullPath($ProjectFile)
$setupPath = ''
$hwnd = ''
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

function Invoke-Checked([string[]]$Arguments) {
    $global:LASTEXITCODE = $null
    $output = @(& winapp @Arguments)
    if ($null -eq $global:LASTEXITCODE -or $global:LASTEXITCODE -ne 0) { throw "winapp failed ($global:LASTEXITCODE): $($Arguments -join ' ') $($output -join "`n")" }
    $output
}
function Invoke-UI([string[]]$Arguments) { Invoke-Checked (@('ui') + $Arguments + @('-a', "$AppPid")) }
function Test-UI([string]$Name, [scriptblock]$Action) {
    try { & $Action | Out-Null; $results.Add(@{ name = $Name; passed = $true }); Write-Output "PASS $Name" }
    catch { $results.Add(@{ name = $Name; passed = $false; error = "$_" }); throw }
}
function Screenshot([string]$Name) {
    $path = Join-Path $artifactRoot $Name
    if (Test-Path -LiteralPath $path) { throw "Evidence path already exists: $path" }
    Invoke-Checked @('ui', 'screenshot', '-w', $hwnd, '--output', $path, '--json') | Out-Null
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Screenshot was not delivered.' }
    $screenshots.Add($path)
}

try {
    $windows = @(Invoke-UI @('list-windows', '--json') | ConvertFrom-Json)
    $main = $windows | Where-Object { $_.hwnd -and $_.title -eq 'Silmoon.Tools.Installer.WindowsMaker' } | Select-Object -First 1
    if ($null -eq $main) { throw 'Expected new tool main window was not found.' }
    $hwnd = [string]$main.hwnd
    Test-UI 'Initial native WinUI window' {
        Invoke-UI @('wait-for', 'ConfigPath', '-t', '5000')
        Screenshot '01-initial.png'
    }
    Test-UI 'Load independent demo config through GUI' {
        Invoke-UI @('set-value', 'ConfigPath', $projectPath)
        Invoke-UI @('invoke', 'OpenPathButton')
        Invoke-UI @('wait-for', 'MainExecutable', '--value', 'Silmoon.Trading.Client.WindowsClient.exe', '-t', '5000')
    }
    Test-UI 'Analyze real publish without launching client' {
        Invoke-UI @('invoke', 'AnalyzeButton')
        Invoke-UI @('wait-for', 'AnalysisResults', '--value', '1.2.161', '--contains', '-t', '20000')
        Screenshot '02-analysis.png'
    }
    Test-UI 'Edit version and save from GUI' {
        Invoke-UI @('invoke', 'NavProduct')
        Invoke-UI @('wait-for', 'ProductId', '--value', 'Silmoon.Trading.Client.WindowsClient', '-t', '5000')
        Invoke-UI @('set-value', 'Version', '0.1.0-preview.1')
        Invoke-UI @('set-value', 'Version', '0.1.0-preview.2')
        Invoke-UI @('wait-for', 'DocumentState', '--value', '有未保存修改', '-t', '3000')
        Invoke-UI @('invoke', 'SaveButton')
        Invoke-UI @('wait-for', 'DocumentState', '--value', '已保存 / 无修改', '-t', '5000')
        $saved = Get-Content -LiteralPath $projectPath -Raw | ConvertFrom-Json
        if ($saved.version -ne '0.1.0-preview.2' -or $saved.productId -ne 'Silmoon.Trading.Client.WindowsClient') { throw 'GUI save did not persist edited version and stable identity.' }
        Invoke-UI @('wait-for', 'Architecture', '--value', 'x64', '-t', '3000')
        Invoke-UI @('wait-for', 'InstallScope', '--value', '当前用户', '-t', '3000')
        Screenshot '03-product-saved.png'
    }
    Test-UI 'New then reopen saved configuration' {
        Invoke-UI @('invoke', 'NewButton')
        Invoke-UI @('wait-for', 'Version', '--value', '1.0.0', '-t', '5000')
        Invoke-UI @('set-value', 'ConfigPath', $projectPath)
        Invoke-UI @('invoke', 'OpenPathButton')
        Invoke-UI @('wait-for', 'Version', '--value', '0.1.0-preview.2', '-t', '5000')
    }
    Test-UI 'Unsaved changes dialog allows cancel without data loss' {
        Invoke-UI @('set-value', 'DisplayName', 'Silmoon Trading temporary UI test')
        Invoke-UI @('invoke', 'NewButton')
        Invoke-UI @('wait-for', 'UnsavedDialog', '-t', '5000')
        Screenshot '04-unsaved-dialog.png'
        Invoke-UI @('invoke', 'CloseButton')
        Invoke-UI @('wait-for', 'UnsavedDialog', '--gone', '-t', '5000')
        Invoke-UI @('wait-for', 'DisplayName', '--value', 'Silmoon Trading temporary UI test', '-t', '5000')
        Invoke-UI @('set-value', 'DisplayName', 'Silmoon Trading')
        Invoke-UI @('invoke', 'SaveButton')
        Invoke-UI @('wait-for', 'DocumentState', '--value', '已保存 / 无修改', '-t', '5000')
    }
    Test-UI 'Dependency modes and offline limitation visible' {
        Invoke-UI @('invoke', 'NavDependencies')
        Invoke-UI @('wait-for', 'DependenciesJson', '--value', 'offlineSidecar', '--contains', '-t', '5000')
        Invoke-UI @('wait-for', 'DependencyKind', '--value', 'WebView2 Runtime', '-t', '3000')
        Invoke-UI @('wait-for', 'DependencyDelivery', '--value', '完整离线安装器', '--contains', '-t', '3000')
        Screenshot '05-dependencies.png'
    }
    Test-UI 'Distribution settings render with support boundaries' {
        Invoke-UI @('invoke', 'NavDistribution')
        Invoke-UI @('wait-for', 'SigningMode', '-t', '5000')
        Invoke-UI @('wait-for', 'DistributionMode', '--value', '完整应用安装包', '-t', '3000')
        Invoke-UI @('wait-for', 'SigningMode', '--value', '不签名', '-t', '3000')
        Screenshot '06-distribution.png'
    }
    Test-UI 'Invalid configuration blocks GUI build and clears result' {
        Invoke-UI @('invoke', 'NavProduct')
        Invoke-UI @('set-value', 'Version', 'not-a-version')
        Invoke-UI @('invoke', 'BuildButton')
        Invoke-UI @('wait-for', 'BuildStatus', '--value', '构建失败', '-t', '10000')
        $value = Invoke-UI @('get-value', 'SetupPath', '--json') | ConvertFrom-Json
        if ($value.text) { throw 'Failed build displayed stale installer.' }
        Screenshot '07-invalid-build.png'
        Invoke-UI @('invoke', 'NavProduct')
        Invoke-UI @('set-value', 'Version', '0.1.0-preview.1')
        Invoke-UI @('set-value', 'Version', '0.1.0-preview.2')
        Invoke-UI @('invoke', 'SaveButton')
        Invoke-UI @('wait-for', 'DocumentState', '--value', '已保存 / 无修改', '-t', '5000')
    }
    if ($BuildDemo) {
        Test-UI 'GUI invokes real vpk and returns verified Setup' {
            Invoke-UI @('invoke', 'BuildButton')
            Invoke-UI @('wait-for', 'CancelBuildButton', '-p', 'IsEnabled', '--value', 'True', '-t', '10000')
            Screenshot '08-building.png'
            Invoke-UI @('wait-for', 'BuildStatus', '--value', '成功', '--contains', '-t', '60000')
            $value = Invoke-UI @('get-value', 'SetupPath', '--json') | ConvertFrom-Json
            $script:setupPath = $value.text
            if (-not $script:setupPath -or -not (Test-Path -LiteralPath $script:setupPath -PathType Leaf)) { throw 'Success did not return an existing Setup.' }
            Screenshot '09-build-success.png'
        }
    }
    Test-UI 'Interactive source controls have AutomationIds' {
        Invoke-UI @('invoke', 'NavProduct')
        $tree = Invoke-UI @('inspect', '--interactive', '--json') | ConvertFrom-Json
        function Nodes($items) { foreach($item in $items) { if ($null -ne $item) { $item; Nodes $item.children } } }
        $elements = @(Nodes @($tree.windows | ForEach-Object { $_.elements }))
        if ($elements.Count -eq 0) { throw 'No accessibility nodes returned.' }
        $required = @('ProductId','DisplayName','Publisher','Version','Architecture','Channel')
        foreach ($id in $required) { if (-not ($elements | Where-Object { $_.automationId -eq $id })) { throw "Missing accessible field: $id" } }
    }
    Invoke-UI @('invoke', 'NavBuild') | Out-Null
} catch {
    $results.Add(@{ name = 'UI batch'; passed = $false; error = "$_" })
} finally {
    try { Invoke-Checked @('ui', 'yield') | Out-Null }
    finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
    $failed = @($results | Where-Object { -not $_.passed }).Count
    $report = [ordered]@{ target = 'local'; appPid = $AppPid; workflowId = $workflow; projectFile = $projectPath; passed = $results.Count - $failed; failed = $failed; setupPath = $setupPath; results = $results.ToArray(); screenshots = $screenshots.ToArray() }
    [IO.File]::WriteAllText((Join-Path $artifactRoot 'ui-results.json'), (($report | ConvertTo-Json -Depth 8) -replace '\r?\n', "`r`n") + "`r`n", [Text.UTF8Encoding]::new($true))
}
Write-Output "UI passed: $($report.passed); failed: $($report.failed). $artifactRoot"
if ($report.failed) { exit 1 }
