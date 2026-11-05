<#
.SYNOPSIS
    Stage W15's window check (App Designer window plan): a custom UI component in App Designer - the toolstrip swapped for a
    component (Event and Configure for Apps shown, App Details, SHARE, App Input Arguments and Convert gone, the Callback split
    button in Callback's place), the component root's inspector tab and its Event-Callback Pairs tab, the Add Event-Public
    Callback Pair dialog writing an event block, the Add Property dialog writing a managed property, Save writing both, Run
    making the component's figure, and Configure for Apps bringing up the "App Designer Custom UI Component Metadata" dialog
    whose Ok writes resources\appDesigner.json - in the JGraph application.

.DESCRIPTION
    Copies comp3.mlapp - the package ComponentAuthoringTests.AComponentSavesAsAnMlappAndOpensAgain writes from the engine's
    template comp3.m (two events, three managed properties, a callback and a startup function) into
    tools\matlab-checklist\ui-probes\w15\rt\jgraph when JGRAPH_W15_OUT names that folder; the test is run here when the file
    is missing - into <WorkRoot>\comp. One launch: -r opens it in App Designer; the designer window is found;
    the toolstrip read (Event, Configure for Apps and the Callback split button there and enabled; App Details, SHARE, App Input
    Arguments and Convert gone); the component's root row selected in the Component Browser (the inspector's first tab reads
    'Custom UI Component', the Event-Callback Pairs tab appears with two rows); Add… on that tab invoked, the dialog's name set to
    Toggled and Add pressed (three rows, the file not yet touched); the Code Browser's Properties add invoked, the Add Property
    dialog's name, data type and default set (Gain, double, 2) and Add pressed; Save written and the file read back (the events
    block with Toggled, the Gain declaration); Run invoked (the component's figure window appears); Configure for Apps invoked
    (the metadata dialog comes up with the file, the name comp3 and the version 1.0), its name set to Counter and Ok pressed, the
    confirmation answered OK (resources\appDesigner.json written with className comp3, componentName Counter and a defaultPosition).

    The user's settings file (%APPDATA%\JGraph\settings.json) is moved aside before and put back after; the application is ended with Stop-Process.

.PARAMETER WorkRoot
    A scratch folder outside the repo.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $WorkRoot,
    [string] $Configuration = 'Release',
    [int] $TimeoutSeconds = 90
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W15Native {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    public static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<IntPtr, string>> Windows(uint processId) {
        var found = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<IntPtr, string>>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == processId && IsWindowVisible(h)) {
                var sb = new System.Text.StringBuilder(512); GetWindowText(h, sb, sb.Capacity);
                found.Add(new System.Collections.Generic.KeyValuePair<IntPtr, string>(h, sb.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
New-Item -ItemType Directory -Force $WorkRoot | Out-Null
$WorkRoot = (Resolve-Path -LiteralPath $WorkRoot).Path
$A = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$failures = New-Object System.Collections.Generic.List[string]

& dotnet build (Join-Path $repo 'src\JGraph.Application\JGraph.Application.csproj') -c $Configuration -v q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "the application did not build" }
$exe = Join-Path $repo "src\JGraph.Application\bin\$Configuration\net8.0-windows\JGraph.Application.exe"

$compFolder = Join-Path $WorkRoot 'comp'
if (Test-Path -LiteralPath $compFolder) { Remove-Item -LiteralPath $compFolder -Recurse -Force }
New-Item -ItemType Directory -Force $compFolder | Out-Null
$comp = Join-Path $compFolder 'comp3.mlapp'
$made = Join-Path $repo 'tools\matlab-checklist\ui-probes\w15\rt\jgraph\comp3.mlapp'
if (-not (Test-Path -LiteralPath $made)) {
    # R2025b has no plain-text component, so the designer opens an .mlapp: the test writes it from the engine's template.
    $env:JGRAPH_W15_OUT = Split-Path -Parent $made
    & dotnet test (Join-Path $repo 'tests\JGraph.Tests\JGraph.Tests.csproj') -c Debug --nologo --filter 'FullyQualifiedName~ComponentAuthoringTests.AComponentSavesAsAnMlappAndOpensAgain' | Out-Null
    Remove-Item Env:\JGRAPH_W15_OUT
    if (-not (Test-Path -LiteralPath $made)) { throw "the test did not write $made" }
}
Copy-Item -LiteralPath $made -Destination $comp
$registry = Join-Path $compFolder 'resources\appDesigner.json'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# The class text inside an .mlapp (its document part), for reading what Save wrote.
function Read-MlappCode([string] $path) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -like 'matlab/document.xml' } | Select-Object -First 1
        if (-not $entry) { return '' }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
}

$settingsFile = Join-Path $env:APPDATA 'JGraph\settings.json'
$settingsCopy = Join-Path $WorkRoot 'settings.before.json'
$hadSettings = Test-Path -LiteralPath $settingsFile
if ($hadSettings) { Move-Item -LiteralPath $settingsFile -Destination $settingsCopy -Force }

function Check([bool] $ok, [string] $what) {
    Write-Host ("  {0,-4} {1}" -f $(if ($ok) { 'ok' } else { 'FAIL' }), $what)
    if (-not $ok) { $failures.Add($what) }
}

function Find-Window([int] $processId, [scriptblock] $match, [int] $seconds = $TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        foreach ($entry in [W15Native]::Windows([uint32] $processId)) {
            if (& $match $entry.Value) { return $entry }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Find-By([IntPtr] $hwnd, $property, [string] $value, [int] $seconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($property, $value)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        try {
            $found = $A::FromHandle($hwnd).FindFirst($Tree::Subtree, $condition)
            if ($found) { return $found }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Find-In([IntPtr] $hwnd, [string] $id, [int] $seconds = 10) { return Find-By $hwnd $A::AutomationIdProperty $id $seconds }

function Find-InProcess([int] $processId, [string] $id, [int] $seconds = 10) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)
    $deadline = (Get-Date).AddSeconds($seconds)
    do {
        foreach ($entry in [W15Native]::Windows([uint32] $processId)) {
            try {
                $found = $A::FromHandle($entry.Key).FindFirst($Tree::Subtree, $condition)
                if ($found) { return $found }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Pattern($element, $pattern) {
    if (-not $element) { return $null }
    $found = $null
    if ($element.TryGetCurrentPattern($pattern, [ref] $found)) { return $found }
    return $null
}

function Invoke-Element($element) {
    $pattern = Pattern $element ([System.Windows.Automation.InvokePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.Invoke()
    Start-Sleep -Milliseconds 600
    return $true
}

function Set-Value($element, [string] $value) {
    $pattern = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if (-not $pattern) { return $false }
    $pattern.SetValue($value)
    Start-Sleep -Milliseconds 300
    return $true
}

function Text-Of($element) {
    if (-not $element) { return $null }
    $value = Pattern $element ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value) { return $value.Current.Value }
    return $element.Current.Name
}

function Wait-Text([IntPtr] $hwnd, [string] $id, [string] $expected, [int] $seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $text = $null
    do {
        $text = Text-Of (Find-In $hwnd $id 2)
        if ($text -and $text.Contains($expected)) { return $text }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    return $text
}

function Save-Picture([IntPtr] $hwnd, [string] $path) {
    $rect = New-Object W15Native+RECT
    [void] [W15Native]::GetWindowRect($hwnd, [ref] $rect)
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    if ($w -le 0 -or $h -le 0) { return $false }
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [W15Native]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return $ok
}

function Visible-And-Enabled([IntPtr] $hwnd, [string] $id, [int] $seconds = 5) {
    $element = Find-In $hwnd $id $seconds
    return ($null -ne $element -and -not $element.Current.IsOffscreen -and $element.Current.IsEnabled)
}

function Gone([IntPtr] $hwnd, [string] $id) {
    $element = Find-In $hwnd $id 2
    return ($null -eq $element -or $element.Current.IsOffscreen)
}

function Start-JGraph([string] $statement) {
    $info = New-Object System.Diagnostics.ProcessStartInfo($exe)
    $info.UseShellExecute = $false
    $info.Arguments = "-r `"$statement`""
    return [System.Diagnostics.Process]::Start($info)
}

# A row of the Component Browser selected by its name.
function Select-Row([IntPtr] $hwnd, [string] $name) {
    $browser = Find-In $hwnd 'ComponentBrowser' 5
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    $deadline = (Get-Date).AddSeconds(5)
    do {
        $item = if ($browser) { $browser.FindFirst($Tree::Subtree, $condition) } else { $null }
        $select = Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($select) { $select.Select(); Start-Sleep -Milliseconds 600; return $true }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Save-App([IntPtr] $hwnd, [string] $file) {
    $before = (Get-Item -LiteralPath $file).LastWriteTimeUtc
    $save = Find-In $hwnd 'appDesignerToolstrip.qab.saveButton' 5
    if (-not $save -or -not $save.Current.IsEnabled) { Write-Host '       (Save is not enabled: the component is not dirty)'; return $false }
    try { [void] (Invoke-Element $save) } catch { Write-Host "       (Save: $($_.Exception.Message))"; return $false }
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (Get-Item -LiteralPath $file).LastWriteTimeUtc -eq $before) { Start-Sleep -Milliseconds 200 }
    return ((Get-Item -LiteralPath $file).LastWriteTimeUtc -ne $before)
}

function Rows-Of($list) {
    if (-not $list) { return 0 }
    $condition = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    return $list.FindAll($Tree::Children, $condition).Count
}

$p = Start-JGraph "appdesigner('$($comp.Replace('\', '/'))')"
try {
    $designer = Find-Window $p.Id { param($t) $t.StartsWith('App Designer - ') }
    Check ($null -ne $designer) "appdesigner opened the component in App Designer ($($designer.Value))"
    if (-not $designer) { throw 'no designer window' }
    $hwnd = $designer.Key
    Start-Sleep -Milliseconds 2500

    # --- the toolstrip for a component (the EDITOR tab stands in the Code View) ------------------------------------------------
    $switch = Pattern (Find-In $hwnd 'CodeView.Switch' 5) ([System.Windows.Automation.TogglePattern]::Pattern)
    Check ($null -ne $switch) 'the Code View switch is there'
    if ($switch) { $switch.Toggle(); Start-Sleep -Milliseconds 1200 }
    $editorTab = Pattern (Find-In $hwnd 'appDesignerToolstrip.editorTab' 5) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($editorTab) { $editorTab.Select(); Start-Sleep -Milliseconds 600 }
    Check (Visible-And-Enabled $hwnd 'appDesignerToolstrip.editorTab.edit.addEvent') 'EDITOR > Event is shown and enabled'
    Check (Visible-And-Enabled $hwnd 'appDesignerToolstrip.editorTab.edit.addCallbackSplitButton') 'the Callback split button stands in the plain button''s place'
    Check (Gone $hwnd 'appDesignerToolstrip.editorTab.edit.addCallbackButton') 'the plain Callback button is gone'
    Check (Gone $hwnd 'appDesignerToolstrip.editorTab.edit.addInputArgumentsButton') 'App Input Arguments is gone'
    $designerTab = Pattern (Find-In $hwnd 'appDesignerToolstrip.designerTab' 5) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($designerTab) { $designerTab.Select(); Start-Sleep -Milliseconds 600 }
    Check (Visible-And-Enabled $hwnd 'appDesignerToolstrip.designerTab.share.configureMetaData') 'DESIGNER > Configure for Apps is shown and enabled'
    Check (Gone $hwnd 'appDesignerToolstrip.designerTab.share.appDetailsButton') 'App Details is gone'
    Check (Gone $hwnd 'appDesignerToolstrip.designerTab.share.shareAllOptions') 'SHARE is gone'
    $layoutTab = Pattern (Find-In $hwnd 'appDesignerToolstrip.layoutTab' 5) ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($layoutTab) { $layoutTab.Select(); Start-Sleep -Milliseconds 600 }
    Check (Gone $hwnd 'appDesignerToolstrip.layoutTab.file.responsifyPushButton') 'CANVAS > Convert is gone'
    if ($designerTab) { $designerTab.Select(); Start-Sleep -Milliseconds 400 }

    # --- the component's root: the inspector's tab and the Event-Callback Pairs tab -------------------------------------------
    Check (Select-Row $hwnd 'comp3') 'the component''s root row selected in the Component Browser'
    $inspectorTab = Find-In $hwnd 'PropertiesPanel.Inspector' 5
    Check ($inspectorTab -and $inspectorTab.Current.Name -eq 'Custom UI Component') "the inspector's first tab says Custom UI Component ($($inspectorTab.Current.Name))"
    $pairsTab = Find-In $hwnd 'PropertiesPanel.EventPairs' 5
    Check ($pairsTab -and -not $pairsTab.Current.IsOffscreen) 'the Event-Callback Pairs tab is shown for the root'
    $pairsSelect = Pattern $pairsTab ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($pairsSelect) { $pairsSelect.Select(); Start-Sleep -Milliseconds 800 }
    $list = Find-In $hwnd 'EventPairs.List' 5
    Check ((Rows-Of $list) -eq 2) "the tab lists the two events ($(Rows-Of $list))"
    Check (Save-Picture $hwnd (Join-Path $WorkRoot 'component.png')) 'a picture of the designer with the component'

    # --- Add Event-Public Callback Pair ----------------------------------------------------------------------------------------
    Check (Invoke-Element (Find-In $hwnd 'EventPairs.Add' 5)) 'Add... invoked on the Event-Callback Pairs tab'
    $dialog = Find-Window $p.Id { param($t) $t -like 'Add Event*Public Callback Pair' } 8
    Check ($null -ne $dialog) 'the Add Event-Public Callback Pair dialog is up'
    if ($dialog) {
        Check (Set-Value (Find-In $dialog.Key 'AddEvent.Name' 5) 'Toggled') 'the event name set to Toggled'
        Start-Sleep -Milliseconds 400
        $callback = Text-Of (Find-In $dialog.Key 'AddEvent.Callback' 3)
        Check ($callback -eq 'ToggledFcn') "the dialog previews the public callback ToggledFcn ($callback)"
        Check (Invoke-Element (Find-In $dialog.Key 'AddEvent.Add' 3)) 'Add pressed'
    }
    Start-Sleep -Milliseconds 800
    $list = Find-In $hwnd 'EventPairs.List' 5
    Check ((Rows-Of $list) -eq 3) "the tab lists three events ($(Rows-Of $list))"

    # --- Add Property through the Code Browser -----------------------------------------------------------------------------------
    $propertiesTab = Find-By $hwnd $A::NameProperty 'Properties' 5
    $propertiesSelect = Pattern $propertiesTab ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($propertiesSelect) { $propertiesSelect.Select(); Start-Sleep -Milliseconds 600 }
    Check (Invoke-Element (Find-In $hwnd 'CodeBrowser.Properties.Add' 5)) 'the Code Browser''s Properties add invoked'
    $dialog = Find-Window $p.Id { param($t) $t -eq 'Add Property' } 8
    Check ($null -ne $dialog) 'the Add Property dialog is up'
    if ($dialog) {
        Check (Set-Value (Find-In $dialog.Key 'AddProperty.Name' 5) 'Gain') 'the property name set to Gain'
        $typeBox = Find-In $dialog.Key 'AddProperty.DataType' 3
        $typeValue = Pattern $typeBox ([System.Windows.Automation.ValuePattern]::Pattern)
        if ($typeValue) { $typeValue.SetValue('double') } else { Write-Host '       (the data type box took no value)' }
        Check (Set-Value (Find-In $dialog.Key 'AddProperty.Default' 3) '2') 'the default value set to 2'
        Check (Invoke-Element (Find-In $dialog.Key 'AddProperty.Add' 3)) 'Add pressed'
    }
    Start-Sleep -Milliseconds 800
    Check (Save-App $hwnd $comp) 'Save wrote the component'
    Start-Sleep -Milliseconds 800
    $saved = Read-MlappCode $comp
    Check ($saved.Contains("    events (HasCallbackProperty, NotifyAccess = private)") -and $saved.Contains("        Toggled")) 'the events block holds Toggled'
    Check ($saved.Contains("Gain double = 2;")) 'the managed property Gain is declared'
    Check ($saved.Contains("    properties (Access = private)") -and $saved.Contains("Gain double = 2;")) 'Gain stands in a private block (the Code Browser''s + adds a private property)'

    # --- Run makes the component's figure ---------------------------------------------------------------------------------------
    $windowsBefore = ([W15Native]::Windows([uint32] $p.Id)).Count
    Check (Invoke-Element (Find-In $hwnd 'appDesignerToolstrip.editorTab.run.run' 5)) 'Run invoked'
    $status = Wait-Text $hwnd 'AppDesigner.Status' 'Running comp3.' 40
    Check ($status -eq 'Running comp3.') "the status says the component runs ($status)"
    $deadline = (Get-Date).AddSeconds(20)
    do { Start-Sleep -Milliseconds 500; $windowsAfter = ([W15Native]::Windows([uint32] $p.Id)).Count } while ($windowsAfter -le $windowsBefore -and (Get-Date) -lt $deadline)
    Check ($windowsAfter -gt $windowsBefore) "a figure window came up for the component ($windowsBefore -> $windowsAfter)"

    # --- Configure for Apps ------------------------------------------------------------------------------------------------------
    if ($designerTab) { $designerTab.Select(); Start-Sleep -Milliseconds 600 }
    Check (Invoke-Element (Find-In $hwnd 'appDesignerToolstrip.designerTab.share.configureMetaData' 5)) 'Configure for Apps invoked'
    $metadata = Find-Window $p.Id { param($t) $t -eq 'App Designer Custom UI Component Metadata' } 30
    Check ($null -ne $metadata) 'the App Designer Custom UI Component Metadata dialog is up'
    if ($metadata) {
        $shownPath = Text-Of (Find-In $metadata.Key 'ComponentMetadata.FilePath' 5)
        Check ($shownPath -like '*comp3.mlapp') "the dialog names the component file ($shownPath)"
        Check ((Text-Of (Find-In $metadata.Key 'ComponentMetadata.Name' 3)) -eq 'comp3') 'the name is offered as comp3'
        Check ((Text-Of (Find-In $metadata.Key 'ComponentMetadata.Version' 3)) -eq '1.0') 'the version is offered as 1.0'
        Check (Save-Picture $metadata.Key (Join-Path $WorkRoot 'metadata.png')) 'a picture of the metadata dialog'
        Check (Set-Value (Find-In $metadata.Key 'ComponentMetadata.Name' 3) 'Counter') 'the name set to Counter'
        Check (Invoke-Element (Find-In $metadata.Key 'ComponentMetadata.Ok' 3)) 'Ok pressed'
        $confirm = Find-InProcess $p.Id 'ComponentMetadata.Confirm.Second' 40
        Check ($null -ne $confirm) 'the registration was confirmed with Add to Path and OK offered'
        if ($confirm) { [void] (Invoke-Element $confirm) }
    }
    Start-Sleep -Milliseconds 1000
    Check (Test-Path -LiteralPath $registry) 'resources\appDesigner.json was written beside the component'
    if (Test-Path -LiteralPath $registry) {
        $json = Get-Content -LiteralPath $registry -Raw
        Check ($json.Contains('"className": "comp3"') -and $json.Contains('"componentName": "Counter"')) 'the registry names the class and the library name'
        Check ($json.Contains('"defaultPosition": [')) 'the registry holds the default position the check read'
        Check ($json.Contains('"schema": 3') -and $json.Contains('"MATLABRelease": "R2025b"')) 'the registry ends with the schema and release'
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 800
}

if (Test-Path -LiteralPath $settingsFile) { Remove-Item -LiteralPath $settingsFile -Force }
if ($hadSettings) { Move-Item -LiteralPath $settingsCopy -Destination $settingsFile -Force }

Write-Host ""
if ($failures.Count -eq 0) { Write-Host "all checks pass; pictures in $WorkRoot" } else { Write-Host "$($failures.Count) failed:"; $failures | ForEach-Object { Write-Host "  $_" }; exit 1 }

exit 0
