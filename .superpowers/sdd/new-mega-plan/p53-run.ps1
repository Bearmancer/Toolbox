$ErrorActionPreference = 'Stop'
$isoRoot = 'C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin'
$worktree = 'C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2'
$logRoot = Join-Path $worktree '.superpowers\sdd\new-mega-plan'

foreach ($discNumber in 5..9) {
    $iso = Join-Path $isoRoot "Disc $discNumber\Disc $discNumber.iso"
    $log = Join-Path $logRoot "p53-disc$discNumber-cli.log"
    $err = Join-Path $logRoot "p53-disc$discNumber-cli.err.log"
    & dotnet run --project (Join-Path $worktree 'src\App') -- audio sacd-convert $iso --keep-iso 2>&1 | Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
