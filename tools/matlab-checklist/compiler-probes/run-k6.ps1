<#
.SYNOPSIS
    Builds stage K6's programs with JGraph's mcc and compiler.build, runs them on a published JGraph
    Runtime, and records what they did the way record-k6.ps1 recorded R2025b's (compiler and SDK
    plan, stage K6): recordings-jgraph\k6_build.out.txt, k6_pe.out.txt, k6_console.out.txt,
    k6_windows.out.txt, and k6_differences.txt, the console lines that differ from R2025b's.

.DESCRIPTION
    Steps (each can be left out with -Skip):

      build     build_k6_jgraph.m under the runtime's jgraph -batch, over a copy of src\k6
      pe        pe-resources.py over every program built
      console   the console programs through run-quiet.ps1, then the differences from
                recordings\k6_console.out.txt (the SECONDS lines aside)
      windows   the windowed programs and the console ones that show a window, watched by
                k6-windows.ps1. These are JGraph windows; each is closed through UI Automation
                patterns only.

.PARAMETER WorkRoot
    A scratch folder outside the repo. The builds go in <WorkRoot>\b.

.PARAMETER Runtime
    A JGraph Runtime laid out by installer\publish-runtime.ps1; the programs find it through
    JGRAPH_RUNTIME_ROOT.

.PARAMETER Mlapp
    k6_app.mlapp as probe_k6_build saved it with R2025b's serializer (its build folder's src\mlapp);
    without it the .mlapp cases are not built.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [Parameter(Mandatory = $true)] [string] $Runtime,
    [string] $Mlapp,
    [string] $Skip = '',
    [string] $Only = '',
    [string] $Python = 'C:\Users\Carpe\AppData\Local\Programs\Python\Python312\python.exe'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $here '..\..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$Runtime = (Resolve-Path -LiteralPath $Runtime).Path
$build = Join-Path $WorkRoot 'b'
$recordings = Join-Path $here 'recordings-jgraph'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$skipped = $Skip -split ','
$onlyCases = @($Only -split ',' | Where-Object { $_ })
$env:JGRAPH_RUNTIME_ROOT = $Runtime

function Tokens([string] $text) {
    $tokens = @(@($build, '<B>'), @($WorkRoot, '<W>'), @($Runtime, '<RUNTIME>'), @($repoRoot, '<REPO>'),
        @([System.IO.Path]::GetTempPath().TrimEnd('\'), '<TEMP>'), @($env:USERPROFILE, '<HOME>'))
    $tokens += @($tokens | ForEach-Object { , @($_[0].Replace('\', '/'), $_[1]) })
    $tokens = $tokens | Sort-Object { - $_[0].Length }
    foreach ($t in $tokens) { $text = $text.Replace($t[0], $t[1]) }
    $text = [regex]::Replace($text, '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', '<UUID>')
    $text
}

function Write-Recording([string] $name, [string] $text) {
    $text = ($text -replace "`r`n", "`n").TrimEnd("`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $recordings $name), (Tokens $text), $utf8)
    Write-Host ("{0,-24} written" -f $name)
}

function Run-Quiet([string] $exe, [string[]] $arguments, [hashtable] $environment) {
    $transcript = [System.IO.Path]::GetTempFileName()
    $json = ConvertTo-Json -InputObject @($arguments) -Compress
    $quiet = @{ Exe = $exe; ArgumentsBase64 = [Convert]::ToBase64String($utf8.GetBytes($json)); Environment = $environment;
        TimeoutSeconds = 120; OutFile = $transcript }
    & (Join-Path $here 'run-quiet.ps1') @quiet *> $null
    $out = [System.IO.File]::ReadAllText($transcript)
    Remove-Item -LiteralPath $transcript
    $out
}

if ($skipped -notcontains 'build') {
    if (Test-Path -LiteralPath $build) { Remove-Item -LiteralPath $build -Recurse -Force }
    $src = Join-Path $build 'src'
    New-Item -ItemType Directory -Force $src | Out-Null
    Copy-Item (Join-Path $here 'src\k6\*') $src
    Copy-Item (Join-Path $here 'src\k0_quiet.m') $src
    if ($Mlapp) {
        New-Item -ItemType Directory -Force (Join-Path $src 'mlapp') | Out-Null
        Copy-Item -LiteralPath $Mlapp (Join-Path $src 'mlapp\k6_app.mlapp')
    }
    $env:K6_OUT = $build
    $out = Join-Path $WorkRoot 'build.out.txt'
    $p = Start-Process -FilePath (Join-Path $Runtime 'bin\win64\jgraph.exe') -ArgumentList @('-batch', ('"{0}"' -f (Join-Path $here 'build_k6_jgraph.m'))) `
        -RedirectStandardOutput $out -RedirectStandardError "$out.err" -NoNewWindow -PassThru
    $null = $p.Handle
    $p.WaitForExit()
    $text = [System.IO.File]::ReadAllText($out)
    $errText = [System.IO.File]::ReadAllText("$out.err")
    if ($errText.Trim()) { $text += "`n" + ((($errText.Trim() -split "`r?`n") | ForEach-Object { "STDERR`t$_" }) -join "`n") }
    Write-Recording 'k6_build.out.txt' ($text + "`nEXIT`t$($p.ExitCode)")
}

if ($skipped -notcontains 'pe') {
    $exes = @(Get-ChildItem -LiteralPath $build -Recurse -Filter *.exe | Where-Object { $_.FullName -notlike '*\src\*' } |
        Sort-Object FullName | ForEach-Object { $_.FullName })
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($exe in $exes) {
        $lines.Add("CASE`t" + $exe.Substring($build.Length + 1))
        foreach ($l in (& $Python (Join-Path $here 'pe-resources.py') $exe)) { $lines.Add($l) }
    }
    Write-Recording 'k6_pe.out.txt' ($lines -join "`n")
}

$cache = Join-Path $WorkRoot 'cache'

if ($skipped -notcontains 'console') {
    # The cases record-k6.ps1 ran against R2025b's programs, by the same names.
    $cases = @(
        @('hello.args', 'm_plain\k6_hello.exe', @('a', 'b c')),
        @('hello.question', 'm_plain\k6_hello.exe', @('-?')),
        @('hello.dash_h', 'm_plain\k6_hello.exe', @('-h')),
        @('hello.question_then_arg', 'm_plain\k6_hello.exe', @('-?', 'x')),
        @('hello.slash_question_upper', 'm_plain\k6_hello.exe', @('/?')),
        @('named', 'm_named\k6_named.exe', @()),
        @('helped.question', 'm_help\k6_helped.exe', @('-?')),
        @('helped.dash_h', 'm_help\k6_helped.exe', @('-h')),
        @('helped.slash_question', 'm_help\k6_helped.exe', @('/?')),
        @('helped.dash_help', 'm_help\k6_helped.exe', @('-help')),
        @('helped.dashdash_help', 'm_help\k6_helped.exe', @('--help')),
        @('helped.after_arg', 'm_help\k6_helped.exe', @('x', '-?')),
        @('helped.plain', 'm_help\k6_helped.exe', @('x')),
        @('api_m.question', 'api_m\k6_api.exe', @('-?')),
        @('api_m.args', 'api_m\k6_api.exe', @('1')),
        @('api_numeric.args', 'api_m_numeric\k6_hello.exe', @('1', '[2 3]')),
        @('separate', 'm_separate\k6_sep.exe', @('s')),
        @('api_separate', 'api_m_separate\k6_hello.exe', @('s')),
        @('m_err', 'm_err\k6_err.exe', @())
    )
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($c in $cases) {
        if ($onlyCases.Count -and $onlyCases -notcontains $c[0]) { continue }
        $exe = Join-Path $build $c[1]
        $lines.Add("CASE`t$($c[0])")
        $lines.Add("RUN`t$($c[1]) $($c[2] -join ' ')")
        if (-not (Test-Path -LiteralPath $exe)) { $lines.Add("  not built"); continue }
        $out = Run-Quiet $exe $c[2] @{ MCR_CACHE_ROOT = $cache; JGRAPH_RUNTIME_ROOT = $Runtime }
        foreach ($l in ($out -split "`r?`n")) { if ($l -ne '') { $lines.Add("  $l") } }
        Write-Host ("  {0}" -f $c[0])
    }
    Write-Recording 'k6_console.out.txt' ($lines -join "`n")

    # What differs from R2025b's run of the same case, the run times aside.
    $theirs = [System.IO.File]::ReadAllText((Join-Path $here 'recordings\k6_console.out.txt')) -split "`n"
    $ours = [System.IO.File]::ReadAllText((Join-Path $recordings 'k6_console.out.txt')) -split "`n"
    function Cases($text) {
        $map = [ordered]@{}
        $name = $null
        foreach ($l in $text) {
            if ($l -like "CASE`t*") { $name = $l.Substring(5); $map[$name] = New-Object System.Collections.Generic.List[string] }
            elseif ($name -and $l -notlike '  SECONDS*' -and $l -ne '') { $map[$name].Add($l) }
        }
        $map
    }
    $a = Cases $theirs
    $b = Cases $ours
    $diff = New-Object System.Collections.Generic.List[string]
    foreach ($name in $b.Keys) {
        $left = if ($a.Contains($name)) { $a[$name] } else { @() }
        $right = $b[$name]
        if (($left -join "`n") -ne ($right -join "`n")) {
            $diff.Add("CASE`t$name")
            foreach ($l in $left) { if ($right -notcontains $l) { $diff.Add("  R2025b`t$l") } }
            foreach ($l in $right) { if ($left -notcontains $l) { $diff.Add("  JGraph`t$l") } }
        }
    }
    if ($diff.Count -eq 0) { $diff.Add('no differences') }
    Write-Recording 'k6_differences.txt' ($diff -join "`n")
}

if ($skipped -notcontains 'windows') {
    . (Join-Path $here 'k6-windows.ps1')
    $cases = @(
        @('e_plain', 'e_plain\k6_hello.exe', @('a', 'b c'), 'none'),
        @('e_wait', 'e_wait\k6_wait.exe', @(), 'none'),
        @('api_e_wait', 'api_e\k6_wait.exe', @(), 'none'),
        @('api_e_default', 'api_e_default\k6_hello.exe', @(), 'none'),
        @('e_fig', 'e_fig\k6_fig.exe', @(), 'close:k6 figure'),
        @('m_fig', 'm_fig\k6_fig.exe', @(), 'close:k6 figure'),
        @('e_err', 'e_err\k6_err.exe', @(), 'dismiss'),
        @('e_app_m', 'e_app_m\k6_app.exe', @(), 'press:k6 app:Close'),
        @('e_app_mlapp', 'e_app_mlapp\k6_app.exe', @(), 'press:k6 app:Close'),
        @('m_app_mlapp', 'm_app_mlapp\k6_app_console.exe', @(), 'close:k6 app'),
        @('api_e_app', 'api_e_app\k6_app.exe', @(), 'press:k6 app:Close')
    )
    $lines = Invoke-K6Windows -Cases $cases -Build $build -WorkRoot $WorkRoot -Cache $cache -Only $onlyCases
    Write-Recording 'k6_windows.out.txt' ($lines -join "`n")
}
