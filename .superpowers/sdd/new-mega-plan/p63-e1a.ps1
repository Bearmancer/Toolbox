$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$source = 'C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 3\Disc 3\Disc 3.dff'
$root = Join-Path (Get-Location) '.superpowers\sdd\new-mega-plan\e1a'
$dummy = Join-Path $root 'disc3-60s.dff'
$out88 = Join-Path $root 'out-88200-24'
$out44 = Join-Path $root 'out-44100-16'
$report = Join-Path (Get-Location) '.superpowers\sdd\new-mega-plan\p63-e1a-report.md'
$payloadBytes = 42336000L

function Set-UInt64BigEndian([byte[]] $buffer, [int] $offset, [UInt64] $value) {
    for ($i = 0; $i -lt 8; $i++) {
        $buffer[$offset + $i] = [byte](($value -shr (56 - 8 * $i)) -band 0xff)
    }
}

function Get-UInt64BigEndian([byte[]] $buffer, [int] $offset) {
    [UInt64] $value = 0
    for ($i = 0; $i -lt 8; $i++) {
        $value = ($value -shl 8) -bor [UInt64]$buffer[$offset + $i]
    }
    return $value
}

if (-not (Test-Path -LiteralPath $source)) { throw "Missing real DFF: $source" }
New-Item -ItemType Directory -Path $root,$out88,$out44 -Force | Out-Null

$input = [System.IO.File]::OpenRead($source)
try {
    $prefix = New-Object byte[] 16
    $fver = New-Object byte[] 16
    $prop = New-Object byte[] 100
    $dsdHeader = New-Object byte[] 12
    $payload = New-Object byte[] $payloadBytes
    if ($input.Read($prefix, 0, 16) -ne 16) { throw 'Short FRM8 header' }
    if ($input.Read($fver, 0, 16) -ne 16) { throw 'Short FVER chunk' }
    if ($input.Read($prop, 0, 100) -ne 100) { throw 'Short PROP chunk' }
    if ($input.Read($dsdHeader, 0, 12) -ne 12) { throw 'Short DSD chunk header' }
    $remaining = $payloadBytes
    $offset = 0
    while ($remaining -gt 0) {
        $read = $input.Read($payload, $offset, [int][Math]::Min($remaining, 1048576))
        if ($read -le 0) { throw "Short DSD payload at $offset" }
        $offset += $read
        $remaining -= $read
    }
}
finally { $input.Dispose() }

Set-UInt64BigEndian $prefix 4 ([uint64](42336144 - 12))
[System.Text.Encoding]::ASCII.GetBytes('DSD ').CopyTo($dsdHeader, 0)
Set-UInt64BigEndian $dsdHeader 4 ([uint64]$payloadBytes)

$output = [System.IO.File]::Create($dummy)
try {
    $output.Write($prefix, 0, $prefix.Length)
    $output.Write($fver, 0, $fver.Length)
    $output.Write($prop, 0, $prop.Length)
    $output.Write($dsdHeader, 0, $dsdHeader.Length)
    $output.Write($payload, 0, $payload.Length)
}
finally { $output.Dispose() }

$dummyBytes = [System.IO.File]::ReadAllBytes($dummy)
$sha = ([System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::HashData($dummyBytes))).Replace('-', '').ToLowerInvariant()
$declared = Get-UInt64BigEndian $dummyBytes 4
if ($dummyBytes.Length -ne 42336144 -or ($declared % 2) -ne 0 -or $declared -ne ($dummyBytes.Length - 12)) {
    throw "Dummy invariant failed: bytes=$($dummyBytes.Length) declared=$declared"
}

$saracon = 'C:\Program Files (x86)\Weiss Engineering\Saracon\saracon.exe'
$sox = 'C:\Program Files (x86)\sox-14-4-2\sox.exe'
$runs = @(
    @{ Name = '88200/24'; Rate = '88200'; Bits = '24bit'; Dir = $out88 },
    @{ Name = '44100/16'; Rate = '44100'; Bits = '16bit'; Dir = $out44 }
)
$rows = @()
foreach ($run in $runs) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    & $saracon -c d2p -r $run.Rate -f wav -n $run.Bits -d tpdf -g 0.00 -T -V all -t $run.Dir $dummy 2>&1 | Tee-Object (Join-Path $root ($run.Name.Replace('/','-') + '-saracon.log'))
    $exit = $LASTEXITCODE
    $watch.Stop()
    $wav = Join-Path $run.Dir 'disc3-60s-d2p.wav'
    if (-not (Test-Path -LiteralPath $wav)) { $wav = Join-Path $run.Dir 'disc3-60s.wav' }
    $stats = & $sox $wav -n stats 2>&1 | Tee-Object (Join-Path $root ($run.Name.Replace('/','-') + '-sox-stats.log'))
    $peakLine = $stats | Where-Object { $_ -match 'Pk lev dB' } | Select-Object -Last 1
    $peak = if ($peakLine -match 'Pk lev dB\s+(-?\d+\.\d+|-inf)') { [double]$Matches[1] } else { throw "No peak in $($run.Name) stats" }
    $rows += [pscustomobject]@{ Name=$run.Name; Exit=$exit; Seconds=[Math]::Round($watch.Elapsed.TotalSeconds, 3); WavBytes=(Get-Item $wav).Length; Peak=$peak; PeakLine=$peakLine }
}

$delta = $rows[0].Peak - $rows[1].Peak
$gain88 = [Math]::Min(6.0, -0.5 - $rows[0].Peak)
$gain44 = [Math]::Min(6.0, -0.5 - $rows[1].Peak)
$verdict = if ([Math]::Abs($delta) -lt 0.05) { 'Immaterial delta; no re-conversion indicated.' } elseif ($delta -gt 0) { 'Historical gain conservative; level was lost.' } else { 'Historical gain optimistic; clipping checks required.' }
$reportLines = @(
    '# P6.3 Experiment E1-A',
    '',
    "Source: $source",
    "Dummy: $dummy",
    '',
    '## Dummy invariants',
    '',
    "- Payload bytes: $payloadBytes",
    "- Total bytes: $($dummyBytes.Length)",
    "- FRM8 ckDataSize: $declared",
    "- SHA-256: $sha",
    '',
    '## Runs',
    '',
    '| Run | Exit | Seconds | WAV bytes | Peak | Raw peak line |',
    '|---|---:|---:|---:|---:|---|'
)
$reportLines += $rows | ForEach-Object { "| $($_.Name) | $($_.Exit) | $($_.Seconds) | $($_.WavBytes) | $($_.Peak) | $($_.PeakLine) |" }
$reportLines += @('', "- Delta (peak88 - peak44): $delta dB", "- Gain 88.2/24: $gain88 dB", "- Gain 44.1/16: $gain44 dB", "- Verdict: $verdict")
$reportLines | Set-Content -LiteralPath $report -Encoding utf8
