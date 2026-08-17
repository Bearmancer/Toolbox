# P2.2 Target Report: CLI Contract Truthfulness

**Branch:** `sacd-completion-v2`
**Target commit:** `a4778df`
**Scope:** Description-only edits to `SacdConvertCommand.cs` and `DsdConvertCommand.cs`. No business logic changed.
**Working-tree status:** source edits committed in `a4778df`; report metadata correction committed in `8f541aa`; plan/ledger/checks remain unrelated working-tree artifacts.

---

## Subtask 1: `sacd-convert` format description → 16-bit only

**Goal:** Correct the `sacd-convert` format description to 16-bit only.

**Command:** `git diff src/CLI/Audio/SacdConvertCommand.cs`

**Raw diff:**
```diff
-		[Description("Output format: 16 (default), 24, both")]
+		[Description("Output format: 16 (only supported value)")]
 		[CommandOption("-f|--format")]
 		public AudioOutputFormat Format { get; init; } = AudioOutputFormat.Bit16;
```

**Source evidence:** `ExecuteAsync` (L59-66) rejects every format except `Bit16`:
```csharp
if (settings.Format != AudioOutputFormat.Bit16)
{
    await Console.Error.WriteLineAsync(
        "SACD conversion supports only --format 16.",
        cancellationToken
    );
    return 1;
}
```
The old description advertised `24, both` which the command rejects. New description matches actual behaviour. **PASS**

---

## Subtask 2: `dsd-convert` input description → DFF only (decision recorded)

**Goal:** Correct the `dsd-convert` input description to DFF only, or add DSF parsing — decide and record.

**Decision: DFF-only. No DSF parser added.**

**Rationale:** `ProbeDsdAsync` in `DsdConvertService` parses the DSDIFF container (`FRM8`/`DSD ` header, `PROP`/`SND` subchunks). DSF is a different container format (RIFF-based, `DSD ` chunk with a different layout) and would require a separate parser. The SACD pipeline (`sacd-convert`) already emits DFF via `sacd_extract`; DSF is not produced anywhere in this pipeline. Adding a DSF parser is speculative scope with no consumer. Per the plan's "decide and record" instruction, the lazy correct choice is to narrow the contract to DFF only rather than build an unrequested parser.

**Command:** `git diff src/CLI/Audio/DsdConvertCommand.cs`

**Raw diff:**
```diff
-		[Description("Input DSF or DFF file")]
+		[Description("Input DFF file")]
 		[CommandArgument(0, "<input>")]
 		public required string Input { get; init; }
```

**Source evidence:** `ProbeDsdAsync` (DsdConvertService.cs) reads DSDIFF `FRM8` magic and `PROP`/`SND` subchunks — DFF-only. `PrepareDffAsync`/`ConvertFullDffAsync` operate on DFF. No DSF code path exists. **PASS**

---

## Subtask 3: Rejection message names supported value

**Goal:** Confirm the rejection message names the supported value.

**Command:** `grep "supports only" src/CLI/Audio/SacdConvertCommand.cs`

**Raw:**
```csharp
"SACD conversion supports only --format 16.",
```

**Source evidence:** `SacdConvertCommand.cs` L62. The rejection names the exact supported option/value (`--format 16`). Already correct; no change required. **PASS**

---

## Subtask 4: `--keep-iso` help states destructive default clearly

**Goal:** Confirm `--keep-iso` help states the destructive default clearly.

**Command:** `git diff src/CLI/Audio/SacdConvertCommand.cs`

**Raw diff:**
```diff
-		[Description("Keep source ISO files (deleted by default)")]
+		[Description("Keep source ISO files (ISO deleted after conversion by default)")]
 		[CommandOption("--keep-iso")]
 		public bool KeepIso { get; init; }
```

**Source evidence:** The prior text "(deleted by default)" was ambiguous about *what* is deleted. New text states explicitly that the ISO is deleted after conversion by default, and that `--keep-iso` retains it. **PASS**

---

## Build verification

**Command:** `dotnet build src/CLI/CLI.csproj`

**Output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:06.49
```

All projects compiled clean. **PASS**

---

## Runtime acceptance (CLI `--help`)

**Status: BLOCKED**

**Command:** `dotnet run --project src/App -- audio sacd-convert --help`

**Observed:** exit code `2`, no stdout/stderr output.

**Blocker signature:** `Program.Main` (src/App/Program.cs L37-44) returns `2` before Spectre parses any command when `.env` is absent:
```csharp
var envPath = Path.Combine(PathResolver.RepoRoot, ".env");
if (!File.Exists(envPath))
{
    Telemetry.Error(
        ".env not found at {Path}. Create one at the repo root with all required keys.",
        envPath
    );
    return 2;
}
```
No `.env` exists in the worktree (`Test-Path .env` → `False`). The app exits at startup, so `--help` output cannot be produced in this environment.

**Owner:** Environment provisioning (a `.env` at repo root with required keys) or a startup refactor that lets `--help` bypass credential loading. Source-level contract truthfulness is verified by the diffs above and the clean build; runtime `--help` rendering remains blocked until `.env` is present.

---

## Summary

| Subtask | Status | Evidence |
|---------|--------|----------|
| sacd-convert format → 16-bit only | PASS | Description changed; matches L59-66 rejection of non-16 |
| dsd-convert input → DFF only | PASS | Description changed; decision recorded (no DSF parser, no consumer) |
| Rejection names `--format 16` | PASS | L62 already names supported value; no change |
| `--keep-iso` destructive default explicit | PASS | Description now states ISO deleted after conversion by default |
| Build | PASS | 0 warnings, 0 errors |
| Runtime `--help` | BLOCKED | `.env` missing → `Program.Main` returns 2 before Spectre; owner: env provisioning |

---

**Commits:** Source edits (`SacdConvertCommand.cs`, `DsdConvertCommand.cs`) in `a4778df`; this report and metadata correction in `8f541aa` on `sacd-completion-v2`.
