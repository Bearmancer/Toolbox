param (
    [string]$Path = ".\",
    [string]$ExcludePattern = "bin|obj|\.git|\.vs"
)

Write-Host "Scanning for bad patterns in $Path..." -ForegroundColor Cyan

$patterns = @(
    '""',
    '\bnull\b',
    '!',
    'catch\s*\{\s*\}'
)

foreach ($pattern in $patterns) {
    Write-Host "Searching for pattern: $pattern" -ForegroundColor Yellow
    Get-ChildItem -Path $Path -Recurse -File | 
        Where-Object { $_.FullName -notmatch $ExcludePattern -and $_.Extension -in ".cs", ".ps1", ".xml", ".json", ".csproj", ".props" } |
        Select-String -Pattern $pattern | 
        Select-Object Path, LineNumber, Line | 
        Format-Table -AutoSize
}
Write-Host "Scan complete." -ForegroundColor Cyan
