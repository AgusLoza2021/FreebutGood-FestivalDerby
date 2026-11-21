<#
.SYNOPSIS
  Runs Unity in batchmode for this project and prints a short summary that is easy to check.
  Built for AI agents without a harness: run one command, read the RESULT line.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action test -Platform EditMode
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action test -Platform PlayMode
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action compile
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/unity.ps1 -Action exec -Method MyEditorClass.Run

.NOTES
  Exit codes: 0 PASS, 1 FAIL, 2 bad usage, 3 BLOCKED (an Editor has this project open),
  4 TIMEOUT, 5 Unity not found.
  Known test failures that do not count as new live in tools/known-test-failures.txt.
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('test', 'compile', 'exec')][string]$Action,
    [ValidateSet('EditMode', 'PlayMode')][string]$Platform = 'EditMode',
    [string]$Method = '',
    [string]$TestFilter = '',
    [string]$Grep = '\[agent\]|Exception',
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$started = Get-Date

function Show-List([string]$title, $items, [int]$max = 20) {
    $list = @($items)
    Write-Output ("{0}: {1}" -f $title, $list.Count)
    $list | Select-Object -First $max | ForEach-Object { Write-Output ("  - " + $_) }
    if ($list.Count -gt $max) { Write-Output ("  ... and {0} more (see log)" -f ($list.Count - $max)) }
}

# Resolve the Editor that matches ProjectVersion.txt, unless UNITY_EXE overrides it.
$unity = $env:UNITY_EXE
if (-not $unity) {
    $versionLine = Get-Content (Join-Path $repo 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -like 'm_EditorVersion:*' } | Select-Object -First 1
    $version = ($versionLine -split ':', 2)[1].Trim()
    $unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
}
if (-not (Test-Path $unity)) {
    Write-Output "RESULT: ERROR - Unity not found at '$unity'. Set the UNITY_EXE environment variable."
    exit 5
}

# Batchmode cannot open a project that an Editor already has open; say so plainly instead of hanging.
$repoKey = $repo.ToLower().Replace('/', '\')
$owner = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine.ToLower().Replace('/', '\').Contains($repoKey) }
$lock = Join-Path $repo 'Temp\UnityLockfile'
$locked = $false
if (Test-Path $lock) {
    try { $fs = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None'); $fs.Close() } catch { $locked = $true }
}
if ($owner -or $locked) {
    Write-Output "RESULT: BLOCKED - a Unity Editor has this project open. Close it (or ask the owner to), then run this again."
    exit 3
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $repo "Logs\agent-unity\$stamp-$Action"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$log = Join-Path $outDir 'unity.log'
$xml = Join-Path $outDir 'results.xml'

$unityArgs = @('-batchmode', '-projectPath', $repo, '-logFile', $log)
switch ($Action) {
    'test' {
        # No -quit here: the test runner exits by itself, and -quit would stop it before it runs.
        $unityArgs += @('-runTests', '-testPlatform', $Platform, '-testResults', $xml)
        if ($TestFilter) { $unityArgs += @('-testFilter', $TestFilter) }
    }
    'compile' { $unityArgs += '-quit' }
    'exec' {
        if (-not $Method) { Write-Output 'RESULT: ERROR - exec needs -Method Namespace.Class.StaticMethod'; exit 2 }
        $unityArgs += @('-quit', '-executeMethod', $Method)
    }
}
# Windows PowerShell 5.1 joins ArgumentList with spaces without quoting, so quote anything with a space.
$quoted = $unityArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }

$label = $Action
if ($Action -eq 'test') { $label = "test $Platform" }
Write-Output "ACTION: $label"
Write-Output "LOG: $log"
Write-Output "Running Unity (can take 5-20 minutes, do not start another run meanwhile)..."

$p = Start-Process -FilePath $unity -ArgumentList $quoted -PassThru -WindowStyle Hidden
$null = $p.Handle  # keeps ExitCode readable after exit in Windows PowerShell 5.1
if (-not $p.WaitForExit($TimeoutMinutes * 60000)) {
    try { $p.Kill() } catch { }
    Write-Output "RESULT: TIMEOUT - Unity did not finish in $TimeoutMinutes minutes. Check the log tail."
    exit 4
}
$exitCode = $p.ExitCode
Write-Output ("UNITY_EXIT: {0}  ELAPSED: {1:mm\:ss}" -f $exitCode, ((Get-Date) - $started))

$logLines = @()
if (Test-Path $log) { $logLines = Get-Content $log }
$compileErrors = $logLines | Select-String -Pattern 'error CS\d+' | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique
$missingRefs = $logLines | Select-String -Pattern 'referenced script .* is missing|Missing Prefab|missing script' | ForEach-Object { $_.Line.Trim() } | Sort-Object -Unique
Show-List 'COMPILE_ERRORS' $compileErrors
Show-List 'MISSING_REFS' $missingRefs 10

$pass = (@($compileErrors).Count -eq 0)

if ($Action -eq 'test') {
    if (-not (Test-Path $xml)) {
        Write-Output 'TESTS: no results file (compile error, crash or nothing ran). See the log.'
        $pass = $false
    }
    else {
        [xml]$doc = Get-Content -Raw -Path $xml
        $run = $doc.'test-run'
        Write-Output ("TESTS: total={0} passed={1} failed={2} skipped={3}" -f $run.total, $run.passed, $run.failed, $run.skipped)
        $knownFile = Join-Path $PSScriptRoot 'known-test-failures.txt'
        $known = @()
        if (Test-Path $knownFile) { $known = Get-Content $knownFile | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } }
        $failed = @($doc.SelectNodes("//test-case[@result='Failed']"))
        $knownHit = @($failed | Where-Object { $known -contains $_.fullname } | ForEach-Object { $_.fullname })
        $newFail = @($failed | Where-Object { $known -notcontains $_.fullname } | ForEach-Object {
                $msg = ''
                if ($_.failure -and $_.failure.message) { $msg = (($_.failure.message.InnerText -split "`n")[0]).Trim() }
                "{0} :: {1}" -f $_.fullname, $msg
            })
        Show-List 'KNOWN_FAILURES' $knownHit
        Show-List 'NEW_FAILURES' $newFail
        if ($newFail.Count -gt 0) { $pass = $false }
        if ([int]$run.total -eq 0) { Write-Output 'TESTS: zero tests ran - check -TestFilter / -Platform.'; $pass = $false }
    }
}
else {
    if ($exitCode -ne 0) { $pass = $false }
    if ($Action -eq 'exec') {
        $hits = $logLines | Select-String -Pattern $Grep | ForEach-Object { $_.Line.Trim() }
        Show-List "LOG_MATCHES /$Grep/" $hits 40
    }
}

if ($pass) { Write-Output 'RESULT: PASS'; exit 0 }
Write-Output 'RESULT: FAIL'
exit 1
