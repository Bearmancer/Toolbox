#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the Toolbox SACD converter with all output live on screen and live to a log file.

.DESCRIPTION
    One pipeline. Native stderr is folded into the success stream with 2>&1, each line is
    stringified, then written to both the console and an auto-flushing StreamWriter.

    No Start-Process. No System.Diagnostics.Process. No Register-ObjectEvent. No PID.
    Those were the cause of the vanished output, not the cure.

.EXAMPLE
    .\disc13-run.ps1
    .\disc13-run.ps1 -Disc 4
    .\disc13-run.ps1 -Iso 'D:\rips\Disc 7\Disc 7.iso' -AppVerbose
    .\disc13-run.ps1 -Disc 13 -NoBuild
#>
[CmdletBinding()]
param(
    [int] $Disc,

    [string] $Iso = 'C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin\Disc 13\Disc 13.iso',

    [string] $RepoRoot = 'C:\Users\Lance\Dev\Toolbox',

    [string] $MusicRoot = 'C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin',

    [string] $LogPath,

    [bool] $KeepIso = $true,

    [switch] $AppVerbose,

    [switch] $NoBuild
)

Set-StrictMode -Version Latest

$ErrorActionPreference = 'Continue'

if ($PSBoundParameters.ContainsKey('Disc')) {
    $Iso = Join-Path $MusicRoot ('Disc {0}\Disc {0}.iso' -f $Disc)
}

if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
    throw "Repo root not found: $RepoRoot"
}
if (-not (Test-Path -LiteralPath $Iso -PathType Leaf)) {
    throw "ISO not found: $Iso"
}

if (-not $LogPath) {
    $artifactDir = Join-Path $RepoRoot '.superpowers\sdd\new-mega-plan'
    if (-not (Test-Path -LiteralPath $artifactDir -PathType Container)) {
        New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
    }
    $discTag = [IO.Path]::GetFileNameWithoutExtension($Iso) -replace '[^\w\-]', '-'
    $stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'
    $LogPath = Join-Path $artifactDir "sacd-$discTag-$stamp.log"
}

$logDir = Split-Path -Parent $LogPath
if ($logDir -and -not (Test-Path -LiteralPath $logDir -PathType Container)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$dotnetArgs = @(
    'run'
    '--project', (Join-Path $RepoRoot 'src\App')
)
if ($NoBuild) { $dotnetArgs += '--no-build' }
$dotnetArgs += '--'
$dotnetArgs += @('audio', 'sacd-convert', $Iso)
if ($KeepIso)    { $dotnetArgs += '--keep-iso' }
if ($AppVerbose) { $dotnetArgs += '--verbose' }

$writer = [System.IO.StreamWriter]::new(
    $LogPath,
    $false,
    [System.Text.UTF8Encoding]::new($false)
)
$writer.AutoFlush = $true

function Write-Both([string] $Text) {
    [Console]::Out.WriteLine($Text)
    $script:writer.WriteLine($Text)
}

$prevOutputEncoding = [Console]::OutputEncoding
$exitCode = 1
$pushed = $false

try {
    [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

    Push-Location -LiteralPath $RepoRoot
    $pushed = $true

    Write-Both "ISO     : $Iso"
    Write-Both "WorkDir : $RepoRoot"
    Write-Both "Log     : $LogPath"
    Write-Both "Command : dotnet $($dotnetArgs -join ' ')"
    Write-Both "Started : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Both ('-' * 78)

    if (-not $NoBuild) {
        Write-Both '[build] dotnet build --nologo'
        & dotnet build (Join-Path $RepoRoot 'Toolbox.slnx') --nologo 2>&1 |
            ForEach-Object { Write-Both ([string]$_) }

        if ($LASTEXITCODE -ne 0) {
            Write-Both ('-' * 78)
            Write-Both "BUILD FAILED (exit $LASTEXITCODE) - not running the app."
            return $LASTEXITCODE
        }
        Write-Both ('-' * 78)
    }

    & dotnet @dotnetArgs 2>&1 | ForEach-Object { Write-Both ([string]$_) }

    $exitCode = $LASTEXITCODE

    Write-Both ('-' * 78)
    Write-Both "Finished : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Both "Exit code: $exitCode"
    Write-Both "Log      : $LogPath"
}
finally {
    if ($pushed) { Pop-Location }
    if ($writer) { $writer.Flush(); $writer.Dispose() }
    [Console]::OutputEncoding = $prevOutputEncoding
}

exit $exitCode
