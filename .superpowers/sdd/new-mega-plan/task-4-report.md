# P0.4 Media Risk Inventory

Scope: `C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin` ISO tree and `C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)` output tree.

Inventory found 20 ISOs. 14 output CUEs exist. Six ISO output trees are absent. No media mutation performed.

## Per-disc inventory

| Disc | Final-track duration (s) | Under 30 s | Output directory / classification | CUE tracks |
|---:|---:|:---:|---|---:|
| 1 | 280.296190 | No | Exists; reprocessed: CUE + 19 FLAC | 19 |
| 2 | 211.456190 | No | Exists; reprocessed: CUE + 8 FLAC | 8 |
| 3 | N/A — `NO_FINAL_FLAC` | BLOCKED | Exists; reprocessed/incomplete: XML + DFF + CUE, 0 FLAC | 4 |
| 4 | BLOCKED | BLOCKED | `OUTPUT_DIR_EXISTS=False`; `NO_CUE`; owner: **Lance** | N/A (no CUE exists; fresh-output absence observed) |
| 5 | BLOCKED | BLOCKED | `OUTPUT_DIR_EXISTS=False`; `NO_CUE`; owner: **Lance** | N/A (no CUE exists; fresh-output absence observed) |
| 6 | BLOCKED | BLOCKED | `OUTPUT_DIR_EXISTS=False`; `NO_CUE`; owner: **Lance** | N/A (no CUE exists; fresh-output absence observed) |
| 7 | BLOCKED | BLOCKED | `OUTPUT_DIR_EXISTS=False`; `NO_CUE`; owner: **Lance** | N/A (no CUE exists; fresh-output absence observed) |
| 8 | BLOCKED | BLOCKED | `OUTPUT_DIR_EXISTS=False`; `NO_CUE`; owner: **Lance** | N/A (no CUE exists; fresh-output absence observed) |
| 9 | BLOCKED | BLOCKED | `OUTPUT_DIR_EXISTS=False`; `NO_CUE`; owner: **Lance** | N/A (no CUE exists; fresh-output absence observed) |
| 10 | 535.469524 | No | Exists; reprocessed: CUE + 7 FLAC | 7 |
| 11 | 1473.402857 | No | Exists; reprocessed: CUE + 4 FLAC | 4 |
| 12 | 725.069524 | No | Exists; reprocessed: CUE + 10 FLAC | 10 |
| 13 | 653.642857 | No | Exists; reprocessed: CUE + 9 FLAC | 9 |
| 14 | 281.749524 | No | Exists; reprocessed: CUE + 15 FLAC | 15 |
| 15 | 565.442857 | No | Exists; reprocessed: CUE + 7 FLAC | 7 |
| 16 | 1902.629524 | No | Exists; reprocessed: CUE + 6 FLAC | 6 |
| 17 | 388.216190 | No | Exists; reprocessed: CUE + 8 FLAC | 8 |
| 18 | 590.096190 | No | Exists; reprocessed: CUE + 9 FLAC | 9 |
| 19 | 441.869524 | No | Exists; reprocessed: CUE + 12 FLAC | 12 |
| 20 | 701.269524 | No | Exists; reprocessed: CUE + 8 FLAC | 8 |

14 CUE discs: 1, 2, 3, 10–20. 13 final FLAC durations measured. No measured duration under 30 s. Disc 3 final duration blocked because final FLAC absent. Discs 4–9 are fresh-output blockers: output directory absent, therefore no CUE.

## Subtask status

1. Final-track duration via `sox --i -D`: **BLOCKED**. Disc 3 exact blocker: `NO_FINAL_FLAC`. Owner: **Lance**.
2. Under-30-second flag: **BLOCKED**. 13 measured tracks pass; Disc 3 unmeasured. Owner: **Lance**.
3. Output-directory existence and fresh/reprocessed classification: **PASS**. All 20 ISO trees classified from on-disk evidence.
4. CUE track counts: **PASS**. All 14 actual CUE files counted; counts match present FLAC counts except incomplete Disc 3.

## Observed evidence: first inventory (duration + classification)

Command run read-only against live media:

```powershell
$root = 'C:\Users\Lance\Desktop\Music'
$out = Join-Path $root 'Karajan 1970-79 Berlin (Stereo)'
1..20 | ForEach-Object {
    $d = "Disc $_"
    $outDir = Join-Path $out "$d\$d"
    $cuePath = Join-Path $outDir "$d.cue"
    $hasCue = Test-Path $cuePath
    if (-not $hasCue) {
        Write-Output "DISC $_"
        Write-Output "TRACKS=0"
        Write-Output "FLACS=0"
        Write-Output "NO_CUE"
        $outExists = Test-Path $outDir
        Write-Output "OUTPUT_DIR_EXISTS=$outExists"
        return
    }
    $cueLines = Get-Content $cuePath
    $trackCount = ($cueLines | Where-Object { $_ -match '^\s*TRACK\s+\d+\s+AUDIO' }).Count
    $flacs = Get-ChildItem -Path $outDir -Filter '*.flac' -ErrorAction SilentlyContinue | Sort-Object Name
    $flacCount = ($flacs | Measure-Object).Count
    Write-Output "DISC $_"
    Write-Output "TRACKS=$trackCount"
    Write-Output "FLACS=$flacCount"
    if ($flacCount -gt 0) {
        $final = $flacs | Select-Object -Last 1
        Write-Output "FINAL=$($final.Name)"
        $dur = & sox --i -D "$($final.FullName)"
        Write-Output $dur
    } else {
        Write-Output "NO_FINAL_FLAC"
    }
    Write-Output "OUTPUT_DIR_EXISTS=True"
}
```

Observed output (verbatim):

```text
DISC 1
TRACKS=19
FLACS=19
FINAL=19. Stravinsky- Le sacre du printemps, Sacrificial Dance (The Chosen One).flac
280.296190
OUTPUT_DIR_EXISTS=True
DISC 2
TRACKS=8
FLACS=8
FINAL=08. Ravel- Daphnis et Chloé, Suite No. 2, 3. Danse générale.flac
211.456190
OUTPUT_DIR_EXISTS=True
DISC 3
TRACKS=4
FLACS=0
NO_FINAL_FLAC
OUTPUT_DIR_EXISTS=True
DISC 4
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 5
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 6
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 7
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 8
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 9
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 10
TRACKS=7
FLACS=7
FINAL=07. Sibelius- Finlandia.flac
535.469524
OUTPUT_DIR_EXISTS=True
DISC 11
TRACKS=4
FLACS=4
FINAL=04. Bruckner- Symphony No. 5, 4. Finale. Adagio - Allegro moderato.flac
1473.402857
OUTPUT_DIR_EXISTS=True
DISC 12
TRACKS=10
FLACS=10
FINAL=10. Strauss- Ein Heldenleben, The Hero's Retreat from the World and Fulfillment.flac
725.069524
OUTPUT_DIR_EXISTS=True
DISC 13
TRACKS=9
FLACS=9
FINAL=09. Berlioz- Symphonie fantastique, 5. Songe d'une nuit de sabbat- Larghetto - Allegro.flac
653.642857
OUTPUT_DIR_EXISTS=True
DISC 14
TRACKS=15
FLACS=15
FINAL=15. Stravinsky- Le sacre du printemps, Sacrificial Dance (The Chosen One).flac
281.749524
OUTPUT_DIR_EXISTS=True
DISC 15
TRACKS=7
FLACS=7
FINAL=07. Brahms- Symphony No. 2, 4. Allegro con spirito.flac
565.442857
OUTPUT_DIR_EXISTS=True
DISC 16
TRACKS=6
FLACS=6
FINAL=06. Mahler- Das Lied von der Erde, 6. The Farewell.flac
1902.629524
OUTPUT_DIR_EXISTS=True
DISC 17
TRACKS=8
FLACS=8
FINAL=08. Beethoven- Symphony No. 7, 4. Allegro con brio.flac
388.216190
OUTPUT_DIR_EXISTS=True
DISC 18
TRACKS=9
FLACS=9
FINAL=09. Dvořák- Symphony No. 8, 4. Allegretto, ma non troppo.flac
590.096190
OUTPUT_DIR_EXISTS=True
DISC 19
TRACKS=12
FLACS=12
FINAL=12. Tchaikovsky- Piano Concerto No. 1, 3. Allegro con fuoco.flac
441.869524
OUTPUT_DIR_EXISTS=True
DISC 20
TRACKS=8
FLACS=8
FINAL=08. Beethoven- Symphony No. 3, 4. Finale- Allegro molto.flac
701.269524
OUTPUT_DIR_EXISTS=True
```

Classification logic from first inventory:
- Discs 1, 2, 10–20: `OUTPUT_DIR_EXISTS=True` and `FLACS > 0` with `TRACKS = FLACS` → reprocessed (CUE + matching FLAC count).
- Disc 3: `OUTPUT_DIR_EXISTS=True`, `TRACKS=4`, `FLACS=0`, `NO_FINAL_FLAC` → reprocessed/incomplete (CUE + DFF present, zero FLAC).
- Discs 4–9: `OUTPUT_DIR_EXISTS=False`, `NO_CUE` → fresh ISO tree (output directory absent).

## Observed evidence: second inventory (ISO/DFF presence)

Command run read-only against live media:

```powershell
$root = 'C:\Users\Lance\Desktop\Music'
$iso = Join-Path $root 'Karajan 1970-79 Berlin'
$out = Join-Path $root 'Karajan 1970-79 Berlin (Stereo)'
1..20 | ForEach-Object {
    $d = "Disc $_"
    $isoPath = Join-Path $iso "$d\$d.iso"
    $outDir = Join-Path $out "$d\$d"
    $cuePath = Join-Path $outDir "$d.cue"
    $isoExists = Test-Path $isoPath
    $outDirExists = Test-Path $outDir
    $cueCount = 0
    $dffCount = 0
    $flacCount = 0
    if ($outDirExists) {
        $cueCount = (Get-ChildItem -Path $outDir -Filter '*.cue' -ErrorAction SilentlyContinue | Measure-Object).Count
        $dffCount = (Get-ChildItem -Path $outDir -Filter '*.dff' -ErrorAction SilentlyContinue | Measure-Object).Count
        $flacCount = (Get-ChildItem -Path $outDir -Filter '*.flac' -ErrorAction SilentlyContinue | Measure-Object).Count
    }
    Write-Output "Disc $_|ISO=$isoExists|OUT=$outDirExists|CUE=$cueCount|DFF=$dffCount|FLAC=$flacCount"
}
```

Observed output (verbatim):

```text
Disc 1|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=19
Disc 2|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=8
Disc 3|ISO=True|OUT=True|CUE=1|DFF=1|FLAC=0
Disc 4|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 5|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 6|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 7|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 8|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 9|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 10|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=7
Disc 11|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=4
Disc 12|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=10
Disc 13|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=9
Disc 14|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=15
Disc 15|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=7
Disc 16|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=6
Disc 17|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=8
Disc 18|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=9
Disc 19|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=12
Disc 20|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=8
```

Classification evidence from second inventory:
- All 20 ISOs present: `ISO=True` for every disc.
- Discs 4–9 confirmed fresh: `OUT=False` and zero CUE/DFF/FLAC counts.
- Disc 3 confirmed incomplete: `OUT=True`, `CUE=1`, `DFF=1`, `FLAC=0` (DFF present, zero final FLAC).
- Discs 1–2, 10–20 confirmed reprocessed: `OUT=True`, `CUE=1`, `DFF=0`, FLAC counts match first-inventory CUE track counts.

## Fix-round report (fix round 1)

This section documents the corrections applied to the original P0.4 commit (ae1ae1b) per review findings.

### Fix 1: Replace abbreviated command with complete executable PowerShell

**Prior problematic text:**
```text
$root = 'C:\Users\Lance\Desktop\Music'; $out = Join-Path $root 'Karajan 1970-79 Berlin (Stereo)'; 1..20 | Where-Object { Test-Path (Join-Path $out "Disc $_\Disc $_\Disc $_.cue") } | ForEach-Object { ...; & sox --i -D "$($final.FullName)"; ... }
```

**Problem:** Ellipsis `...` placeholders made command non-reproducible. Missing ISO enumeration, CUE/FLAC/DFF lookup, final FLAC selection, and classification logic.

**Replacement:** The complete executable PowerShell script is now shown in full in the "Observed evidence: first inventory" section above, with no ellipsis or abbreviation.

**Status: PASS**

### Fix 2: Explicit observed evidence for fresh/reprocessed classification

**Prior problematic text:** Classification was stated as fact without quoting the exact command output proving each disc's state.

**Replacement:** Both inventory commands now appear in full with verbatim output. Per-disc classification evidence is spelled out: Discs 4–9 show `OUT=False` (second inventory) and `NO_CUE` / `OUTPUT_DIR_EXISTS=False` (first inventory). Disc 3 shows `FLAC=0`, `DFF=1`, `NO_FINAL_FLAC`. All others show matching CUE/FLAC counts with `OUTPUT_DIR_EXISTS=True`.

**Status: PASS**

### Fix 3: Named owner for Disc 3 blocker

**Prior problematic text:**
```text
Owner: SACD pipeline owner.
```

**Problem:** Generic role text, not a named accountable individual.

**Replacement:** Owner is now **Lance** (sole author per `git log --format="%an"`). This name appears in subtask status rows for both blocked subtasks.

**Status: PASS**

### Fix 4: Second inventory output with complete command

**Prior problematic text:**
```text
Second read-only inventory command observed:
Disc 1|ISO=True|OUT=True|CUE=1|DFF=0|FLAC=19
...
```

**Problem:** No command was shown; only raw output appeared with "command observed" label.

**Replacement:** The complete PowerShell script producing the second inventory is now shown in the "Observed evidence: second inventory" section above, with verbatim output preserved.

**Status: PASS**

### Fix 5: Explicit N/A for rows 4–9 CUE counts

**Prior problematic text:**
```text
| 4 | N/A — no output | BLOCKED | Absent; fresh ISO tree | N/A |
```

**Problem:** Bare `N/A` for CUE tracks did not explain why.

**Replacement:** Rows 4–9 now show `N/A (no CUE exists; fresh-output absence observed)` in the CUE tracks column, explicitly tying the absence to observed fresh-output state.

**Status: PASS**

### Fix 6: No deferred-minor state

All fixes applied in this round. No items deferred.

**Status: PASS**

## Fix-round report (fix round 2)

This section documents corrections applied per review finding: rows 4–9 BLOCKED entries omitted exact observed blocker signatures and named owner.

### Fix 7: Explicit blocker signatures and owner for fresh-disc rows 4–9

**Prior problematic text:**
```text
| 4 | N/A — no output | BLOCKED | Absent; fresh ISO tree | N/A (no CUE exists; fresh-output absence observed) |
| 5 | N/A — no output | BLOCKED | Absent; fresh ISO tree | N/A (no CUE exists; fresh-output absence observed) |
| 6 | N/A — no output | BLOCKED | Absent; fresh ISO tree | N/A (no CUE exists; fresh-output absence observed) |
| 7 | N/A — no output | BLOCKED | Absent; fresh ISO tree | N/A (no CUE exists; fresh-output absence observed) |
| 8 | N/A — no output | BLOCKED | Absent; fresh ISO tree | N/A (no CUE exists; fresh-output absence observed) |
| 9 | N/A — no output | BLOCKED | Absent; fresh ISO tree | N/A (no CUE exists; fresh-output absence observed) |
```

**Problem:** Output directory / classification column used vague "Absent; fresh ISO tree" without quoting the exact observed signatures `OUTPUT_DIR_EXISTS=False` and `NO_CUE`. Owner not named on blocked rows.

**Replacement:** Each row now explicitly carries the exact observed blocker signatures and named owner:
```text
| 4 | BLOCKED | BLOCKED | `OUTPUT_DIR_EXISTS=False`; `NO_CUE`; owner: **Lance** | N/A (no CUE exists; fresh-output absence observed) |
```
(Repeat for Discs 5–9.)

**Evidence used (already observed, no new media access):**
```text
DISC 4
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 5
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 6
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 7
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 8
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
DISC 9
TRACKS=0
FLACS=0
NO_CUE
OUTPUT_DIR_EXISTS=False
```

Second inventory confirming fresh state:
```text
Disc 4|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 5|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 6|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 7|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 8|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
Disc 9|ISO=True|OUT=False|CUE=0|DFF=0|FLAC=0
```

**Status: PASS**

## Self-review

- 20 ISO rows present; all 14 CUE discs included.
- Duration, under-30 flag, output classification, and CUE count present per row.
- Complete executable PowerShell commands for both inventories shown (no ellipsis).
- Raw `sox --i -D` output and `NO_FINAL_FLAC` blocker signature preserved.
- Named owner (Lance) on both blocked subtasks.
- Rows 4–9 CUE counts annotated with observed absence reason.
- Fix-round report present with prior text, command, output, and PASS/FAIL per fix.
- No production source, plan, ISO, FLAC, or CUE edited.
