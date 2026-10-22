param([string]$Dll, [string]$MWArray)
Add-Type -Path $MWArray
Add-Type -Path $Dll
$c = New-Object JgProbe.addtwoClass
foreach ($n in 1..6) {
    try {
        $s = $c.shapes($n)
        $desc = for ($i = 0; $i -lt $s.Length; $i++) { $s[$i].GetType().Name + "[" + ($s[$i].Dimensions -join 'x') + "]<<" + ($s[$i].ToString() -replace "`r?`n", ' | ') + ">>" }
        Write-Output ("shapes($n) ok: " + ($desc -join ' ;; '))
    } catch {
        $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
        Write-Output ("shapes($n) failed: " + $e.GetType().FullName + ": " + ($e.Message -replace "`r?`n", ' '))
    }
}
try { $r = $c.shapes(); Write-Output ("shapes() single -> " + $r.GetType().Name) } catch { Write-Output ("shapes() failed: " + $_.Exception.Message) }
$a = New-Object MathWorks.MATLAB.NET.Arrays.MWNumericArray -ArgumentList ([double]1.0)
foreach ($n in 0..3) {
    try { $r = $c.addtwo($n, $a, $a); Write-Output ("addtwo nargout=$n -> length " + $r.Length) }
    catch { $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }; Write-Output ("addtwo nargout=$n failed: " + $e.GetType().FullName + ": " + ($e.Message -replace "`r?`n", ' ')) }
}
try { $r = $c.addtwo((New-Object MathWorks.MATLAB.NET.Arrays.MWCharArray -ArgumentList "3"), (New-Object MathWorks.MATLAB.NET.Arrays.MWCharArray -ArgumentList "4")); Write-Output ("addtwo('3','4') -> " + $r.GetType().Name + " " + $r) } catch { Write-Output ("char call failed: " + $_.Exception.Message) }
try { $r = $c.addtwo([MathWorks.MATLAB.NET.Arrays.MWArray][int]1, [MathWorks.MATLAB.NET.Arrays.MWArray][int]2); Write-Output ("addtwo(int,int) implicit -> " + $r.GetType().Name + " " + $r) } catch { Write-Output ("int call failed: " + $_.Exception.Message) }
$c.Dispose()
