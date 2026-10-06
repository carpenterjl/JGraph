# Drives u9b_sendfrom: one R2025b session after another, each starting after the value that left
# the last one's bridge dead, until every value has been tried. Collects the lines in u9b_sends.out.
$dir = $PSScriptRoot
$all = Join-Path $dir 'u9b_sends.out'
Remove-Item -LiteralPath $all -ErrorAction SilentlyContinue
$start = 1
for ($round = 0; $round -lt 60; $round++) {
    Set-Content -LiteralPath (Join-Path $dir 'sendfrom.txt') -Value $start
    & (Join-Path $dir 'run-probe.ps1') -Name u9b_sendfrom -TimeoutSec 600 | Out-Null
    $lines = Get-Content (Join-Path $dir 'u9b_sendfrom.out')
    $lines | Add-Content -LiteralPath $all
    $dead = $lines | Where-Object { $_ -match '^dead after (\d+)' } | Select-Object -First 1
    $never = $lines | Where-Object { $_ -match '^oddsend (\d+) page never ready' } | Select-Object -First 1
    if ($dead) { $start = [int]([regex]::Match($dead, '\d+').Value) + 1; continue }
    if ($never) { $start = [int]([regex]::Match($never, '\d+').Value); "never ready at $start" | Add-Content -LiteralPath $all; $start++; continue }
    break
}
'done' | Add-Content -LiteralPath $all
