<#
.SYNOPSIS
    Stage K5's window check (compiler and SDK plan): JGraph's own windows only, driven through UI
    Automation patterns (no synthesized input), in three runs.

.DESCRIPTION
    console  k5window with no WPF of its own: the runtime starts its window thread. Two components:
             a uifigure app ("k5 app of a") with a Press button, and a figure ("Figure 1: k5 plot of b").
             The plot's toolbar must be there (Open...) without Script... or Options...; Press is
             invoked while the client makes no call; both windows are closed through their window
             pattern, and WaitForFiguresToClose must return for each.
    wpf      the same, with the client running a WPF application of its own that the windows join.
    ide      JGraph.Application -batch <script> -showfigures (the run loop moved out in K5): a figure
             whose toolbar must have Script... and Options..., and an app whose Press closes both, so
             the program ends with exit code 0 and the callback's line on standard output.

.PARAMETER WorkRoot
    A scratch folder outside the repo.

.PARAMETER Runs
    A comma-separated list of 'console', 'wpf' and 'ide'; all three by default.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Runs = 'console,wpf,ide',
    [string] $Configuration = 'Debug',
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$A = [System.Windows.Automation.AutomationElement]
$failures = New-Object System.Collections.Generic.List[string]

function Check([bool] $ok, [string] $what) {
    Write-Host ("  {0,-4} {1}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $what)
    if (-not $ok) { $failures.Add($what) }
}

function Wait-Line([string] $file, [string] $key) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $file) {
            $hit = Get-Content -LiteralPath $file -ErrorAction SilentlyContinue | Where-Object { $_ -like "$key`t*" } | Select-Object -First 1
            if ($hit) { return ($hit -split "`t", 2)[1] }
        }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Find-Window([int] $processId, [string] $name) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $processId)),
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)))
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $found = $A::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($found) { return $found }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

# A button that should be there is waited for: a window a host's thread shows is opened after the
# call that asked for it, and its components arrive in a frame after that. One that should not be
# there is looked for once.
function Find-Button($window, [string] $name, [switch] $Once) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $deadline = (Get-Date).AddSeconds($(if ($Once) { 0 } else { 15 }))
    do {
        $found = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($found -or $Once) { return $found }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Close-Window($window) {
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}

function Press($window, [string] $name) {
    $button = Find-Button $window $name
    if (-not $button) {
        # What the window does hold, for whoever reads the failure.
        foreach ($e in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            Write-Host ("    uia: {0} '{1}'" -f $e.Current.ControlType.ProgrammaticName, $e.Current.Name)
        }
        return $false
    }
    try {
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    catch {
        Write-Host "    uia: invoking '$name' failed: $($_.Exception.Message)"
        return $false
    }
    return $true
}

function Stop-Leftover($process) {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}

$selected = $Runs -split ','
if ($selected -contains 'console' -or $selected -contains 'wpf') {
    $clientOut = Join-Path $WorkRoot 'client'
    & dotnet build (Join-Path $PSScriptRoot 'k5window\k5window.csproj') -c $Configuration -o $clientOut -nologo -v q *> (Join-Path $WorkRoot 'client_build.txt')
    $built = $LASTEXITCODE
    & dotnet build-server shutdown *> $null
    if ($built -ne 0) { throw "k5window did not build (see $WorkRoot\client_build.txt)." }

    foreach ($run in @('console', 'wpf')) {
        if ($selected -notcontains $run) { continue }
        Write-Host "== $run"
        $out = Join-Path $WorkRoot "$run.out.txt"
        $err = Join-Path $WorkRoot "$run.err.txt"
        $arguments = @((Join-Path $WorkRoot "work_$run"))
        if ($run -eq 'wpf') { $arguments += '-wpf-host' }
        $p = Start-Process -FilePath (Join-Path $clientOut 'k5window.exe') -ArgumentList $arguments `
            -RedirectStandardOutput $out -RedirectStandardError $err -PassThru -WindowStyle Hidden
        $null = $p.Handle # Windows PowerShell keeps no exit code for a process whose handle was never read
        try {
            Check ($null -ne (Wait-Line $out 'ready')) 'the client opened both components and showed their figures'
            $app = Find-Window $p.Id 'k5 app of a'
            $plot = Find-Window $p.Id 'Figure 1: k5 plot of b'
            Check ($null -ne $app) "a window titled 'k5 app of a'"
            Check ($null -ne $plot) "a window titled 'Figure 1: k5 plot of b'"
            if ($plot) {
                Check ($null -ne (Find-Button $plot 'Open…')) "the plot's toolbar is there (Open…)"
                Check ($null -eq (Find-Button $plot 'Script…' -Once)) "the plot's toolbar has no Script…"
                Check ($null -eq (Find-Button $plot 'Options…' -Once)) "the plot's toolbar has no Options…"
            }
            if ($app) {
                Check (Press $app 'Press') "the app has a Press button, invoked"
                Check ((Wait-Line $out 'pressed') -eq '1') 'its callback ran once, between the client''s calls'
                Close-Window $app
                Check ((Wait-Line $out 'a.closed') -eq '0') 'closing the app''s window ended WaitForFiguresToClose, its figure gone'
            }
            if ($plot) {
                Close-Window $plot
                Check ((Wait-Line $out 'b.closed') -eq '0') 'closing the plot''s window ended WaitForFiguresToClose, its figure gone'
            }
            Check ($p.WaitForExit($TimeoutSeconds * 1000) -and $p.ExitCode -eq 0) 'the client ended with exit code 0'
        }
        finally { Stop-Leftover $p }
        Get-Content -LiteralPath $out | ForEach-Object { Write-Host "    | $_" }
        $errText = (Get-Content -LiteralPath $err -Raw -ErrorAction SilentlyContinue)
        if ($errText -and $errText.Trim()) { Write-Host "    stderr: $($errText.Trim())" }
    }
}

if ($selected -contains 'ide') {
    Write-Host "== ide"
    $exe = Join-Path $repo "src\JGraph.Application\bin\$Configuration\net8.0-windows\JGraph.Application.exe"
    $script = Join-Path $WorkRoot 'k5ide.m'
    [System.IO.File]::WriteAllText($script, @"
figure('Name', 'k5 ide plot');
plot(1:5, [2 4 3 5 1]);
f = uifigure('Name', 'k5 ide app', 'Position', [200 300 320 160]);
uibutton(f, 'Text', 'Press', 'Position', [100 60 120 40], 'ButtonPushedFcn', @(~, ~) k5ide_pressed());
function k5ide_pressed()
disp('k5 ide pressed');
close all force;
end
"@.Replace("`r`n", "`n"))
    $out = Join-Path $WorkRoot 'ide.out.txt'
    $err = Join-Path $WorkRoot 'ide.err.txt'
    $p = Start-Process -FilePath $exe -ArgumentList @('-batch', "`"$script`"", '-showfigures') -WorkingDirectory $WorkRoot `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
    $null = $p.Handle
    try {
        $plot = Find-Window $p.Id 'Figure 1: k5 ide plot'
        $app = Find-Window $p.Id 'k5 ide app'
        Check ($null -ne $plot) "a window titled 'Figure 1: k5 ide plot'"
        Check ($null -ne $app) "a window titled 'k5 ide app'"
        if ($plot) {
            Check ($null -ne (Find-Button $plot 'Script…')) "the IDE's figure toolbar has Script…"
            Check ($null -ne (Find-Button $plot 'Options…')) "the IDE's figure toolbar has Options…"
        }
        if ($app) { Check (Press $app 'Press') "the app's Press button, invoked" }
        Check ($p.WaitForExit($TimeoutSeconds * 1000) -and $p.ExitCode -eq 0) 'the program ended with exit code 0 once its windows closed'
    }
    finally { Stop-Leftover $p }
    $text = Get-Content -LiteralPath $out -Raw -ErrorAction SilentlyContinue
    Check ($text -match 'k5 ide pressed') "the callback's line reached standard output"
    if ($text) { $text.Trim() -split "`r?`n" | ForEach-Object { Write-Host "    | $_" } }
    $errText = (Get-Content -LiteralPath $err -Raw -ErrorAction SilentlyContinue)
    if ($errText -and $errText.Trim()) { Write-Host "    stderr: $($errText.Trim())" }
}

if ($failures.Count) {
    Write-Host "$($failures.Count) check(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
