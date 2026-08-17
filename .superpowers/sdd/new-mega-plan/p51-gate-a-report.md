# P5.1 Gate A - Disc 3

Date: 2026-08-17
Worktree: `C:\Users\Lance\Dev\Toolbox-worktrees\sacd-completion-v2`

## Command

```text
dotnet run --project src\App -- audio sacd-convert "C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin\Disc 3\Disc 3.iso" --keep-iso
```

`--format` was omitted because `SacdConvertCommand.Settings.Format` defaults to `AudioOutputFormat.Bit16`; numeric `--format 16` is rejected by the current Spectre enum parser. The run used PATH-resolved `sacd_extract`, `saracon`, and `sox`, with no GUI.

## Observed output

CLI log: `p51-disc3-cli.log`. Result:

```text
Found 1 SACD ISO(s) to process
Probing "Disc 3"
Pipeline.Incomplete ... cue=4 flacs=0
Disc "Disc 3": case B - DFF valid, 0/4 FLACs -> converting
Saracon.Id3Detected input="Disc 3.dff" - stripping before conversion
Pipeline.KeepIsoRetained iso="...Disc 3.iso"
SACD processing completed: 1 succeeded, 0 failed
```

JSONL evidence in `state/logs/audio.jsonl`:

```text
ProcessRunner.Start binary=sacd_extract args=-P -i "...Disc 3.iso"
ProcessRunner.Complete binary=sacd_extract exitCode=0
DffMetadataStripper.Completed inputBytes=3332711216 outputBytes=3332709410
ProcessRunner.Start binary=saracon args=-c d2p -r 44100 -f wav -n 16bit -d tpdf -g 0.00 -T -V all -t ...gain_probe... "...Disc 3_clean.dff"
ProcessRunner.Complete binary=saracon exitCode=0
Saracon.ConvertStart ... rate=44100 bitDepth=16 gain=6
ProcessRunner.Start binary=saracon args=-c d2p -r 44100 -f wav -n 16bit -d tpdf -g 6.00 -T -V all -t "...Disc 3" "...Disc 3_clean.dff"
ProcessRunner.Complete binary=saracon exitCode=0
Pipeline.KeepIsoRetained iso="...Disc 3.iso"
```

No `Saracon.OutputTooSmall` entry occurred. `Saracon.Id3Detected` occurred once in this run. Output has 4 non-empty FLACs with durations `1223.000000`, `1158.373333`, `820.720000`, and `1519.136190` seconds. ISO remains 1,141,997,568 bytes; CUE remains present.

This Gate A run was captured before the keep-ISO cleanup fix, so its output directory retained original and `_clean.dff`. The corrected implementation now validates outputs, deletes DFF/XML, and retains only the ISO when `--keep-iso` is set; Disc 4 rerun provides green integration evidence.

## Verdict

PASS - all seven P5.1 subtasks observed. No files were deleted.
