<#
.SYNOPSIS
    Records what R2025b's .NET assembly target does (compiler and SDK plan, stage K8): the builds
    compiler.build.dotNETAssembly and mcc -W dotnet make, what DotNETAssemblyOptions answers and
    refuses, the files the main build generates, and how the MWArray API and the generated classes
    answer a .NET client. Writes recordings\k8_build.out.txt, k8_client.out.txt and k8_out\*.

.DESCRIPTION
    probe_k8_build builds src\k8 under matlab -batch -noFigureWindows. Its main build (dn\K8, two
    classes, .NET 5.0) is what k8client (src\k8client) is built against with dotnet and run through
    run-quiet.ps1. The main build's generated text files are copied into recordings\k8_out with the
    paths and the date replaced by tokens. No window is shown: k8quiet turns the beep off first.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'build' and 'client' to leave out.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [int] $TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$recordings = Join-Path $here 'recordings'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','

function Tokens([string] $text) {
    $tokens = @(@($WorkRoot, '<W>'), @($MatlabRoot, '<MATLABROOT>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

function Run-Matlab([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
            -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
            -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
        $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { & taskkill.exe /PID $p.Id /T /F | Out-Null; Start-Sleep -Seconds 2 }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
        [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
        Write-Host ("{0,-24} exit {1}{2}" -f $target, $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

if ($skipped -notcontains 'build') {
    Run-Matlab "cd('$here'); probe_k8_build('$WorkRoot\b')" 'k8_build.out.txt'
    # The main build's generated text, as golden files for JGraph's generator (the date tokenized).
    $outCopy = Join-Path $recordings 'k8_out'
    if (Test-Path $outCopy) { Remove-Item -LiteralPath $outCopy -Recurse -Force }
    New-Item -ItemType Directory $outCopy | Out-Null
    $built = Join-Path $WorkRoot 'b\dn\K8'
    foreach ($f in Get-ChildItem -LiteralPath $built -File | Where-Object { $_.Extension -in '.cs', '.xml', '.html', '.txt', '.csproj', '.json', '.log' }) {
        $text = [System.IO.File]::ReadAllText($f.FullName)
        $text = [regex]::Replace($text, '\* Date: [^\r\n]*', '* Date: <DATE>')
        [System.IO.File]::WriteAllText((Join-Path $outCopy $f.Name), (Tokens $text), $utf8)
    }
    Write-Host "k8_out                   $((Get-ChildItem $outCopy).Count) files"
}

if ($skipped -notcontains 'client') {
    $lines = New-Object System.Collections.Generic.List[string]
    $clientSrc = Join-Path $WorkRoot 'clientsrc'
    if (Test-Path $clientSrc) { Remove-Item -LiteralPath $clientSrc -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $here 'src\k8client') -Destination $clientSrc -Recurse
    $buildLog = Join-Path $WorkRoot 'client_build.txt'
    & dotnet build (Join-Path $clientSrc 'k8client.csproj') -c Release -o (Join-Path $WorkRoot 'client') `
        "-p:K8Out=$WorkRoot\b\dn\K8" "-p:MatlabRoot=$MatlabRoot" -nologo *> $buildLog
    $lines.Add("BUILD`texit $LASTEXITCODE")
    & dotnet build-server shutdown *> $null
    $exe = Join-Path $WorkRoot 'client\k8client.exe'
    if (Test-Path $exe) {
        $transcript = [System.IO.Path]::GetTempFileName()
        $quiet = @{ Exe = $exe; Environment = @{ MCR_CACHE_ROOT = (Join-Path $WorkRoot 'cache_client') };
            TimeoutSeconds = 900; OutFile = $transcript; WorkingDirectory = (Join-Path $WorkRoot 'client') }
        & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
        $out = [System.IO.File]::ReadAllText($transcript)
        Remove-Item -LiteralPath $transcript
        foreach ($l in ($out -split "`r?`n")) { if ($l.Trim()) { $lines.Add("  $l") } }
    }
    else { $lines.Add("  no program (see $buildLog)") }
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings 'k8_client.out.txt'), (Tokens $text), $utf8)
    Write-Host "k8_client.out.txt        done"
}
