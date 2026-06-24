#!/usr/bin/env pwsh
# Scans C# source files for silent failure patterns — fallback values instead of throwing.
# Usage: ./scripts/scan-silent-failures.ps1 [path]

param(
    [string]$Path = "src"
)

$patterns = @(
    @{ Pattern = '\?\? ""'; Label = "Empty string fallback" },
    @{ Pattern = '\?\? string\.Empty'; Label = "string.Empty fallback" },
    @{ Pattern = '\?\? 0\b'; Label = "Zero int fallback" },
    @{ Pattern = '\?\? 0L'; Label = "Zero long fallback" },
    @{ Pattern = '\?\? 0\.0'; Label = "Zero double fallback" },
    @{ Pattern = '\?\? false'; Label = "False fallback (check: may be valid for null-safe bool)" },
    @{ Pattern = 'TimeSpan\.Zero'; Label = "TimeSpan.Zero (check: UTC offset vs fallback)" },
    @{ Pattern = 'DateTime\.MinValue'; Label = "DateTime.MinValue sentinel" },
    @{ Pattern = 'DateTimeOffset\.MinValue'; Label = "DateTimeOffset.MinValue sentinel" },
    @{ Pattern = 'DateTime\.Now'; Label = "DateTime.Now (local) — use DateTimeOffset or UtcNow" },
    @{ Pattern = 'DateTime\.UtcNow'; Label = "DateTime.UtcNow — prefer DateTimeOffset.UtcNow" },
    @{ Pattern = 'catch\s*\(\s*Exception[^)]*\)\s*\{[^}]*\}'; Label = "Catch block (check: swallowed or re-thrown?)" },
    @{ Pattern = 'catch\s*\{\s*\}'; Label = "Empty catch block — silent swallow" },
    @{ Pattern = 'catch\s*\(\s*\)'; Label = "Parameterless catch — catches everything" },
    @{ Pattern = 'new DateTime\('; Label = "DateTime ctor — DateTimeKind.Unspecified by default" },
    @{ Pattern = 'DateTime\.Parse\('; Label = "DateTime.Parse — ambiguous, use DateTimeOffset.Parse" },
    @{ Pattern = 'DateTime\.TryParse\('; Label = "DateTime.TryParse — ambiguous, use DateTimeOffset.TryParse" },
    @{ Pattern = '\.ToString\(".*[Hhmstfz]'; Label = "DateTime format string — check if timezone-aware" }
)

Write-Host "=== Silent Failure Scanner ===" -ForegroundColor Cyan
Write-Host "Scanning: $Path`n" -ForegroundColor Gray

$total = 0

foreach ($p in $patterns) {
    $results = rg -n $p.Pattern -t cs -g "!bin" -g "!obj" $Path 2>$null
    if ($results) {
        Write-Host "--- $($p.Label) ---" -ForegroundColor Yellow
        $results | ForEach-Object {
            Write-Host "  $_" -ForegroundColor White
            $total++
        }
        Write-Host ""
    }
}

Write-Host "=== Found $total potential silent failures ===" -ForegroundColor Cyan
