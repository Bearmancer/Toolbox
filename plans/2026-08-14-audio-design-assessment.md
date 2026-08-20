# Audio Services Design Assessment

**Assessment date:** 2026-08-14\
**Scope:** Current working tree under `src/Services/Audio` and `src/CLI/Audio`, with project guidance and directly relevant Core conventions used as context.\
**Mode:** Read-only assessment. No production code, tests, configuration, state files, or Git history were changed.

## Executive assessment

The Audio subsystem has several intentional boundaries that are sound:

- `SacdConvertCommand` delegates to `PipelineOrchestrator`.
- `PipelineOrchestrator` delegates DSD conversion through `DsdConvertService` rather than calling `SaraconService` or `SoxService` directly.
- `SaraconService`, `SoxService`, and `SacdExtractService` are recognizable external-tool adapters.
- `DiskSpaceChecker` and the individual `SoxService` operations are small, focused components.

The main design problem is responsibility accumulation around three central components:

1. `PipelineOrchestrator` is both a god class and a family of workflow god methods. It owns batch execution, state routing, output layout, destructive cleanup, conversion preparation, and source-ISO cleanup.
2. `DsdConvertService` is a facade that has expanded into a god class. It contains DFF parsing, gain analysis, master conversion, track splitting, metadata application, full-file conversion, and directory derivation.
3. `ProcessRunner.RunAsync` is a god method. It combines process launch, output capture, timeout state machines, cancellation, completion-marker policy, termination, telemetry, and result interpretation.

The highest operational risks are not merely maintainability concerns:

- A cancellation or unexpected exception can leave process-global `LogPaths` state configured for a later run (`PipelineOrchestrator.cs:69-111`).
- A process killed after a completion marker is rewritten as exit code `0` (`ProcessRunner.cs:171-203`).
- Directory derivation logs failures but always returns success (`DsdConvertService.cs:318-345`).
- Intermediate PCM cleanup is not exception-safe (`DsdConvertService.cs:172-244`).
- Path containment uses a raw string-prefix check and accepts sibling paths with the same prefix (`PathValidator.cs:44-55`).

The recommended direction is staged decomposition, not a wholesale rewrite: first make lifecycle and result semantics truthful, then extract workflow policies and format infrastructure behind the existing facade boundaries.

## Review method and metric interpretation

I assessed each source file for:

- **God methods:** unusually large or branch-heavy methods that combine independently changeable policies, I/O, process control, error translation, and presentation.
- **God classes:** classes with several independent responsibility clusters, regardless of whether any individual method is long.
- **SRP violations:** reasons for change belonging to different domains, such as filesystem layout, binary format parsing, process execution, metadata mapping, and workflow routing.
- **Boundary integrity:** whether CLI, application orchestration, domain policy, and infrastructure concerns remain separated.
- **Operational design:** cancellation, cleanup, partial success, process termination, global state, and truthful error reporting.
- **Testability:** direct static I/O, concrete dependencies, hidden side effects, and diagnostic code embedded in production services.

The metric snapshot is heuristic. Lines of code or dependency counts do not prove a god class; they identify candidates for responsibility analysis. A class is classified as a god class only when its metrics align with multiple independent responsibility clusters.

## Metric snapshot

| File                         | LOC |                            Public operations | Primary-constructor dependencies | Responsibility signal                                             |
| ---------------------------- | --: | -------------------------------------------: | -------------------------------: | ----------------------------------------------------------------- |
| `PipelineOrchestrator.cs`    | 427 |                                            1 |                                6 | God class; batch, state routing, layout, conversion, cleanup      |
| `DsdConvertService.cs`       | 352 |                                            6 |                                3 | God class; parser, gain, conversion, split, tags, derivation      |
| `SacdProbeRunner.cs`         | 355 |                                            1 |                                1 | Diagnostic god class; harness, process, console, journal          |
| `ProcessRunner.cs`           | 255 |                                            2 |                                0 | God method; one 223-line execution state machine                  |
| `DiscOutputInspector.cs`     | 256 |                                            1 |                                3 | Inspector plus scanner, policy, routing state                     |
| `SaraconService.cs`          | 299 |                                            2 |                                2 | Broad adapter method; sanitizing, process, output validation      |
| `CueParser.cs`               | 215 |                                            1 |                                0 | Cohesive parser with hidden file/encoding I/O                     |
| `DffMetadataStripper.cs`     | 204 | 1 direct public method plus static detection |                                0 | Focused purpose, duplicated binary walking and cleanup gap        |
| `FlacCompletenessChecker.cs` | 153 |                                            2 |                                1 | Mostly cohesive, but mixes duration policy and artifact discovery |
| `AudioMetadataService.cs`    | 135 |                                            3 |                                0 | Three metadata use cases; moderate SRP drift                      |
| `SoxService.cs`              | 142 |                                            4 |                                2 | Focused tool adapter; no major god-method finding                 |
| `SacdExtractService.cs`      | 121 |                                            2 |                                2 | Focused tool adapter; output discovery is related to extraction   |
| `AudioModels.cs`             |  98 |                               records/policy |                                0 | Mixed contracts and conversion policy, low risk                   |
| `PathValidator.cs`           |  57 |                                            3 |                                0 | Focused validator, but unsafe containment implementation          |
| `LogPaths.cs`                |  61 |                                            4 |                                0 | Small class with process-global mutable state                     |
| `AudioSetup.cs`              |  46 |                                            1 |                                0 | Focused composition root, but eager infrastructure coupling       |
| `DiskSpaceChecker.cs`        |  35 |                                            2 |                                0 | Cohesive and appropriately small                                  |
| `RealDffFixture.cs`          |  50 |                                            2 |                                0 | Diagnostic fixture with hard-coded machine path                   |
| `SacdProbeService.cs`        |  15 |                                            1 |                                1 | Thin wrapper, but constructs its own runner                       |
| `DsdConvertCommand.cs`       | 150 |                            command operation |                                2 | CLI contains a conversion workflow                                |
| `SacdConvertCommand.cs`      |  67 |                            command operation |                                1 | Thin and appropriately layered                                    |
| `AudioCommandModule.cs`      |  17 |                                 registration |                                0 | Thin registration module                                          |

## Prioritized findings

### A1 — `PipelineOrchestrator` is a god class and contains multiple workflow god methods

**Severity:** High\
**Files and evidence:**

- Class and six dependencies: `src/Services/Audio/PipelineOrchestrator.cs:8-15`
- Batch validation, enumeration, sorting, disk-space checks, logging setup, aggregation, and final cleanup: `:22-114`
- Per-disc probing, output-layout derivation, assessment routing, partial cleanup, extraction, and conversion: `:124-257`
- Partial-output deletion: `:259-301`
- CUE/DFF discovery, probing, gain calculation, format selection, conversion, and derived-output routing: `:303-364`
- DFF/XML/source-ISO cleanup and a containment check: `:366-426`

This class has several independent reasons to change:

- batch scheduling and result aggregation;
- disc state-machine policy;
- output directory naming;
- destructive artifact cleanup;
- conversion preparation and derived-output policy;
- process-run logging context.

That is a god class independently of method length. It also contains at least three god methods:

- `RunAsync` (`:22-114`) mixes batch control, validation, global state, cancellation, aggregation, and cleanup.
- `ProcessIsoAsync` (`:124-257`) mixes state inspection, routing, deletion, extraction, and conversion.
- `ConvertDiscAsync` (`:303-364`) mixes file discovery, parsing, binary probing, gain analysis, conversion settings, output layout, and derivation.

**Why it matters:** Changes to output layout, resume behavior, deletion safety, and conversion sequencing all modify the same class. The workflow is difficult to test without real filesystem state and external tools. The current method family also makes state transitions implicit in combinations of booleans and nullable values.

**Focused refactoring direction:** Keep a small high-level coordinator, but delegate policies to focused components:

- `SacdBatchRunner` for ISO enumeration, ordering, and batch aggregation;
- `DiscProcessingWorkflow` for one-disc state routing;
- `DiscConversionWorkflow` for parse/probe/gain/convert/derive;
- `AudioOutputLayout` for channel and derived-directory names;
- `AudioArtifactCleaner` for partial and successful-run cleanup.

The first extraction should preserve existing behavior and make cleanup/result semantics explicit; it should not introduce a broad generic filesystem abstraction prematurely.

### A2 — `ProcessRunner.RunAsync` is a god method and an overly broad abstraction

**Severity:** High\
**File:** `src/Services/Audio/ProcessRunner.cs:11-234`

The method handles all of the following in one state machine:

- executable discovery (`:23-24`);
- telemetry and argument formatting (`:26-33`);
- `ProcessStartInfo` construction (`:35-46`);
- stdout/stderr collection and callbacks (`:55-99`);
- inactivity timeout refresh (`:60-73`, `:88-96`);
- cancellation and process-tree termination (`:101-151`);
- overall timeout (`:154-169`);
- completion-pattern detection and grace-period killing (`:171-193`);
- exit-code rewriting and result construction (`:196-232`).

This is a god method even though `ProcessRunner` as a class has a narrow nominal purpose. It contains several independently testable policies with different failure semantics.

**Why it matters:** Callers inherit hidden behavior. A generic process runner now understands completion markers, inactivity policy, and grace kills, although those semantics are primarily Saracon-specific. Output callbacks are invoked from process event handlers, introducing callback reentrancy/threading concerns. `inactivityCts` is created at `:60` but is not disposed.

**Focused refactoring direction:** Split the state machine into internal seams or collaborators:

- process launch/configuration;
- stdout/stderr collection;
- lifetime and timeout policy;
- termination operation;
- result interpretation.

Keep the public contract small. Move Saracon-specific completion-marker behavior to `SaraconService` or a named tool policy instead of exposing `completionPattern` as a generic success mechanism.

### A3 — A forcibly killed process is represented as successful

**Severity:** High\
**File:** `src/Services/Audio/ProcessRunner.cs:171-203`

After a completion pattern is observed, the runner waits for a grace period and kills the process if it remains alive (`:180-191`). It then forces the exit code to zero (`:201-203`):

```csharp
if (graceKillOccurred)
    exitCode = 0;
```

`SaraconService` activates this behavior with `completionPattern: "100%"` and a ten-second completion timeout (`src/Services/Audio/SaraconService.cs:161-169`).

**Why it matters:** Printing `100%` is not equivalent to normal process termination or output finalization. The process may still be writing or flushing. A partial output can therefore enter the success path, with only a coarse size check later acting as a safeguard.

**Focused refactoring direction:** Preserve the actual exit code and return a distinct termination reason, for example `Exited`, `Cancelled`, `TimedOut`, `KilledAfterCompletionMarker`, or `FailedToStart`. Let `SaraconService` decide whether a specific termination reason is acceptable after strong output validation. Do not encode process-kill-as-success in the shared runner.

### A4 — `DsdConvertService` is a god class, not merely a useful facade

**Severity:** High\
**File:** `src/Services/Audio/DsdConvertService.cs:8-352`

The facade boundary is intentional and useful, but the class contains at least six responsibility clusters:

- DSDIFF/DFF binary parsing: `:18-124`;
- gain-analysis orchestration using Saracon and Sox: `:126-170`;
- master conversion, track splitting, filename sanitization, and metadata tagging: `:172-244`;
- full-file DFF conversion and temporary-file movement: `:246-290`;
- one-file FLAC derivation: `:292-316`;
- directory derivation and partial-failure policy: `:318-345`.

**Why it matters:** The class has unrelated reasons for change: file-format changes, gain policy changes, track naming changes, metadata changes, temporary workspace changes, and derived-format policy changes. The facade currently hides tool coupling from the pipeline, but it does not provide a focused internal design.

**Focused refactoring direction:** Retain a caller-facing facade if it protects the pipeline, but delegate internally to focused services:

- `DsdiffInspector`;
- `GainAnalyzer`;
- `DiscTrackConverter`;
- `SingleFileDsdConverter`;
- `FlacDerivationService`;
- a small temporary-workspace owner.

Do not split methods into arbitrary fragments solely to reduce line count; split at responsibility and error-policy boundaries.

### A5 — `ConvertAndSplitAsync` mixes conversion, track workflow, tagging, aggregation, and cleanup

**Severity:** High\
**File:** `src/Services/Audio/DsdConvertService.cs:172-244`

The method:

1. runs Saracon (`:180-192`);
2. derives output filenames and sanitizes titles (`:195-200`);
3. splits every CUE track with Sox (`:201-207`);
4. silently discards split errors (`:209-212`);
5. applies metadata (`:216-222`);
6. deletes the master PCM only on the normal loop path (`:225-226`);
7. reconstructs missing tracks from generated filenames (`:228-240`).

**Why it matters:** A cancellation or exception during splitting/tagging leaves the intermediate master PCM behind. The original split errors are lost, so diagnostics reduce to an inferred missing-track list. Tagging failure is warning-only while splitting failure is eventually fatal, but that policy is not represented in the result.

**Focused refactoring direction:** Introduce explicit stages:

- `ConvertMasterAsync`;
- `ConvertTracksAsync` returning per-track results/errors;
- `WriteTrackMetadata`;
- a `try/finally` master-workspace owner.

Use track-number keyed results rather than reconstructing failure state from output filenames. Decide explicitly whether partial files are retained for resume or deleted after an aggregate failure.

### A6 — Directory derivation reports success despite failures

**Severity:** High\
**File:** `src/Services/Audio/DsdConvertService.cs:318-345`

`DeriveDirectoryAsync` enumerates source FLACs and logs failures (`:327-341`), then returns `Result.Success` unconditionally (`:344`). The pipeline treats this operation as successful in `PipelineOrchestrator.cs:159-174`, `:177-203`, and `:353-361`.

**Why it matters:** The derived directory can be incomplete while the ISO is counted as succeeded. This contradicts the completeness/resume model and makes CLI exit status and pipeline counts unreliable.

**Focused refactoring direction:** Return structured per-file results or an error carrying failed files. The orchestration layer should decide whether partial derivation is recoverable, retryable, or a failed disc. Keep warning-level telemetry for individual details, but do not erase the aggregate outcome.

### A7 — Pipeline logging context is process-global and not exception-safe

**Severity:** High\
**Files:** `PipelineOrchestrator.cs:69-111`, `LogPaths.cs:3-60`

`RunAsync` calls `LogPaths.Setup` at `:69` and `LogPaths.Reset` only after normal processing and successful cleanup at `:110-111`. Cancellation is explicitly thrown at `:82`; filesystem calls, external services, and cleanup can also fail before reset.

`LogPaths` stores mutable static `IsoRoot` and `OutputRoot` (`LogPaths.cs:5-18`).

**Why it matters:** A later command can inherit stale roots. Concurrent or nested runs can overwrite each other. Logging and redaction behavior therefore depends on prior command lifecycle rather than on an explicit run context.

**Focused refactoring direction:** At minimum, put reset in a `finally`. Prefer a run-scoped immutable context or logger formatter whose lifetime is owned by the batch operation. If static state remains temporarily, use a disposable scope that restores the prior context rather than blindly clearing it.

### A8 — Path containment is implemented as an unsafe string-prefix check

**Severity:** High\
**File:** `src/Services/Audio/PathValidator.cs:44-55`

`ValidateContainedPath` accepts a child whenever:

```csharp
fullChild.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase)
```

A base such as `C:\output\Disc` therefore also matches `C:\output\Discipline`.

`PipelineOrchestrator` has a more careful separator-appended check at `:379-385`, while `LogPaths.IsWithin` repeats the weaker prefix behavior at `LogPaths.cs:46-48`. The same policy is not centralized.

**Why it matters:** A path outside the intended directory can pass containment validation, and log formatting can misclassify sibling paths. This is both a design duplication problem and a boundary-validation correctness risk.

**Focused refactoring direction:** Normalize full paths and compare the exact root or the root plus a directory separator. Centralize this in one focused path-boundary operation and cover sibling names, relative paths, alternate separators, and case differences.

### A9 — `DiscOutputInspector` is an inspector, scanner, parser coordinator, and routing-policy owner

**Severity:** Medium-High\
**File:** `src/Services/Audio/DiscOutputInspector.cs:8-256`

The class has three dependencies (`:8-12`) and `EvaluateDiscAsync` performs:

- DFF directory discovery (`:37`);
- CUE discovery and parsing (`:39-56`);
- DFF enumeration and probing (`:58-77`);
- FLAC mapping (`:79-80`);
- derived-directory naming and enumeration (`:82-105`);
- primary and derived completeness policy (`:128-183`);
- duration validation (`:185-210`);
- state classification and telemetry (`:212-254`).

The nested `DiscAssessment` is a boolean/nullable state bag (`:14-28`) with combinations such as `IsComplete`, `NeedsExtraction`, `HasValidDff`, `HasCue`, and `DerivedOnly` that the caller must interpret correctly.

**Why it matters:** Inspection, filesystem scanning, completeness policy, and resume routing have different reasons to change. Some output-layout and state policy is duplicated in `PipelineOrchestrator`. The state bag permits ambiguous combinations and makes future states harder to model.

**Focused refactoring direction:** Have a scanner produce an immutable artifact snapshot, then apply pure completeness/resume policy to that snapshot. Replace the boolean bag with explicit states such as `NeedsExtraction`, `NeedsPrimaryConversion`, `NeedsDerivedConversion`, `Complete`, and `Invalid`. Keep telemetry at the application boundary where possible.

### A10 — `DsdConvertCommand` violates the thin-CLI boundary

**Severity:** Medium\
**File:** `src/CLI/Audio/DsdConvertCommand.cs:38-149`

The CLI guidance says commands should be thin wrappers with no service logic (`src/CLI/AGENTS.md:1-3`, `:43-55`). `DsdConvertCommand.ExecuteAsync` nevertheless owns:

- path resolution and input validation (`:46-55`);
- probing (`:58-74`);
- automatic gain policy (`:76-94`);
- conversion-settings selection (`:96-99`);
- primary conversion (`:101-112`);
- optional derivation (`:114-128`);
- optional metadata read/write (`:130-141`);
- user-facing output (`:144-148`).

`SacdConvertCommand` is appropriately thin because it delegates directly to the pipeline (`src/CLI/Audio/SacdConvertCommand.cs:31-65`). The two Audio entry points therefore have inconsistent layering and error semantics.

**Why it matters:** A conversion workflow is difficult to reuse outside the CLI and difficult to test without Spectre command infrastructure. Optional derivation and tagging failures are warning-only and still return success, unlike the primary conversion path.

**Focused refactoring direction:** Add an application-level `DsdFileConversionWorkflow` that accepts a request and returns structured results. The command should bind settings, call the workflow, render results, and map errors to an exit code.

### A11 — DFF/DSDIFF chunk parsing is duplicated across components

**Severity:** Medium\
**Files:**

- `DsdConvertService.cs:35-102`;
- `SaraconService.cs:253-297`;
- `DffMetadataStripper.cs:24-57`, `:99-126`;
- `RealDffFixture.cs:30-48`.

These components independently walk `FRM8`, form-type, chunk-size, padding, `DSD`, `PROP`, `FS`, and `CHNL` structures.

**Why it matters:** A format-boundary correction must be made in several places. The existing comments in `DffMetadataStripper.cs:33-35`, `:61-63`, and `:139-141` show that parsing and failure behavior have already been sensitive to merge/repro changes. Implementations differ in validation rigor and contain casts from `ulong` to `int`/`long`.

**Focused refactoring direction:** Create one validated DSDIFF reader/chunk iterator that exposes the metadata needed by probing, expected-size estimation, and stripping. Keep stripping/writing separate from reading. Make fixture code consume the same reader rather than maintaining another parser.

### A12 — Temporary-directory cleanup can replace the primary conversion error

**Severity:** Medium\
**Files:** `DsdConvertService.cs:126-170`, `:246-290`

Both `CalculateGainAsync` and `ConvertFullDffAsync` delete temporary directories in `finally` without handling cleanup failures (`:165-169`, `:285-289`).

**Why it matters:** A failed conversion or cancellation can be replaced by an exception from cleanup, especially while an external process still has a file open. The caller then loses the primary diagnostic and may not receive an `ErrorOr` result at all.

**Focused refactoring direction:** Use a temporary-workspace owner that separates primary-operation failure from cleanup failure. Preserve the primary error, log cleanup failure, and only expose cleanup failure as an additional structured diagnostic when useful.

### A13 — `SaraconService.RunConversionAsync` crosses adapter, format, and output-integrity boundaries

**Severity:** Medium\
**File:** `src/Services/Audio/SaraconService.cs:97-251`

The method:

- creates output directories (`:119-120`);
- detects and strips DFF ID3 metadata (`:125-159`);
- runs the process (`:161-169`);
- interprets process errors and exit codes (`:171-194`);
- resolves Saracon naming variants (`:196-225`);
- estimates expected PCM size and validates truncation (`:228-242`).

The method is logically cohesive as “perform one Saracon conversion,” so this is not a line-count-only recommendation. The concern is that the external-tool adapter now also owns DFF sanitization and output-integrity policy.

**Focused refactoring direction:** Extract named seams such as `PrepareInputDffAsync`, `RunSaraconAsync`, `ResolveOutputPath`, and `ValidateOutputSize`. Keep the adapter facade but move reusable DFF and output-validation policies into focused services.

### A14 — Diagnostic probe code is embedded in the production Audio service boundary

**Severity:** Medium\
**Files:** `SacdProbeRunner.cs:7-355`, `SacdProbeService.cs:3-15`, `RealDffFixture.cs:5-50`

`SacdProbeRunner` is a diagnostic harness, but it also owns:

- a hard-coded output directory (`:16`);
- a repository-relative journal path (`:9-15`);
- console presentation (`:45-98`);
- fixture preconditions (`:49-60`);
- matrix execution and failure classification (`:66-97`, `:100-141`);
- direct process execution bypassing `ProcessRunner` (`:180-268`);
- read/modify/write journal persistence (`:331-354`).

`SacdProbeService` constructs its runner directly (`:5`) instead of receiving it through DI.

**Why it matters:** The diagnostic harness has different lifecycle, output, and persistence requirements from the conversion pipeline. Direct process execution duplicates infrastructure and makes the service graph misleading. A production build also carries machine-specific assumptions such as `C:\Temp\t.dff`.

**Focused refactoring direction:** Move the probe harness to an explicit diagnostic/audit boundary or a separate tool/project if the repository permits. If it must remain in this project, isolate journal persistence, console rendering, and process execution behind explicit diagnostic components and inject the runner.

### A15 — Metadata service combines reading, generic writing, and CUE mapping

**Severity:** Low-Medium\
**File:** `src/Services/Audio/AudioMetadataService.cs:8-118`

The class contains three different use cases:

- DSD/ATL metadata reading (`:10-37`);
- generic FLAC tag writing (`:39-83`);
- CUE-to-FLAC mapping (`:85-118`).

**Why it matters:** ATL file access, domain mapping, and field-presence policy can change independently. The class is not large enough to be a severe god class, but it is a clear SRP drift and exposes external ATL concerns alongside domain mapping.

**Focused refactoring direction:** Separate `DsdMetadataReader`, `FlacTagWriter`, and `CueMetadataMapper` only when the next change or test seam requires it. Keep `TrackMetadata` as the domain-facing model.

### A16 — Error policy is inconsistent across pipeline, service, and CLI

**Severity:** Medium\
**Files:** `DsdConvertService.cs:209-223`, `:318-345`; `DsdConvertCommand.cs:114-141`; `PipelineOrchestrator.cs:159-203`, `:353-361`; `SacdConvertCommand.cs:45-65`

Observed policies differ:

- primary conversion failures return errors;
- per-track split errors are discarded and inferred later;
- tagging failures are warnings;
- directory derivation failures are warnings and aggregate success;
- direct DSD derivation failures are warnings and exit code `0`;
- the SACD command returns exit code `1` when pipeline failure count is nonzero.

**Why it matters:** The same conceptual operation can produce different success semantics depending on entry point. Users and callers cannot reliably determine whether output is complete from the result type or exit code.

**Focused refactoring direction:** Define result categories such as fatal conversion failure, partial output, optional metadata warning, and cleanup warning. Return those categories from workflows; let CLI and batch orchestration map them consistently.

### A17 — Output layout and sample-rate derivation policy are duplicated

**Severity:** Medium\
**Files:** `DiscOutputInspector.cs:84-104`; `PipelineOrchestrator.cs:64-68`, `:182-198`, `:355-360`; `AudioModels.cs:25-57`

Derived-directory names are built in both the inspector and orchestrator. The target-rate logic also appears inline as:

```csharp
sr == 2822400 ? 44100 : 88200
```

while `DsdConversionSettings.ForDsdRate` is documented as the single source for sample-rate mapping.

**Why it matters:** A naming or sample-rate policy change can update one path and leave another inconsistent. The inspector may classify a derived directory differently from the directory the orchestrator creates.

**Focused refactoring direction:** Introduce a small output-layout policy and use `DsdConversionSettings.ForDsdRate` or a similarly centralized domain policy for target settings. Keep path naming out of the inspection algorithm.

### A18 — `FlacCompletenessChecker` mixes duration policy with artifact discovery

**Severity:** Low-Medium\
**File:** `src/Services/Audio/FlacCompletenessChecker.cs:8-153`

`CheckTrackDurationsAsync` is a mostly cohesive duration validator (`:25-116`), while `GetFlacsByTrackNumber` and `FindDffDir` perform filesystem discovery (`:118-151`). The duration result also carries repeated routing state such as DFF directory, sample rate, and derived directory (`:15-23`).

**Why it matters:** Filesystem scanning and pure completeness rules have different reasons to change. The result is being used as both a validation outcome and a partial assessment transport object.

**Focused refactoring direction:** Keep the duration validator focused on expected/actual durations and move artifact discovery into the inspector/scanner boundary. Replace repeated state transport with an immutable assessment snapshot where practical.

## Secondary design observations

### Eager binary validation couples unrelated commands to Audio infrastructure

**Severity:** Low\
**File:** `src/Services/Audio/AudioSetup.cs:9-14`, `:39-44`

`AddAudioServices` validates Saracon, Sox, and `sacd_extract` during DI registration. An unrelated CLI command can fail during application startup when an Audio binary is absent. The current Audio guidance explicitly documents eager validation, so this is a deliberate operational trade-off rather than a clear defect.

**Direction:** Consider command-specific or lazy validation if startup independence becomes important. Preserve precise binary-not-found errors at the command boundary.

### `CueParser.Parse` has hidden I/O but remains mostly cohesive

**Severity:** Low-Medium testability\
**File:** `src/Services/Audio/CueParser.cs:15-126`, encoding helpers `:128-214`

The method reads bytes, chooses encoding, parses directives, validates required fields, and derives track durations. The parser logic itself is cohesive; the main issue is that file I/O and encoding heuristics make pure parser tests harder.

**Direction:** Add a text-based parsing seam, such as `ParseContent`, and keep file reading/encoding detection in a small reader. Do not split the directive switch into many tiny methods without a testing or reuse reason.

### `DffMetadataStripper.StripId3TagsAsync` has a partial-output cleanup gap

**Severity:** Medium operational correctness\
**File:** `src/Services/Audio/DffMetadataStripper.cs:75-151`

The output file is created before the size validation at `:94-95`. That validation returns an error without deleting the already-created output. Exception paths delete the file at `:147-149`, but non-exceptional validation failures do not.

**Direction:** Validate before creating output, or use a success flag and delete incomplete output in `finally`. Share a chunk iterator with `HasId3Chunk`.

### `SacdExtractService` is a reasonable adapter, with related output discovery

**Severity:** No major violation\
**File:** `src/Services/Audio/SacdExtractService.cs:8-119`

`ProbeAsync` and `ExtractAsync` are focused on interpreting `sacd_extract`. Directory-difference and DFF fallback discovery in `ExtractAsync` are part of interpreting the tool’s output, not an independent god-class responsibility.

### `SoxService` methods are focused

**Severity:** No major violation\
**File:** `src/Services/Audio/SoxService.cs:9-142`

Track splitting, peak statistics, duration probing, and FLAC derivation are separate operations within one Sox adapter. The class has a coherent reason to change: Sox invocation and output interpretation. No method-level extraction is currently justified.

### `DiskSpaceChecker` is cohesive

**Severity:** No violation\
**File:** `src/Services/Audio/DiskSpaceChecker.cs:7-35`

The class owns one policy—required space versus available space—with two domain-specific expansion factors. It does not exhibit god-class behavior.

### `SacdConvertCommand` and `AudioCommandModule` are appropriately thin

**Severity:** No violation\
**Files:** `src/CLI/Audio/SacdConvertCommand.cs:9-67`, `src/CLI/Audio/AudioCommandModule.cs:1-17`

`SacdConvertCommand` binds settings, calls the orchestrator, renders the result, and maps failure count to an exit code. `AudioCommandModule` only registers commands. These should remain the target layering pattern for `DsdConvertCommand`.

### `AudioModels` is mixed but low risk

**Severity:** Low\
**File:** `src/Services/Audio/AudioModels.cs:3-98`

The file contains SACD records, conversion settings/policy, CUE records, and application results. That is a mixed model file, but the records are small and related to the Audio boundary. `DsdConversionSettings.ForDsdRate` is legitimate domain policy; the main risk is discoverability and future growth, not current god-class behavior.

## Cross-cutting design issues

### 1. Direct static filesystem/process I/O reduces testability

The core workflows directly call `File`, `Directory`, `FileInfo`, `DriveInfo`, `Process.Start`, and static DFF helpers. This is most consequential in `PipelineOrchestrator`, `DiscOutputInspector`, `DsdConvertService`, `CueParser`, `PathValidator`, and `SacdProbeRunner`.

The project guidance states there is no test project and manual verification is used. That constraint makes hidden side effects more expensive: the current design leaves little room for fast deterministic tests around state routing, cleanup, and failure aggregation.

**Direction:** Do not introduce interfaces for every framework call. Start with seams where correctness is highest:

- process lifecycle/result semantics;
- output layout and path containment;
- artifact scanning snapshots;
- cleanup policy;
- DFF parsing.

### 2. Concrete dependency graphs hide application boundaries

`PipelineOrchestrator`, `DiscOutputInspector`, and `DsdConvertService` depend on concrete services. This is acceptable for a small application, but it makes workflow tests require the real service graph and external tool wrappers.

**Direction:** Add interfaces only at stable application boundaries (`IDiscInspector`, `IDsdConverter`, `ISacdExtractor`, `IProcessRunner`) when extracting workflows. Avoid an interface-per-class conversion that would add ceremony without improving isolation.

### 3. The domain state model is implicit

`DiscAssessment` uses multiple booleans and nullable values. `PipelineResult` contains aggregate counts and free-form recoverable error strings. Process termination is encoded in an exit code, including a false success code after a grace kill.

**Direction:** Replace implicit combinations with explicit result/state types at the points where routing decisions are made. This is more valuable than merely reducing class sizes.

### 4. Cleanup is treated as incidental instead of as a first-class policy

Cleanup appears in `PipelineOrchestrator`, `DsdConvertService`, and `DffMetadataStripper`, with different exception and warning behavior. Some cleanup failures throw, some are swallowed, and some cleanup is skipped on non-normal control flow.

**Direction:** Define ownership for every temporary or generated artifact and make cleanup policy explicit: guaranteed cleanup, best-effort cleanup, retained-for-resume, or delete-on-success.

## Recommended refactoring sequence

### Stage 1 — Make lifecycle and result semantics truthful

1. Put `LogPaths.Reset` under exception-safe scope management.
2. Stop rewriting a grace-killed process as exit code `0`; add an explicit termination reason.
3. Preserve primary conversion errors when temporary cleanup fails.
4. Return aggregate derivation failures instead of unconditional success.
5. Fix path-segment containment and centralize the implementation.
6. Remove partial output on non-exceptional DFF-strip validation failure.

These changes reduce correctness risk without changing the major architecture.

### Stage 2 — Extract pipeline workflow boundaries

1. Extract output-layout calculations.
2. Extract partial/success cleanup policy.
3. Extract per-disc state routing from `ProcessIsoAsync`.
4. Extract parse/probe/gain/convert/derive from `ConvertDiscAsync`.
5. Replace the `DiscAssessment` boolean bag with explicit states.

Keep `PipelineOrchestrator` as the batch coordinator and preserve its documented use of `DsdConvertService`.

### Stage 3 — Decompose conversion internals

1. Centralize DSDIFF chunk reading.
2. Extract gain analysis.
3. Extract track conversion/aggregation and metadata application.
4. Extract single-file conversion and derived-directory workflows.
5. Keep a narrow `DsdConvertService` facade only if it remains useful to callers.

### Stage 4 — Restore CLI and diagnostic boundaries

1. Move the direct DSD workflow into an Audio application service.
2. Make `DsdConvertCommand` a settings/result adapter like `SacdConvertCommand`.
3. Isolate or relocate `SacdProbeRunner`, including journal persistence and direct process execution.
4. Inject diagnostic components where the probe must remain in the production project.

### Stage 5 — Add focused verification seams

Because the repository does not use a test NuGet framework, prioritize standalone/manual verification around pure or replaceable seams:

- path-segment containment, including sibling paths;
- DFF chunk walking and malformed-size handling;
- explicit process termination reasons;
- disc assessment state transitions;
- per-track aggregation and partial failure behavior;
- cleanup ownership and cancellation paths;
- derived-output completeness and exit-code mapping.

## Final classification by file

| File                         | Classification              | Assessment                                                                             |
| ---------------------------- | --------------------------- | -------------------------------------------------------------------------------------- |
| `AudioSetup.cs`              | Low design trade-off        | Focused composition root; eager PATH validation couples startup to optional binaries.  |
| `AudioMetadataService.cs`    | Low-Medium SRP drift        | Reading, generic writing, and CUE mapping are separate metadata use cases.             |
| `AudioModels.cs`             | Low                         | Mixed contracts and conversion policy, but no god-class behavior.                      |
| `CueParser.cs`               | Low-Medium testability      | Cohesive parser with hidden file I/O and encoding selection.                           |
| `DffMetadataStripper.cs`     | Medium                      | Focused stripper, but duplicated chunk walking and incomplete-output cleanup gap.      |
| `DiscOutputInspector.cs`     | Medium-High god-class drift | Scanning, parsing coordination, completeness policy, layout, and state classification. |
| `DiskSpaceChecker.cs`        | None                        | Cohesive single policy.                                                                |
| `DsdConvertService.cs`       | High god class              | DFF parsing, gain, conversion, split, metadata, and derivation.                        |
| `FlacCompletenessChecker.cs` | Low-Medium boundary drift   | Duration validation plus artifact discovery.                                           |
| `LogPaths.cs`                | Medium cross-cutting risk   | Process-global mutable state and duplicated weak containment logic.                    |
| `PathValidator.cs`           | High correctness risk       | Unsafe string-prefix containment.                                                      |
| `PipelineOrchestrator.cs`    | High god class              | Batch workflow, state routing, layout, conversion, and cleanup.                        |
| `ProcessRunner.cs`           | High god method             | Process lifecycle and tool-specific completion policy in one state machine.            |
| `RealDffFixture.cs`          | Low diagnostic coupling     | Hard-coded machine path and another DFF chunk walker.                                  |
| `SacdExtractService.cs`      | None/Low                    | Focused external-tool adapter.                                                         |
| `SacdProbeRunner.cs`         | Medium diagnostic god class | Harness, console, direct process, classification, and journal persistence.             |
| `SacdProbeService.cs`        | Low                         | Thin wrapper, but runner construction is not injected.                                 |
| `SaraconService.cs`          | Medium method boundary      | Tool adapter also sanitizes DFF and validates output integrity.                        |
| `SoxService.cs`              | None                        | Focused external-tool operations.                                                      |
| `AudioCommandModule.cs`      | None                        | Registration only.                                                                     |
| `DsdConvertCommand.cs`       | Medium layering violation   | CLI contains an end-to-end direct-file conversion workflow.                            |
| `SacdConvertCommand.cs`      | None                        | Thin command boundary.                                                                 |

## Bottom line

The subsystem does not need indiscriminate extraction of every long method. The strongest case is targeted: make process and cleanup outcomes truthful, remove global/path-policy duplication, then split `PipelineOrchestrator` and `DsdConvertService` along workflow and format boundaries. `SoxService`, `SacdExtractService`, `DiskSpaceChecker`, `SacdConvertCommand`, and `AudioCommandModule` already demonstrate the focused component shape worth preserving.
