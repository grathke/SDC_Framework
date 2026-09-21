<#
    measure-session-close.ps1

    How long does a closed browser tab take to release SDC.Framework.exe?

    Two clocks, because they are not the same event and may be far apart:

      OnClose   the application learning the session has gone - the "VirtualUI session closed"
                line in startup.log
      Exit      the process actually disappearing, which is when the exe stops being locked
                and a build can run again

    Reconnection timeout on the application profile reads 5 seconds, and OnClose has been
    measured at 156 seconds and at about 210. Five cannot produce a hundred and fifty-six, so
    something upstream of that grace is taking the time. This measures rather than explains.
    See THINFINITY_NOTES.md, "Closing, and what does not detect it".

    Run it AFTER the application is up in the browser. It waits for you to close the tab.
#>

[CmdletBinding()]
param(
    [int] $CapSeconds = 600
)

$ErrorActionPreference = 'Stop'

$procs = @(Get-Process SDC.Framework -ErrorAction SilentlyContinue)
if ($procs.Count -eq 0) {
    Write-Host "SDC.Framework is not running. Start it in the browser first, then run this again." -ForegroundColor Yellow
    exit 1
}
if ($procs.Count -gt 1) {
    Write-Host "More than one SDC.Framework is running - that makes the measurement ambiguous:" -ForegroundColor Yellow
    $procs | Select-Object Id, StartTime | Format-Table | Out-String | Write-Host
    Write-Host "Close all but the one you are about to test, then run this again." -ForegroundColor Yellow
    exit 1
}

$proc    = $procs[0]
$exePath = $proc.Path
$logPath = Join-Path (Split-Path $exePath -Parent) 'startup.log'

Write-Host ""
Write-Host "  PID      : $($proc.Id)"
Write-Host "  Started  : $($proc.StartTime.ToString('HH:mm:ss'))"
Write-Host "  Exe      : $exePath"
Write-Host "  Log      : $logPath"
if (-not (Test-Path $logPath)) {
    Write-Host "  WARNING  : no startup.log yet - only the process clock will be measured." -ForegroundColor Yellow
}

# Everything already in the log is history. Only lines written from here on count.
$baseline = 0
if (Test-Path $logPath) { $baseline = (Get-Item $logPath).Length }

Write-Host ""
Write-Host "Close the browser tab now, then press Enter here IMMEDIATELY." -ForegroundColor Cyan
Read-Host "Press Enter at the moment the tab is closed" | Out-Null

$clock     = [Diagnostics.Stopwatch]::StartNew()
$onCloseAt = $null
$exitAt    = $null
$nextTick  = 15

Write-Host ""
while ($clock.Elapsed.TotalSeconds -lt $CapSeconds) {

    if (-not $onCloseAt -and (Test-Path $logPath)) {
        $len = (Get-Item $logPath).Length
        if ($len -gt $baseline) {
            $tail = Get-Content $logPath -Tail 40 -ErrorAction SilentlyContinue
            if ($tail -match 'VirtualUI session closed') {
                $onCloseAt = [math]::Round($clock.Elapsed.TotalSeconds, 1)
                Write-Host ("  {0,7:N1}s  OnClose reached the application" -f $onCloseAt) -ForegroundColor Green
            }
        }
    }

    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) {
        $exitAt = [math]::Round($clock.Elapsed.TotalSeconds, 1)
        Write-Host ("  {0,7:N1}s  process gone - the exe is free" -f $exitAt) -ForegroundColor Green
        break
    }

    if ($clock.Elapsed.TotalSeconds -ge $nextTick) {
        Write-Host ("  {0,7:N0}s  still running..." -f $clock.Elapsed.TotalSeconds) -ForegroundColor DarkGray
        $nextTick += 15
    }

    Start-Sleep -Milliseconds 500
}

$clock.Stop()

Write-Host ""
Write-Host "RESULT" -ForegroundColor Cyan
if ($onCloseAt) { Write-Host ("  OnClose       {0,7:N1} s" -f $onCloseAt) }
else            { Write-Host  "  OnClose         never seen in startup.log" -ForegroundColor Yellow }

if ($exitAt) { Write-Host ("  Process exit  {0,7:N1} s" -f $exitAt) }
else         { Write-Host ("  Process exit    still running after {0:N0} s - gave up" -f $clock.Elapsed.TotalSeconds) -ForegroundColor Yellow }

Write-Host ""
if ($exitAt -and $exitAt -le 20) {
    Write-Host "Near the 5-second reconnection timeout. The old 156s measurement was something else," -ForegroundColor Yellow
    Write-Host "and whatever it was is no longer happening." -ForegroundColor Yellow
}
elseif ($exitAt) {
    Write-Host "Far beyond the 5-second reconnection timeout, so the delay is not that setting." -ForegroundColor Yellow
    if ($onCloseAt -and ($exitAt - $onCloseAt) -gt 10) {
        Write-Host ("The application then took a further {0:N1}s to exit after OnClose - that part is ours." -f ($exitAt - $onCloseAt)) -ForegroundColor Yellow
    }
    else {
        Write-Host "The application exited promptly once told, so the wait is the server's detection" -ForegroundColor Yellow
        Write-Host "of a dead connection - a question for Cybele support, not a change here." -ForegroundColor Yellow
    }
}
Write-Host ""
