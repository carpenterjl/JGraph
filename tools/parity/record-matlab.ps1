<#
.SYNOPSIS
    Records what MATLAB R2024a prints for each parity fixture, so the test suite can compare against
    it without MATLAB present.

.DESCRIPTION
    A parity fixture is a MATLAB-dialect script under tests/JGraph.Tests/MatlabParity/fixtures that
    prints lines of the form

        CHK|<name>|<value>|<rule>

    where <rule> is one of: exact, shape, rel=<tol>, abs=<tol>, div=ADR<nnnn>. This script runs
    each fixture through MATLAB headless (matlab.exe -batch), keeps ONLY the CHK lines, and writes
    them as UTF-8 without a BOM to tests/JGraph.Tests/MatlabParity/expected/<fixture>.txt. Everything
    else MATLAB prints — [Warning: ...] blocks, the licence banner, a UTF-16 BOM from PowerShell's
    own redirection — is dropped on the floor, which is why the recorder exists rather than a `>`.

    It also writes expected/matlab_version.txt from `version`, so the recording says which MATLAB
    it is a recording of.

    This is the ONLY step in the parity suite that needs MATLAB. It is run once per new fixture,
    and again when a fixture changes; its output is committed. The xunit test
    (MatlabParityFixtureTests) never runs MATLAB. MATLAB's first call is 30-70x slower than its warm
    one, so nothing here is a timing.

.PARAMETER Fixtures
    Fixture names (without .m) to record. Omit to record all of them.

.PARAMETER MatlabExe
    The MATLAB launcher. Defaults to E:\Matlab\bin\matlab.exe.

.EXAMPLE
    powershell -File tools/parity/record-matlab.ps1
    powershell -File tools/parity/record-matlab.ps1 -Fixtures m124_ode45,m124_integral
#>
[CmdletBinding()]
param(
    [string[]] $Fixtures,
    [string] $MatlabExe = 'E:\Matlab\bin\matlab.exe'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixtureDir = Join-Path $repo 'tests\JGraph.Tests\MatlabParity\fixtures'
$expectedDir = Join-Path $repo 'tests\JGraph.Tests\MatlabParity\expected'

if (-not (Test-Path -LiteralPath $MatlabExe)) {
    Write-Error "MATLAB not found at '$MatlabExe'."
}
if (-not (Test-Path -LiteralPath $expectedDir)) {
    New-Item -ItemType Directory -Path $expectedDir | Out-Null
}

$all = Get-ChildItem -LiteralPath $fixtureDir -Filter '*.m' | ForEach-Object { $_.BaseName }
if ($Fixtures) {
    foreach ($f in $Fixtures) {
        if ($all -notcontains $f) { Write-Error "no fixture named '$f' in $fixtureDir" }
    }
    $all = $Fixtures
}

# MATLAB writes its stdout in the console code page; PowerShell's redirection may prepend a BOM.
# ReadAllText honours a BOM when there is one and assumes UTF-8 when there is not; every CHK line
# is ASCII, so either way the lines come back intact.
function Invoke-Matlab {
    param([string] $Statement, [string] $WorkingDirectory)

    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        # Not -Wait: in Windows PowerShell 5.1 that waits for every descendant too, and MATLAB.exe
        # leaves MathWorksServiceHost running, so a -Wait never returns. WaitForExit waits for the
        # launcher alone, which itself waits for MATLAB.exe.
        $proc = Start-Process -FilePath $MatlabExe -ArgumentList @('-batch', "`"$Statement`"") `
            -WorkingDirectory $WorkingDirectory -PassThru -NoNewWindow `
            -RedirectStandardOutput $out -RedirectStandardError $err
        $null = $proc.Handle   # without this PS 5.1 reports a null ExitCode
        $proc.WaitForExit()
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        [pscustomobject]@{ Text = $text; Error = $errText; Exit = $proc.ExitCode }
    } finally {
        Remove-Item -LiteralPath $out, $err -Force -ErrorAction SilentlyContinue
    }
}

$fixtureDirMatlab = $fixtureDir -replace "'", "''"
$version = Invoke-Matlab -Statement "fprintf('%s\n', version)" -WorkingDirectory $fixtureDir
$versionLine = ($version.Text -split "`r?`n" | Where-Object { $_ -match '\(R\d{4}[ab]\)' } | Select-Object -First 1)
if (-not $versionLine) { Write-Error "could not read MATLAB's version: $($version.Text) $($version.Error)" }
[System.IO.File]::WriteAllText((Join-Path $expectedDir 'matlab_version.txt'), ($versionLine.Trim() + "`n"),
    (New-Object System.Text.UTF8Encoding $false))
Write-Host "MATLAB: $($versionLine.Trim())"

$failed = 0
foreach ($name in $all) {
    $result = Invoke-Matlab -Statement "cd('$fixtureDirMatlab'); $name" -WorkingDirectory $fixtureDir
    $lines = @($result.Text -split "`r?`n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_ -match '^CHK\|' })

    if ($result.Exit -ne 0 -or $lines.Count -eq 0) {
        $failed++
        Write-Host ("  {0,-28} FAILED (exit {1}, {2} CHK lines)" -f $name, $result.Exit, $lines.Count)
        $shown = ($result.Text + $result.Error) -split "`r?`n" | Where-Object { $_ -and $_ -notmatch '^CHK\|' } | Select-Object -Last 12
        foreach ($l in $shown) { Write-Host "      $l" }
        continue
    }

    $path = Join-Path $expectedDir ($name + '.txt')
    [System.IO.File]::WriteAllText($path, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
    Write-Host ("  {0,-28} {1,4} CHK lines -> expected\{2}.txt" -f $name, $lines.Count, $name)
}

if ($failed -gt 0) {
    Write-Error "$failed fixture(s) did not record."
}
