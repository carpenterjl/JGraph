<#
.SYNOPSIS
    Records how R2025b builds a Production Server archive and how its in-MATLAB test server answers
    HTTP (compiler and SDK plan, stage K14). Writes recordings\k14_build.out.txt,
    k14_server.out.txt and k14_out\*.

.DESCRIPTION
    probe_k14_build runs under matlab -batch -noFigureWindows over stage K14's functions (src\k14);
    each archive it wrote is then listed (its entries and its manifest's attributes) by Python's
    zipfile into k14_out. MATLAB Production Server itself is not installed here, so the HTTP oracle
    is the server productionServerArchiveCompiler's test client uses, mps_in_matlab's serverSetup
    and serverStart, started by probe_k14_server under matlab -r and left idle at its prompt (it
    answers nothing under -batch or inside pause). src\k14client\k14client.py is run against it in
    each of its modes, twice: with CORS and discovery on, and with both off. The MATLAB process is
    ended by the id it wrote once the server listened. No figure window is shown; the probe turns
    the beep off.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Skip
    A comma-separated list of 'build' and 'server' to leave out.

.PARAMETER Port
    The port the test server listens on (the next one is used for the second session).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Skip = '',
    [int] $Port = 9951,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $Python = (Join-Path $env:LOCALAPPDATA 'Programs\Python\Python312\python.exe'),
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
    $tokens += @($tokens | Where-Object { $_[0].Contains('\') } | ForEach-Object { , @($_[0].Replace('\', '\\'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

function Run-Matlab([string] $statement, [string] $target) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    $p = Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
        -ArgumentList @('-batch', "`"$statement`"", '-noFigureWindows') `
        -RedirectStandardOutput $out -RedirectStandardError $err -NoNewWindow -PassThru
    try {
        $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { try { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null } catch { }; Start-Sleep -Seconds 2 }
        $text = [System.IO.File]::ReadAllText($out)
        $errText = [System.IO.File]::ReadAllText($err)
        if ($errText.Trim()) { $text += (($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n" }
        if ($timedOut) { $text += "`nTIMEOUT`tafter $TimeoutSeconds s" }
        $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
        [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
        Write-Host ("{0,-28} exit {1}{2}" -f $target, $p.ExitCode, $(if ($timedOut) { '  TIMEOUT' } else { '' }))
    }
    finally { Remove-Item -LiteralPath $out, $err -ErrorAction SilentlyContinue }
}

function Save([string[]] $lines, [string] $target) {
    $text = ($lines -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $target), (Tokens $text), $utf8)
    Write-Host ("{0,-28} {1} lines" -f $target, $lines.Count)
}

# One server session: MATLAB started with -r, left idle, the client run against it, MATLAB ended.
function Run-Server([int] $port, [int] $cors, [int] $discovery, [string[]] $modes, [string] $header) {
    $ready = Join-Path $WorkRoot "ready_$port.txt"
    Remove-Item -LiteralPath $ready -ErrorAction SilentlyContinue
    $log = Join-Path $WorkRoot "server_$port.log"
    $statement = "cd('$($here.Replace('\', '/'))'); probe_k14_server($port, $cors, $discovery, '$($ready.Replace('\', '/'))')"
    Start-Process -FilePath (Join-Path $MatlabRoot 'bin\matlab.exe') `
        -ArgumentList @('-nodesktop', '-nosplash', '-minimize', '-noFigureWindows', '-r', "`"$statement`"", '-logfile', $log) | Out-Null
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $ready) -and $watch.Elapsed.TotalSeconds -lt 300) { Start-Sleep -Milliseconds 500 }
    $lines = @()
    if (-not (Test-Path -LiteralPath $ready)) {
        Write-Host "server on $port never became ready"
        $lines += "== $header"
        $lines += "  NOTREADY"
        return $lines
    }
    $matlabPid = [int]([System.IO.File]::ReadAllText($ready).Trim())
    Write-Host ("server on {0} ready after {1:n1} s (pid {2})" -f $port, $watch.Elapsed.TotalSeconds, $matlabPid)
    try {
        $transcript = Join-Path $WorkRoot "client_$port.txt"
        $errors = Join-Path $WorkRoot "client_$port.err"
        $env:PYTHONIOENCODING = 'utf-8'
        $arguments = @('-I', "`"$(Join-Path $here 'src\k14client\k14client.py')`"", "http://localhost:$port", 'k14comp') + $modes
        $p = Start-Process -FilePath $Python -ArgumentList $arguments `
            -RedirectStandardOutput $transcript -RedirectStandardError $errors -NoNewWindow -PassThru -Wait
        $text = [System.IO.File]::ReadAllText($transcript, $utf8)
        $lines += "== $header"
        foreach ($l in ($text -split "`r?`n")) { if ($l.Length -gt 0) { $lines += "  $l" } }
        $lines += "  EXIT`t$($p.ExitCode)"
        Write-Host ("client {0,-24} {1} lines" -f $header, $lines.Count)
    }
    finally {
        try { Stop-Process -Id $matlabPid -Force -ErrorAction SilentlyContinue } catch { }
    }
    $lines
}

# Each archive a build wrote: its entries, then its manifest's attributes but the signature.
function List-Archives([string] $folder, [string] $outCopy) {
    $script = Join-Path $WorkRoot 'list_ctf.py'
    [System.IO.File]::WriteAllText($script, @'
import re, sys, zipfile
for path in sys.argv[1:]:
    z = zipfile.ZipFile(path)
    names = sorted(z.namelist())
    print("ENTRIES", len(names))
    for n in names:
        info = z.getinfo(n)
        print("  " + n + ("" if n.endswith("/") or info.file_size == 0 else " (%d)" % info.file_size))
    for n in names:
        if n.endswith("manifest.xml") or n.startswith("fsroot/.META/") or n.startswith("fsroot/.mps/") or "routes" in n.lower() or "signature" in n.lower():
            text = z.read(n).decode("utf-8", "replace")
            if n.endswith("manifest.xml"):
                m = re.search(r"<manifest ([^>]*)>", text)
                attrs = re.findall(r'(\S+)="([^"]*)"', m.group(1)) if m else []
                print("MANIFEST " + " ".join("%s=%s" % (k, "<SIG>" if k == "signature" else v) for k, v in attrs))
                for tag in re.findall(r"<(ctf-settings|discovery|routes|web-handler)[^>]*>.*?</\1>", text, re.S):
                    print("  tag " + tag)
                for line in re.findall(r"<(?!directory|file|manifest|/)[^>]+>", text):
                    print("  " + line[:300])
            elif not n.endswith(".mat") and not n.endswith(".prf") and not n.endswith(".mlsettings"):
                print("FILE " + n)
                for line in text.replace("\r\n", "\n").split("\n")[:80]:
                    print("  |" + line)
'@, $utf8)
    $count = 0
    foreach ($ctf in Get-ChildItem -LiteralPath $folder -Recurse -Filter *.ctf -File) {
        $key = $ctf.Directory.Name + '_' + $ctf.Name
        $text = & $Python -I $script $ctf.FullName 2>&1 | Out-String
        [System.IO.File]::WriteAllText((Join-Path $outCopy ($key + '.entries.txt')), (Tokens ($text -replace "`r`n", "`n")), $utf8)
        $count++
    }
    Write-Host "k14_out                      $count archives listed"
}

if ($skipped -notcontains 'build') {
    $build = Join-Path $WorkRoot 'b'
    Run-Matlab "cd('$here'); probe_k14_build('$build')" 'k14_build.out.txt'
    $outCopy = Join-Path $recordings 'k14_out'
    if (Test-Path $outCopy) { Remove-Item -LiteralPath $outCopy -Recurse -Force }
    New-Item -ItemType Directory $outCopy | Out-Null
    foreach ($key in 'main', 'defaults', 'signatures', 'routes', 'mcc_default') {
        $folder = Join-Path $build $key
        if (-not (Test-Path $folder)) { continue }
        foreach ($f in Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object { $_.Extension -in '.txt', '.json', '.log', '.html' }) {
            $rel = $f.FullName.Substring($folder.Length + 1).Replace('\', '_')
            [System.IO.File]::WriteAllText((Join-Path $outCopy ($key + '_' + $rel)), (Tokens ([System.IO.File]::ReadAllText($f.FullName))), $utf8)
        }
    }
    List-Archives $build $outCopy
}

# The routes file's checks against an archive its "component" names (probe_k14_build's named another).
if ($skipped -notcontains 'routes') {
    Run-Matlab "cd('$here'); probe_k14_routes('$(Join-Path $WorkRoot 'r')')" 'k14_routes.out.txt'
}

if ($skipped -notcontains 'server') {
    $lines = @()
    $lines += Run-Server $Port 1 1 @('sync', 'cors', 'api') 'cors+discovery'
    $lines += Run-Server ($Port + 1) 0 0 @('cors', 'api') 'plain'
    Save $lines 'k14_server.out.txt'
}
