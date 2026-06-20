# Live Seq Pipeline Probe — run against localhost:5341 and actual buffer file

$logDir = Join-Path $env:USERPROFILE ".cache\logs\app"
$buf = Join-Path $logDir "seq-buffer-20260620_004.clef"
$book = Join-Path $logDir "seq-buffer.bookmark"

Write-Host "=== 1. Live POST to /api/events/raw (what the sink actually calls) ===" -ForegroundColor Cyan
$payload = '{"@t":"2026-06-20T21:00:00Z","@mt":"probe","@l":"Information"}'
try {
    $r = Invoke-WebRequest -Uri "http://localhost:5341/api/events/raw" -Method POST -Body $payload -ContentType "application/json" -UseBasicParsing -TimeoutSec 5
    Write-Host "  status: $($r.StatusCode) $($r.StatusDescription)" -ForegroundColor Green
    Write-Host "  body:   $($r.Content.Substring(0, [Math]::Min($r.Content.Length, 200)))"
} catch {
    $code = $_.Exception.Response.StatusCode.value__
    $body = $null
    try { $body = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd() } catch {}
    Write-Host "  status: $code" -ForegroundColor Red
    Write-Host "  body:   $body"
    Write-Host "  ---"
    Write-Host "  This is what the sink sees when it tries to ship. 401 = bad/missing API key. 500 = payload rejected. 400 = bad payload. 413 = too large."
}

Write-Host ""
Write-Host "=== 2. Try with no API key (Seq 5.x requires an API key for ingestion) ===" -ForegroundColor Cyan
try {
    $r = Invoke-WebRequest -Uri "http://localhost:5341/api/events/raw?apiKey=" -Method POST -Body $payload -ContentType "application/json" -UseBasicParsing -TimeoutSec 5
    Write-Host "  status: $($r.StatusCode)"
} catch {
    $code = $_.Exception.Response.StatusCode.value__
    Write-Host "  status: $code"
}

Write-Host ""
Write-Host "=== 3. Inspect the actual bookmark file format ===" -ForegroundColor Cyan
if (Test-Path $book) {
    $raw = [IO.File]::ReadAllBytes($book)
    Write-Host "  raw bytes (length=$($raw.Length)): $([BitConverter]::ToString($raw[0..([Math]::Min(60, $raw.Length-1))]))"
    Write-Host "  as text: '$(Get-Content $book -Raw)'"
    $text = (Get-Content $book -Raw).Trim()
    $parts = $text -split ':::'
    Write-Host "  pos: $($parts[0])  (parsed as Int64 = $([int64]::Parse($parts[0])))"
    Write-Host "  file: $($parts[1])"
    Write-Host "  file exists: $(Test-Path $parts[1])"
    Write-Host "  file size:  $((Get-Item $parts[1]).Length)"
}

Write-Host ""
Write-Host "=== 4. Inspect the last bytes of the buffer (the most recent append) ===" -ForegroundColor Cyan
$fs = [IO.File]::Open($buf, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
$fs.Seek(-[Math]::Min(500, $fs.Length), [IO.SeekOrigin]::End) | Out-Null
$tail = New-Object char[] 500
$read = $fs.Read($tail, 0, 500)
$fs.Close()
$tailText = -join $tail[0..($read-1)]
Write-Host "  last $read bytes of buffer:"
Write-Host "  ---"
Write-Host "  $tailText"
Write-Host "  ---"

Write-Host ""
Write-Host "=== 5. Parse the timestamps of events in the buffer (first 5 + last 5) ===" -ForegroundColor Cyan
$lines = Get-Content $buf
$ts = @()
foreach ($line in $lines) {
    if ($line -match '"@t":"([^"]+)"') { $ts += [datetime]::Parse($matches[1]) }
}
Write-Host "  total events in buffer: $($ts.Count)"
$firstTs = $ts[0]
$lastTs = $ts[-1]
Write-Host "  first event:  $($firstTs.ToString('o'))"
Write-Host "  last event:   $($lastTs.ToString('o'))"
$spanMin = ($lastTs - $firstTs).TotalMinutes
$ageMin = ([DateTime]::UtcNow - $lastTs).TotalMinutes
Write-Host ("  span:         {0:F1} min" -f $spanMin)
Write-Host ("  last event age: {0:F1} min ago" -f $ageMin)

Write-Host ""
Write-Host "=== 6. Decode the bookmark integer to confirm matching ===" -ForegroundColor Cyan
$text = (Get-Content $book -Raw).Trim()
$parts = $text -split ':::'
$pos = [int64]::Parse($parts[0])
$fileSize = (Get-Item $parts[1]).Length
Write-Host "  bookmark pos: $pos"
Write-Host "  current file size: $fileSize"
Write-Host "  delta (unread bytes): $($fileSize - $pos)"

Write-Host ""
Write-Host "=== 7. Is the sink process running? (would explain why forwarder is silent) ===" -ForegroundColor Cyan
Write-Host "  app processes: $((Get-Process -Name 'App','dotnet' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*New*' } | Select-Object Id,ProcessName,StartTime | Format-List | Out-String).Trim())"
Write-Host "  dotnet processes:"
Get-Process -Name 'dotnet' -ErrorAction SilentlyContinue | Select-Object Id, StartTime, @{n='Cmd';e={(Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)").CommandLine}} | Format-Table -Wrap -AutoSize | Out-String | Write-Host

Write-Host ""
Write-Host "=== 8. What did the Sink see in the last error? (selflog file) ===" -ForegroundColor Cyan
$selflogPaths = @(
    (Join-Path $logDir "selflog.txt"),
    (Join-Path $env:USERPROFILE ".cache\logs\selflog.txt"),
    (Join-Path $env:TEMP "serilog-selflog.txt")
)
foreach ($p in $selflogPaths) {
    if (Test-Path $p) {
        Write-Host "  found: $p"
        Get-Content $p -Tail 20 | ForEach-Object { Write-Host "    $_" }
    }
}
Write-Host "  (no Serilog selflog output found — SelfLog is never enabled, all forwarder errors are silently dropped)"
