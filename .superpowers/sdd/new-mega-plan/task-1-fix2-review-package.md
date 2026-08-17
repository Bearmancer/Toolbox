66df544 P0.1 fix-round 2: all 13 canaries captured, evidence corrections
 .superpowers/sdd/new-mega-plan/task-1-report.md | 160 ++++++++++++++++++++++++
 1 file changed, 160 insertions(+)
diff --git a/.superpowers/sdd/new-mega-plan/task-1-report.md b/.superpowers/sdd/new-mega-plan/task-1-report.md
index a87915a..5e01084 100644
--- a/.superpowers/sdd/new-mega-plan/task-1-report.md
+++ b/.superpowers/sdd/new-mega-plan/task-1-report.md
@@ -390,10 +390,170 @@ All 7 missing canaries **can be resolved** in a later Phase by running `sacd-con
 
 ## Revised Concerns
 
 1. **Disc 3ΓÇô9 FLAC gap (FAIL, not BLOCKED):** Incomplete pipeline execution. All 7 ISOs exist; conversion simply hasn't run. Resolvable in Phase 2+ by executing `sacd-convert` on each missing disc. P0.1 snapshot is read-only; forward conversion doesn't violate ordering.
 
 2. **No second volume (BLOCKED):** Single C: drive. Cannot cross-verify byte totals via copy. Owner: system hardware. Signature: `Get-Volume` shows only `C: Fixed 999507046400`.
 
 3. **ISO/FLAC co-location:** Both source ISOs and extracted FLACs on C: drive. Single disk failure risk. No off-volume backup.
 
 4. **Disc 3 intermediate state:** `.dff` extracted but not converted to FLAC. This is a normal pipeline intermediate ΓÇö `.dff` is the DSD master output from `sacd_extract`; `.flac` is the downstream conversion from `saracon`/`sox`. Pipeline ran partially.
+
+---
+
+# Fix Round 2 ΓÇö Complete Canary Capture + Evidence Corrections
+
+**Trigger:** Review findings ΓÇö (1) brief requires 13 canaries total, not per specific disc numbers; FLACs exist for 13 discs; capture all. (2) Disc 3 .dff hash in fix-round 1 matched empty-file SHA-256 ΓÇö re-verify. (3) System-wide FLAC search scope wording too broad. (4) Raw output required, not summary.
+
+## Finding 2.1: All 13 FLAC Canary Discs Identified
+
+Exhaustive search confirmed FLACs exist for exactly these 13 discs: **1, 2, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20**. The brief's assumption that discs 1ΓÇô13 were the target was based on an expected numbering; the actual inventory yields 13 canaries from a different subset. Selection criterion: first FLAC file alphabetically within each disc's output directory.
+
+### Canary Capture ΓÇö Raw Output
+
+**Command (Discs 1, 2, 10ΓÇô17, 19):**
+```powershell
+$basePath = "C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)"
+$discs = @(1, 2, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20)
+foreach ($i in $discs) {
+    $discPath = Join-Path $basePath "Disc $i\Disc $i"
+    $firstFlac = Get-ChildItem -Path $discPath -Filter "*.flac" -ErrorAction SilentlyContinue | Select-Object -First 1
+    if ($firstFlac) {
+        $hash = (Get-FileHash -Path $firstFlac.FullName -Algorithm SHA256).Hash
+        Write-Output "Disc $i|$($firstFlac.Name)|$hash|$($firstFlac.Length)|$($firstFlac.FullName)"
+    }
+}
+```
+
+**Raw Output (Discs 1, 2, 10ΓÇô17, 19):**
+```
+Disc 1|01. Vivaldi- Sinfonia 'Al Santo Sepolcro', 1. Adagio molto.flac|A48E5FD0F3BE58F946C57A132767F7C64C84B09F3FA21A5D7C908DF4AB4DBA42|16275169|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 1\Disc 1\01. Vivaldi- Sinfonia 'Al Santo Sepolcro', 1. Adagio molto.flac
+Disc 2|01. Mendelssohn- Symphony No. 3, 1. Andante con moto - Allegro un poco agitato.flac|0541A1AC447D0C4B37EA5755967E9DA92C6FFEF55FE011E71E6E615306F2F3B7|66028635|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 2\Disc 2\01. Mendelssohn- Symphony No. 3, 1. Andante con moto - Allegro un poco agitato.flac
+Disc 10|01. Mozart- Sinfonia concertante for four winds, 1. Allegro.flac|88B68F8B9022650EB2D4B9585F6511FF3BC193A6AD493CBFD9306188BCC5F63E|74189533|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 10\Disc 10\01. Mozart- Sinfonia concertante for four winds, 1. Allegro.flac
+Disc 11|01. Bruckner- Symphony No. 5, 1. Introduktion. Adagio - Allegro.flac|51039CCE1643550A52A7578E653A9A377BAD2AF56F9EB6AA7EBFA1B630A1CF33|98894827|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 11\Disc 11\01. Bruckner- Symphony No. 5, 1. Introduktion. Adagio - Allegro.flac
+Disc 12|01. Mozart- Symphony No. 41, 1. Allegro vivace.flac|CA590C41A6776BDEBF039D9744C74C9F87295A4F7591FC3B603AB0BC4CB72111|44276357|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 12\Disc 12\01. Mozart- Symphony No. 41, 1. Allegro vivace.flac
+Disc 13|01. Wimberger- Plays, 1. Konfrontation.flac|4EB42B8FB16BBB06EB5517ED1CFAC5A4E386072172FA81C10DFCDA476CFD83DC|28418262|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 13\Disc 13\01. Wimberger- Plays, 1. Konfrontation.flac
+Disc 14|01. Th\u00f6richen- Batrachomyomachia.flac|2DDBAA89FBF41F9733EC5CBA013905E12F77A8D44592DD32489644F26F5EF2A4|176290578|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 14\Disc 14\01. Th\u00f6richen- Batrachomyomachia.flac
+Disc 15|01. Brahms- Double Concerto, 1. Allegro.flac|ED613C013FD80F52C832936A98C3A12C17021CDF08884B59A8D1A83EB6DEDD6C|101900160|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 15\Disc 15\01. Brahms- Double Concerto, 1. Allegro.flac
+Disc 16|01. Mahler- Das Lied von der Erde, 1. The Drinking Song of Earth's Misery.flac|E81481BB00210E5FAF5D75F1B3D2B3E8C30DB65047239BBB751F2813F0C6E07B|45841460|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 16\Disc 16\01. Mahler- Das Lied von der Erde, 1. The Drinking Song of Earth's Misery.flac
+Disc 17|01. Sibelius- Symphony No. 4, 1. Tempo molto moderato, quasi adagio.flac|9D5B3CD55A1B052CAA798C6FA01E6C4F4F9556ECE1293B08742D148FD75E659F|49187240|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 17\Disc 17\01. Sibelius- Symphony No. 4, 1. Tempo molto moderato, quasi adagio.flac
+Disc 19|01. Webern- Five Movements for String Orchestra, 1. Heftig bewegt.flac|13513CF253748447DEAB1A241BFCCC65A38F519FE55FA1FFFF7838B286099F33|13639213|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 19\Disc 19\01. Webern- Five Movements for String Orchestra, 1. Heftig bewegt.flac
+```
+
+**Command (Discs 18 and 20 ΓÇö re-run due to empty hash in first pass):**
+```powershell
+$basePath = "C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)"
+foreach ($i in @(18, 20)) {
+    $discPath = Join-Path $basePath "Disc $i\Disc $i"
+    $firstFlac = Get-ChildItem -Path $discPath -Filter "*.flac" -ErrorAction SilentlyContinue | Select-Object -First 1
+    $h = Get-FileHash -LiteralPath $firstFlac.FullName -Algorithm SHA256
+    Write-Output "Disc $i|$($firstFlac.Name)|$($h.Hash)|$($firstFlac.Length)|$($firstFlac.FullName)"
+}
+```
+
+**Raw Output (Discs 18, 20):**
+```
+Disc 18|01. Bach- Brandenburg Concerto No. 3, 1. [Allegro] - 2. Adagio.flac|CCA45DB5A543B9AE7C77C054C6070FEDEF69375F2608141221AC8C3A6701B112|37486065|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 18\Disc 18\01. Bach- Brandenburg Concerto No. 3, 1. [Allegro] - 2. Adagio.flac
+Disc 20|01. Bach- Brandenburg Concerto No. 1, 1. [Allegro].flac|1E471D945EAEAAD9A767759E7D7E4FB1F1BA5F9552974341A7E86FA2C11FEC92|22491753|C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 20\Disc 20\01. Bach- Brandenburg Concerto No. 1, 1. [Allegro].flac
+```
+
+### Complete Canary Table (13 of 13)
+
+| Disc | FLAC File | SHA-256 | Bytes |
+|------|-----------|---------|-------|
+| 1 | `01. Vivaldi- Sinfonia 'Al Santo Sepolcro', 1. Adagio molto.flac` | `A48E5FD0F3BE58F946C57A132767F7C64C84B09F3FA21A5D7C908DF4AB4DBA42` | 16,275,169 |
+| 2 | `01. Mendelssohn- Symphony No. 3, 1. Andante con moto - Allegro un poco agitato.flac` | `0541A1AC447D0C4B37EA5755967E9DA92C6FFEF55FE011E71E6E615306F2F3B7` | 66,028,635 |
+| 10 | `01. Mozart- Sinfonia concertante for four winds, 1. Allegro.flac` | `88B68F8B9022650EB2D4B9585F6511FF3BC193A6AD493CBFD9306188BCC5F63E` | 74,189,533 |
+| 11 | `01. Bruckner- Symphony No. 5, 1. Introduktion. Adagio - Allegro.flac` | `51039CCE1643550A52A7578E653A9A377BAD2AF56F9EB6AA7EBFA1B630A1CF33` | 98,894,827 |
+| 12 | `01. Mozart- Symphony No. 41, 1. Allegro vivace.flac` | `CA590C41A6776BDEBF039D9744C74C9F87295A4F7591FC3B603AB0BC4CB72111` | 44,276,357 |
+| 13 | `01. Wimberger- Plays, 1. Konfrontation.flac` | `4EB42B8FB16BBB06EB5517ED1CFAC5A4E386072172FA81C10DFCDA476CFD83DC` | 28,418,262 |
+| 14 | `01. Th\u00f6richen- Batrachomyomachia.flac` | `2DDBAA89FBF41F9733EC5CBA013905E12F77A8D44592DD32489644F26F5EF2A4` | 176,290,578 |
+| 15 | `01. Brahms- Double Concerto, 1. Allegro.flac` | `ED613C013FD80F52C832936A98C3A12C17021CDF08884B59A8D1A83EB6DEDD6C` | 101,900,160 |
+| 16 | `01. Mahler- Das Lied von der Erde, 1. The Drinking Song of Earth's Misery.flac` | `E81481BB00210E5FAF5D75F1B3D2B3E8C30DB65047239BBB751F2813F0C6E07B` | 45,841,460 |
+| 17 | `01. Sibelius- Symphony No. 4, 1. Tempo molto moderato, quasi adagio.flac` | `9D5B3CD55A1B052CAA798C6FA01E6C4F4F9556ECE1293B08742D148FD75E659F` | 49,187,240 |
+| 18 | `01. Bach- Brandenburg Concerto No. 3, 1. [Allegro] - 2. Adagio.flac` | `CCA45DB5A543B9AE7C77C054C6070FEDEF69375F2608141221AC8C3A6701B112` | 37,486,065 |
+| 19 | `01. Webern- Five Movements for String Orchestra, 1. Heftig bewegt.flac` | `13513CF253748447DEAB1A241BFCCC65A38F519FE55FA1FFFF7838B286099F33` | 13,639,213 |
+| 20 | `01. Bach- Brandenburg Concerto No. 1, 1. [Allegro].flac` | `1E471D945EAEAAD9A767759E7D7E4FB1F1BA5F9552974341A7E86FA2C11FEC92` | 22,491,753 |
+
+**Target selection:** First FLAC file alphabetically within each disc's output directory (`Karajan 1970-79 Berlin (Stereo)\Disc N\Disc N\`). Inventory yields 13 discs with FLACs: 1, 2, 10ΓÇô20. Discs 3ΓÇô9 have no FLAC output (documented in fix-round 1).
+
+---
+
+## Finding 2.2: Disc 3 .dff Re-Verification
+
+Fix-round 1 report contained incorrect SHA-256 for Disc 3 `.dff` (`E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855` ΓÇö this is the SHA-256 of an empty input, not a 3.3 GB file). Re-verified:
+
+**Command:**
+```powershell
+$dffPath = "C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 3\Disc 3\Disc 3.dff"
+Get-Item -LiteralPath $dffPath | Select-Object FullName, Length, LastWriteTime | Format-List
+Get-FileHash -LiteralPath $dffPath -Algorithm SHA256 | Format-List
+```
+
+**Raw Output:**
+```
+FullName      : C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 3\Disc 3\Disc 3.dff
+Length        : 3332711216
+LastWriteTime : 13-08-2026 17:58:22
+
+Algorithm : SHA256
+Hash      : 997526C65C384DB93BA0CA2F78AD24DC3B284A75EDA3DAB6A7BAA3571731ED3B
+Path      : C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin (Stereo)\Disc 3\Disc 3\Disc 3.dff
+```
+
+**Corrected values:**
+- Size: 3,332,711,216 bytes (3.1 GB) ΓÇö confirmed via `Get-Item.Length`
+- SHA-256: `997526C65C384DB93BA0CA2F78AD24DC3B284A75EDA3DAB6A7BAA3571731ED3B`
+- Last write: 2026-08-13 17:58:22
+
+**Note:** This is a `.dff` (DSD master), not a FLAC. Hash captured for reference only ΓÇö does not count toward the 13 FLAC canaries.
+
+---
+
+## Finding 2.3: System-Wide FLAC Search Scope Correction
+
+**Original claim:** "No FLACs exist anywhere else on the system"
+**Problem:** Unbounded scope claim ΓÇö cannot verify entire system.
+
+**Corrected claim:** "No FLACs found outside `C:\Users\Lance\Desktop\Music` at search depth 6."
+
+**Command:**
+```powershell
+Get-ChildItem -Path "C:\Users\Lance" -Filter "*.flac" -Recurse -ErrorAction SilentlyContinue -Depth 6 |
+  Where-Object { $_.FullName -notlike "*Desktop\Music*" }
+```
+
+**Raw Output:** *(empty ΓÇö no matches)*
+
+**Scope:** `C:\Users\Lance` subtree, recursive depth 6. Does not cover other user profiles, system directories, or non-FLAC audio formats. Exhaustive for FLAC within documented scope.
+
+---
+
+## Finding 2.4: Subtask 4 Final Verdict
+
+**Original (fix-round 1):** `FAIL`
+**Revised (fix-round 2):** `PASS`
+
+**Rationale:** The brief requires "SHA-256 for one FLAC per disc, 13 canaries." Thirteen FLAC canaries have been captured with verified SHA-256 hashes, full paths, and byte lengths. The 13 discs with FLACs (1, 2, 10ΓÇô20) differ from the initially assumed discs 1ΓÇô13, but the contract quantity (13 canaries) is met. Selection criterion was explicit (first FLAC alphabetically per disc directory). All hashes re-verified via `Get-FileHash`.
+
+---
+
+## Revised Acceptance Criteria Summary (Final)
+
+| Criterion | Status | Notes |
+|-----------|--------|-------|
+| Tag exists | Γ£à PASS | `backup/pre-completion-brief-v2` @ `354ca0d203d5dc93e6ede48fa06977381c9386cb` |
+| Byte totals equal | Γ¥î BLOCKED | Single C: drive; no second volume for cross-verification |
+| 13 canaries recorded | Γ£à PASS | 13 FLAC hashes captured (Discs 1, 2, 10ΓÇô20); raw output + paths + sizes |
+| 20 ISOs manifested | Γ£à PASS | All 20 present; nesting `Disc N\Disc N.iso` confirmed; 21.1 GB total |
+
+---
+
+## Revised Concerns (Final)
+
+1. **No second volume (BLOCKED):** Single C: drive (~931 GB). Cannot cross-verify byte totals via copy. Owner: system hardware. Signature: `Get-Volume` shows only `C: Fixed 999507046400`.
+
+2. **ISO/FLAC co-location:** Both source ISOs and extracted FLACs on C: drive. Single disk failure risk. No off-volume backup.
+
+3. **Disc 3 intermediate state:** `.dff` (3.3 GB DSD master) extracted but not converted to FLAC. Normal pipeline intermediate ΓÇö `.dff` from `sacd_extract`, `.flac` from downstream `saracon`/`sox`. Resolvable in Phase 2+.
+
+4. **Discs 4ΓÇô9 absent from output tree:** ISOs exist but no extraction/conversion has run. Directories entirely missing. Resolvable by running `sacd-convert` on each ISO. Does not affect P0.1 snapshot ΓÇö snapshot records current state, which is accurate.
