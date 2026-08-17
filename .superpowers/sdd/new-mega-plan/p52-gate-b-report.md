# P5.2 Gate B - Disc 4

Date: 2026-08-17

## Command

```text
dotnet run --project src\App -- audio sacd-convert "C:\Users\Lance\Desktop\Music\Karajan 1970-79 Berlin\Disc 4\Disc 4.iso" --keep-iso
```

`--format` was omitted; the current command default is `AudioOutputFormat.Bit16`. Saracon was invoked only through its CLI wrapper, never through the GUI.

## Observed result

Preflight: Disc 4 ISO existed at 1,073,840,128 bytes; output directory did not exist. CLI log recorded `case A - extracting from ISO`, then `SACD processing completed: 1 succeeded, 0 failed`.

JSONL recorded:

```text
ProcessRunner.Start binary=sacd_extract args=-2 -e -c -C -i "...Disc 4.iso"
ProcessRunner.Complete binary=sacd_extract exitCode=0
Saracon.ConvertStart ... rate=44100 bitDepth=16 gain=0
ProcessRunner.Start binary=saracon args=-c d2p -r 44100 -f wav -n 16bit -d tpdf -g 0.00 -T -V all -t ...gain_probe...
Saracon.ConvertStart ... rate=44100 bitDepth=16 gain=6
ProcessRunner.Start binary=saracon args=-c d2p -r 44100 -f wav -n 16bit -d tpdf -g 6.00 -T -V all -t ...Disc 4...
Pipeline.KeepIsoRetained iso="...Disc 4.iso"
```

Observed filesystem: one CUE, 8 non-empty FLACs, zero WAVs, two retained DFFs under `Disc 4\Disc 4`, and the original ISO retained at its original size. Guard remained empty in the worktree run; successful processing therefore did not leave a failed/needs-extraction entry.

## Verdict

PASS - all six P5.2 subtasks after cleanup fix. Fresh extraction reached without throwing; CUE oracle count equals FLAC count; no WAV or DFF/XML residue after the safe rerun; ISO retained; no failed guard state. The rerun log recorded `Pipeline.DeletionValidationPassed` followed by `Pipeline.KeepIsoRetained`, and exited 0.
