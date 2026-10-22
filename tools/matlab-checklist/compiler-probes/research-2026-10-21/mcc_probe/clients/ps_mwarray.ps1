param([string]$Dll, [string]$MWArray)
$ErrorActionPreference = 'Stop'
$sw = [Diagnostics.Stopwatch]::StartNew()
Add-Type -Path $MWArray
Add-Type -Path $Dll
Write-Output ("CLR " + [Environment]::Version + " loaded assemblies in " + $sw.ElapsedMilliseconds + " ms")
$sw.Restart()
$c = New-Object JgProbe.addtwoClass
Write-Output ("construct: " + $sw.ElapsedMilliseconds + " ms")
$sw.Restart()
$a = New-Object MathWorks.MATLAB.NET.Arrays.MWNumericArray -ArgumentList ([double]1.0)
$b = New-Object MathWorks.MATLAB.NET.Arrays.MWNumericArray -ArgumentList ([double]2.0)
$r = $c.addtwo($a, $b)
Write-Output ("first call: " + $sw.ElapsedMilliseconds + " ms")
Write-Output ("addtwo(1,2) -> " + $r.GetType().FullName + " dims=[" + ($r.Dimensions -join ',') + "] ToString=<<" + $r.ToString() + ">> scalar=" + $r.ToScalarDouble())
$m = $c.multi(3, [MathWorks.MATLAB.NET.Arrays.MWArray[]]@($a, $b, $b))
for ($i = 0; $i -lt $m.Length; $i++) { Write-Output ("multi out $i -> " + $m[$i].GetType().Name + " <<" + $m[$i].ToString() + ">>") }
$s = $c.shapes(6)
for ($i = 0; $i -lt $s.Length; $i++) { Write-Output ("shapes out $i -> " + $s[$i].GetType().Name + " dims=[" + ($s[$i].Dimensions -join ',') + "] <<" + ($s[$i].ToString() -replace "`r?`n", ' | ') + ">>") }
try { $null = $c.addtwo(3, $a, $b) } catch { Write-Output ("addtwo nargout=3 -> " + $_.Exception.InnerException.GetType().FullName + ": " + $_.Exception.InnerException.Message.Trim()) }
$c.Dispose()
Write-Output ("total " + $sw.ElapsedMilliseconds + " ms")
