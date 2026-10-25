<#
.SYNOPSIS
    Runs one compiled program (an R2025b mcc build, or a client of one) so that it cannot raise a
    Windows error dialog, and prints what it did (compiler and SDK plan, stage K0).

.DESCRIPTION
    A program built by MATLAB Compiler whose DLLs are not on the search path makes Windows show a
    "System Error ... was not found" box with the error sound, and the launching tool never sees
    it (this happened on 2026-10-21). This script prevents that four ways:

      1. Before anything runs, it reads the program's PE import table (normal and delay-load)
         and resolves every DLL the way the loader would: the program's folder, then System32,
         then each folder on the PATH the child will get. An unresolved DLL means the program
         is not started: the script prints UNRESOLVED lines and exits with code 97.
      2. It puts R2025b's runtime\win64 (and bin\win64) first on the child's PATH.
      3. It sets SetErrorMode(SEM_FAILCRITICALERRORS | ...) in this process before starting the
         child. The child inherits SEM_FAILCRITICALERRORS (checked: the others are not
         inherited), so a loader error ends it with a status code instead of a dialog. The child
         is also put in a job object with JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION, so a crash
         ends it without the crash dialog, and KILL_ON_JOB_CLOSE, so its tree dies with it.
      4. It refuses a program with the Windows GUI subsystem (an mcc -e build) unless
         -AllowWindowed is given; a windowed MATLAB program needs the user's leave.

    The child gets a closed stdin, a timeout, and its whole process tree killed if the timeout
    passes. The script prints the child's stdout, then STDERR, EXIT and SECONDS lines, and
    writes the same text to -OutFile when one is given. It exits with the child's exit code
    (98 on a timeout, 97 when the imports do not resolve, 96 when a windowed program is refused).

.PARAMETER Exe
    The program to run.

.PARAMETER Arguments
    The program's arguments, each passed as one argv entry (quoted as CommandLineToArgvW reads it).

.PARAMETER ArgumentsBase64
    The arguments as a JSON array of strings, UTF-8, base64-encoded, for callers using
    powershell -File: that joins an array parameter into one comma-separated string, and
    Windows PowerShell 5.1 strips the quotes from a JSON string on a native command line.
    Wins over -Arguments.

.PARAMETER MatlabRoot
    The R2025b installation whose runtime the program uses.

.PARAMETER WorkingDirectory
    The child's current folder. Defaults to the program's folder.

.PARAMETER Environment
    Extra environment variables for the child, as a hashtable.

.PARAMETER TimeoutSeconds
    How long the child may run. The first start of a MATLAB Runtime is slow (56 s was seen for
    Python's initialize()), so the default is generous.

.PARAMETER OutFile
    Where to write the transcript (UTF-8, LF line ends).

.PARAMETER AllowWindowed
    Start a Windows GUI subsystem program. Only with the user's leave.

.PARAMETER CheckOnly
    Resolve the imports and stop.

.PARAMETER PathFirst
    Folders put first on the child's PATH, before R2025b's runtime: a JGraph Runtime's runtime\win64
    for a C program that loads jgrt.dll (stage K10).

.PARAMETER NoMatlabRuntime
    Leave R2025b's runtime\win64 and bin\win64 off the child's PATH, so a program that should not
    need them shows it.

.EXAMPLE
    powershell -File run-quiet.ps1 -Exe out\hello\hello.exe -Arguments 1,two,'three four'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [string[]] $Arguments = @(),
    [string] $ArgumentsBase64,
    [string] $MatlabRoot = 'C:\Program Files\MATLAB\R2025b',
    [string] $WorkingDirectory,
    [hashtable] $Environment = @{},
    [int] $TimeoutSeconds = 300,
    [string] $OutFile,
    [switch] $AllowWindowed,
    [switch] $CheckOnly,
    [string[]] $PathFirst = @(),
    [switch] $NoMatlabRuntime
)

$ErrorActionPreference = 'Stop'

if (-not ('JGraphQuiet.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JGraphQuiet
{
    public static class Native
    {
        [DllImport("kernel32.dll")] public static extern uint SetErrorMode(uint mode);
        [DllImport("kernel32.dll")] public static extern uint GetErrorMode();

        [StructLayout(LayoutKind.Sequential)]
        struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IoCounters { public ulong R, W, O, RB, WB, OB; }

        [StructLayout(LayoutKind.Sequential)]
        struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr a, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int cls, ref ExtendedLimits info, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        // A job whose processes die on an unhandled exception instead of showing the crash dialog,
        // and are all killed when the job's last handle closes (the process tree on a timeout).
        public static IntPtr QuietJob(IntPtr process)
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            var info = new ExtendedLimits();
            info.Basic.LimitFlags = 0x400 | 0x2000; // DIE_ON_UNHANDLED_EXCEPTION | KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(ExtendedLimits))))
                throw new System.ComponentModel.Win32Exception();
            if (!AssignProcessToJobObject(job, process)) throw new System.ComponentModel.Win32Exception();
            return job;
        }
    }

    public sealed class PeInfo
    {
        public bool Is64;
        public int Subsystem;
        public List<string> Imports = new List<string>();
        public List<string> DelayImports = new List<string>();

        public static PeInfo Read(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(b, 0x3C);
            if (BitConverter.ToUInt32(b, pe) != 0x00004550) throw new InvalidDataException("not a PE file: " + path);
            int coff = pe + 4;
            int sections = BitConverter.ToUInt16(b, coff + 2);
            int optSize = BitConverter.ToUInt16(b, coff + 16);
            int opt = coff + 20;
            var info = new PeInfo();
            info.Is64 = BitConverter.ToUInt16(b, opt) == 0x20B;
            info.Subsystem = BitConverter.ToUInt16(b, opt + 68);
            int dirs = opt + (info.Is64 ? 112 : 96);
            int secTable = opt + optSize;
            Func<uint, int> rvaToOffset = rva =>
            {
                for (int i = 0; i < sections; i++)
                {
                    int s = secTable + i * 40;
                    uint va = BitConverter.ToUInt32(b, s + 12);
                    uint size = Math.Max(BitConverter.ToUInt32(b, s + 8), BitConverter.ToUInt32(b, s + 16));
                    uint raw = BitConverter.ToUInt32(b, s + 20);
                    if (rva >= va && rva < va + size) return (int)(rva - va + raw);
                }
                return -1;
            };
            Func<int, string> cstr = off =>
            {
                int end = off;
                while (end < b.Length && b[end] != 0) end++;
                return Encoding.ASCII.GetString(b, off, end - off);
            };
            uint impRva = BitConverter.ToUInt32(b, dirs + 8);
            if (impRva != 0)
            {
                for (int d = rvaToOffset(impRva); d >= 0; d += 20)
                {
                    uint nameRva = BitConverter.ToUInt32(b, d + 12);
                    if (nameRva == 0) break;
                    info.Imports.Add(cstr(rvaToOffset(nameRva)));
                }
            }
            uint delayRva = BitConverter.ToUInt32(b, dirs + 13 * 8);
            if (delayRva != 0)
            {
                for (int d = rvaToOffset(delayRva); d >= 0; d += 32)
                {
                    uint nameRva = BitConverter.ToUInt32(b, d + 4);
                    if (nameRva == 0) break;
                    info.DelayImports.Add(cstr(rvaToOffset(nameRva)));
                }
            }
            return info;
        }
    }
}
'@
}

function Quote-Argument([string] $a) {
    # The inverse of CommandLineToArgvW: backslashes double only before a quote.
    if ($a.Length -gt 0 -and $a -notmatch '[\s"]') { return $a }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    $slashes = 0
    foreach ($ch in $a.ToCharArray()) {
        if ($ch -eq '\') { $slashes++; continue }
        if ($ch -eq '"') { [void]$sb.Append('\' * (2 * $slashes + 1)); [void]$sb.Append('"') }
        else { [void]$sb.Append('\' * $slashes); [void]$sb.Append($ch) }
        $slashes = 0
    }
    [void]$sb.Append('\' * (2 * $slashes))
    [void]$sb.Append('"')
    return $sb.ToString()
}

if ($ArgumentsBase64) {
    $json = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($ArgumentsBase64))
    # Windows PowerShell 5.1's ConvertFrom-Json sends an array down the pipeline as one object.
    $parsed = ConvertFrom-Json $json
    $Arguments = @(foreach ($a in $parsed) { [string]$a })
}
$exePath = (Resolve-Path -LiteralPath $Exe).Path
$exeDir = Split-Path -Parent $exePath
if (-not $WorkingDirectory) { $WorkingDirectory = $exeDir }

$runtimeDirs = if ($NoMatlabRuntime) { @() } else { @((Join-Path $MatlabRoot 'runtime\win64'), (Join-Path $MatlabRoot 'bin\win64')) }
$childPath = ((@($PathFirst) + $runtimeDirs + @($env:Path -split ';')) | Where-Object { $_ }) -join ';'

$lines = New-Object System.Collections.Generic.List[string]
function Say([string] $s) { $script:lines.Add($s); Write-Host $s }
function Finish([int] $code) {
    if ($OutFile) {
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($OutFile, (($lines -join "`n") + "`n"), $utf8)
    }
    exit $code
}

# 1. Every import must resolve before anything runs.
$pe = [JGraphQuiet.PeInfo]::Read($exePath)
$system32 = [Environment]::GetFolderPath('System')
$searchDirs = @($exeDir, $system32, $env:windir) + @($childPath -split ';')
$unresolved = @()
foreach ($dll in @($pe.Imports) + @($pe.DelayImports)) {
    if ($dll -match '^(api|ext)-ms-') { continue }
    $found = $false
    foreach ($d in $searchDirs) {
        if ($d -and (Test-Path -LiteralPath (Join-Path $d $dll))) { $found = $true; break }
    }
    if (-not $found) { $unresolved += $dll }
}
$windowed = $pe.Subsystem -eq 2
if ($unresolved) {
    foreach ($u in $unresolved) { Say "UNRESOLVED`t$u" }
    Say "EXIT`tnot started (imports do not resolve)"
    Finish 97
}
if ($CheckOnly) {
    Say ("IMPORTS`tresolved ({0} + {1} delay-load); subsystem {2}" -f $pe.Imports.Count, $pe.DelayImports.Count, $(if ($windowed) { 'windows' } else { 'console' }))
    Finish 0
}
if ($windowed -and -not $AllowWindowed) {
    Say "EXIT`tnot started (a Windows GUI subsystem program needs -AllowWindowed, with the user's leave)"
    Finish 96
}

# 3. The child inherits this error mode.
$SEM_FAILCRITICALERRORS = 0x0001; $SEM_NOGPFAULTERRORBOX = 0x0002; $SEM_NOOPENFILEERRORBOX = 0x8000
[void][JGraphQuiet.Native]::SetErrorMode($SEM_FAILCRITICALERRORS -bor $SEM_NOGPFAULTERRORBOX -bor $SEM_NOOPENFILEERRORBOX)

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exePath
$psi.Arguments = (@($Arguments | ForEach-Object { Quote-Argument $_ }) -join ' ')
$psi.WorkingDirectory = $WorkingDirectory
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.EnvironmentVariables['PATH'] = $childPath
foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }

# Windows PowerShell's Process.Start opens the child's stdin with a writer in Console.InputEncoding
# and flushes that encoding's byte order mark into the pipe at once, so the child's stdin would not
# be empty: R2025b's input and JGraph's both read EF BB BF as a line (found in stage K1). The writer
# takes the encoding at Start, so one with no preamble is put in place for that call only.
$savedInput = $null
try { $savedInput = [Console]::InputEncoding; [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { $savedInput = $null }
$watch = [System.Diagnostics.Stopwatch]::StartNew()
try { $proc = [System.Diagnostics.Process]::Start($psi) }
finally { if ($savedInput) { try { [Console]::InputEncoding = $savedInput } catch { } } }
# The child runs for a moment before it joins the job; the error mode covers the loader's part.
$job = [JGraphQuiet.Native]::QuietJob($proc.Handle)
$proc.StandardInput.Close()
$outTask = $proc.StandardOutput.ReadToEndAsync()
$errTask = $proc.StandardError.ReadToEndAsync()
$timedOut = -not $proc.WaitForExit($TimeoutSeconds * 1000)
if ($timedOut) {
    & taskkill.exe /PID $proc.Id /T /F 2>&1 | Out-Null
    [void]$proc.WaitForExit(10000)
}
$seconds = $watch.Elapsed.TotalSeconds
$stdout = ($outTask.Result -replace "`r`n", "`n").TrimEnd("`n")
$stderr = ($errTask.Result -replace "`r`n", "`n").TrimEnd("`n")
if ($stdout) { foreach ($l in ($stdout -split "`n")) { Say $l } }
if ($stderr) { foreach ($l in ($stderr -split "`n")) { Say "STDERR`t$l" } }
if ($timedOut) {
    Say "EXIT`tTIMEOUT after $TimeoutSeconds s (process tree killed)"
    Say ("SECONDS`t{0:N1}" -f $seconds)
    Finish 98
}
$code = $proc.ExitCode
$shown = if ($code -lt 0 -or $code -gt 0xFFFF) { '{0} (0x{1:X8})' -f $code, $code } else { "$code" }
Say "EXIT`t$shown"
Say ("SECONDS`t{0:N1}" -f $seconds)
Finish $code
