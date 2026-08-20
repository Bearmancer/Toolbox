# pristine-hardening - Work Plan

## TL;DR (For humans)

**What you'll get:** Pristine downloader that fails loudly, cleans up safely, preserves authentication, selects only explicitly identified 16-bit FLAC candidates, verifies every final file with ffprobe, and produces usable failure evidence.

**Why this approach:** Fix browser readiness and lifecycle first; then enforce media correctness and result semantics; only then run live downloads. This prevents another green build/zero-download cycle.

**What it will NOT do:** No API reverse engineering, no stream transcoding, no album-level parallelism, no new test framework, no secret logging, and no deletion of user auth/output data.

**Effort:** XL  
**Risk:** High - authenticated browser automation, persistent cookies, external site behavior, and diagnostic artifacts.
**Decisions to sanity-check:** Always-on failure artifacts approved; five-component topology approved; execution remains separate via `$start-work`.

Your next move: start execution in a worker session with `$start-work pristine-hardening` after reviewing this plan.

---

> TL;DR (machine): 12 implementation/verification todos harden Pristine browser/auth, resolver, media gates, diagnostics, state hygiene, and live QA; 4 final audits.

## Scope

### Must have

- Preserve `Desktop\\Pristine` default output and sequential album order.
- Preserve maximum five concurrent track downloads within one album.
- Propagate existing `Program.Main` cancellation token; do not add a second global cancellation architecture.
- Use Locator-first Playwright actions, explicit per-operation timeouts, and bounded fresh-page retries.
- Normalize `__Host-` cookies correctly: `Secure=true`, `Path=/`, no `Domain`; never log values.
- Capture structured failure metadata and always capture failure screenshots/traces locally under `state/pristine/diagnostics`, with bounded cleanup.
- Select only candidates with explicit 16-bit evidence before download; ffprobe immediately after download; delete/reject non-FLAC or non-16-bit files.
- Make missing ffprobe a hard verification failure; never infer bit depth from extension.
- Make failed albums produce nonzero CLI status; never print failure markers as green success.
- Validate Azure deployment prerequisites with redacted official CLI commands; never edit or print `.env` secrets.
- Produce durable evidence for single-FLAC, ffprobe, concurrency, and sequential-PASC scenarios.

### Must NOT have (guardrails, anti-slop, scope boundaries)

- No implementation by planner; execution only through `$start-work`.
- No new Playwright/NUnit/xUnit/MSTest package or test project.
- No hardcoded credentials, cookie values, signed media URLs, request bodies, or auth tokens in Telemetry, manifests, commits, or staged artifacts. Raw failure traces may contain browser data by Playwright design; they remain local-only, gitignored, bounded, and explicitly marked sensitive.
- No API reverse engineering, GUI automation outside Playwright, transcoding, database, or album-level concurrency.
- No destructive deletion of `state/auth`, browser profile, or final output; cleanup may delete only explicitly scoped diagnostic files and failed `.part` artifacts.
- No global Telemetry redesign; retain `Telemetry.ForService(ServiceName.Pristine)` and existing JSONL event names/properties unless a migration is documented.

## Verification strategy

> Zero human intervention - all verification is agent-executed.

- Test decision: **tests-after/manual integration**, because current Toolbox has no test project and repository rules prohibit test NuGet packages.
- Static gate after each todo: `dotnet build --nologo -v q`; expected 0 warnings/0 errors.
- Diagnostics gate: `lsp_diagnostics` on every changed C# file; expected no errors.
- Browser QA: use `playwright-cli` if installed for snapshots, cookie inspection, request/console evidence, and trace inspection; otherwise use the bundled Microsoft.Playwright .NET driver plus trace viewer script. Record tool availability rather than installing unrelated dependencies.
- Live commands:
  - `dotnet run --project src/App -- pristine download PASC552 --single --headless --debug`
  - `dotnet run --project src/App -- pristine download PASC552 PASC553 --headless --debug`
- Audio gate: `ffprobe -v error -print_format json -show_streams <file>`; require codec `flac`, bits `16`, sample rate `44100` where the source reports it, and at least one audio stream.
- Azure prerequisite gate, redacted only:
  - `az cognitiveservices account show -g rg-lance -n ai-lance-openai --query properties.endpoint -o tsv`
  - `az cognitiveservices account deployment list -g rg-lance -n ai-lance-openai --query "[].{name:name,model:properties.model.name,version:properties.model.version,state:properties.provisioningState}" -o table`
  - `az cognitiveservices account deployment show -g rg-lance -n ai-lance-openai --deployment-name gpt-4o --query properties.provisioningState -o tsv`
  - Never run `keys list` in captured output.
- Evidence root: `.omo/evidence/` with one artifact directory per todo; append expected output, actual output, rationale, and failure classification to `state/pristine-refactor-log.md`.

## Execution strategy

### Parallel execution waves

- **Wave 1:** Todos 1-3 are independent source/config hardening after baseline capture; do not edit the same file concurrently.
- **Wave 2:** Todos 4-6 are dependent: diagnostics contract before retries/resolver integration; resolver and retry changes may touch shared Poll/Album files and execute sequentially within this wave.
- **Wave 3:** Todos 7-9 depend on stable resolver/download lifecycle; strict media, downloader cleanup, and result semantics can be split only by file ownership.
- **Wave 4:** Todos 10-12 are verification/preflight tasks; live runs are strictly sequential and isolated.
- **Final wave:** F1-F4 run in parallel after all todos.

### Dependency matrix

| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 | baseline | 4-12 | 2, 3 |
| 2 | baseline | 4, 10-12 | 1, 3 |
| 3 | baseline | 4-6, 10-12 | 1, 2 |
| 4 | 1-3 | 5-6, 10-12 | none |
| 5 | 3-4 | 6, 10-12 | none |
| 6 | 3-4 | 7, 10-12 | none |
| 7 | 6 | 8, 10-12 | none |
| 8 | 7 | 9-12 | none |
| 9 | 5, 8 | 10-12 | none |
| 10 | 1-9 | 11-12 | none |
| 11 | 1-10 | 12 | none |
| 12 | 1-11 | F1-F4 | none |

## Todos

> Implementation + Test = ONE todo. Never separate.

- [ ] 1. Harden historical compiler patterns and resolver failure boundaries
  What to do / Must NOT do: Replace `PristinePollService.cs:121` `ErrorOr.Match(t => t, _ => [])` with explicit `IsError`/`Value` handling; replace `PristineAlbumService.cs:269` nullable spread with null-coalescing spread; remove the empty recovery catch around current resolver recovery; preserve `Errors.Pristine.TracklistParseFailed` and do not use null-forgiving operators. Do not alter unrelated ErrorOr users.
  Parallelization: Wave 1 | Blocked by: none | Blocks: 4, 6
  References (executor has NO interview context - be exhaustive): `src/Services/Pristine/PristinePollService.cs:120-124`; `src/Services/Pristine/PristineAlbumService.cs:263-282`; `src/Core/Errors.cs:163-191`; `mega_session_findings.md:27-81`; Microsoft C# pattern matching and collection expression docs.
  Acceptance criteria (agent-executable): `dotnet build --nologo -v q` returns 0 warnings/0 errors; grep finds no `tracklistResult.Match(t => t, _ => [])`, no `raw is not null ? [.. raw] : []`, and no empty catch in changed Pristine code; `TracklistParseFailed` remains referenced and builds.
  QA scenarios (name exact tool + invocation): happy: build + `lsp_diagnostics` pass; failure: a synthetic/error-path inspection confirms ErrorOr failure returns a classified error instead of defaulting silently. Evidence `.omo/evidence/task-1-pristine-hardening/`.
  Commit: Y | `fix(pristine): harden historical error paths`

- [ ] 2. Repair configuration and secret-state hygiene
  What to do / Must NOT do: Add `state/auth/`, `state/pristine/`, `state/pristine/diagnostics/`, and unrelated `.playwright-cli/` artifacts to `.gitignore` without deleting files; remove dead `PRISTINE_HEADLESS` environment write; reconcile stale docs/log statements with hardcoded `Desktop\\Pristine`; retain only error factories with consumers or explicitly document retained future factories. Do not print or commit auth values.
  Parallelization: Wave 1 | Blocked by: none | Blocks: 10-12
  References (executor has NO interview context - be exhaustive): `src/Services/Pristine/PristineCredentials.cs:5-19`; `src/CLI/Pristine/PristineDownloadCommand.cs:28-35,53-75`; `src/Services/Pristine/PristinePaths.cs:5-15`; `state/auth/pristine/auth.json`; `state/pristine/auth.json`; `Pristine Script.md`; `state/pristine-refactor-log.md`; `state/pristine-failure-log.md`.
  Acceptance criteria (agent-executable): `git check-ignore` confirms both auth locations and diagnostics; `git ls-files --error-unmatch` fails for both auth paths, `.playwright-cli`, traces, screenshots, and browser profiles; `git status --short` shows no auth file staged/tracked; `PRISTINE_HEADLESS` has no writer without reader; docs state `Desktop\\Pristine`; no secret value appears in diff/log evidence.
  QA scenarios (name exact tool + invocation): happy: fresh clone/status check shows auth ignored and untracked; failure: `git ls-files` and `git add -A --dry-run` do not list auth, traces, screenshots, or `.playwright-cli` artifacts. Evidence `.omo/evidence/task-2-pristine-hardening/`.
  Commit: Y | `chore(pristine): protect auth and diagnostic state`

- [ ] 3. Normalize browser cookies and guarantee async resource cleanup
  What to do / Must NOT do: Introduce an owned `PristineBrowserSession` record/file containing `IPlaywright` and `IBrowserContext`; return it from `PristineBrowser.CreateAsync`; dispose context first, then Playwright driver, on success/failure/cancellation. Normalize `__Host-` cookies with `Secure=true`, `Path=/`, no `Domain` (use `Url` only if required by the validated Playwright API); rethrow caller `OperationCanceledException` instead of treating it as a bad cookie/origin; close contexts/pages with a short independent cleanup timeout after caller cancellation. Preserve auth/local-storage restoration.
  Parallelization: Wave 1 | Blocked by: none | Blocks: 4-6, 10
  References (executor has NO interview context - be exhaustive): `src/Services/Pristine/PristineBrowser.cs:9-231`; `src/Services/Pristine/PristineLoginService.cs:11-150`; `src/Services/Pristine/PristinePollService.cs:14-600`; official Playwright BrowserContext cookie docs; MDN cookie prefix rules; Playwright .NET issue #1652.
  Acceptance criteria (agent-executable): cookie metadata logs show `Domain=(none)`, `Path=/`, `Secure=true` for every `__Host-` candidate; cancellation is never logged as `CookieRejected`; session disposal closes context and Playwright driver on success, exception, and cancellation using independent cleanup deadline; build/LSP clean.
  QA scenarios (name exact tool + invocation): happy: load `state/auth/pristine/auth.json` and inspect metadata-only cookie logs; failure: cancel during cookie restore and confirm OCE propagates while cleanup completes within bounded timeout. Evidence `.omo/evidence/task-3-pristine-hardening/`.
  Commit: Y | `fix(pristine): harden browser auth lifecycle`

- [ ] 4. Add structured Playwright diagnostics and failure artifacts
  What to do / Must NOT do: Add one Pristine-scoped diagnostics class/file using existing Telemetry; record operation, target, attempt, timeout, elapsed, exception type/message/stack, URL, readyState, and safe state. Sanitize exception text and URI query/fragment before Telemetry. Attach request-failed, console, and page-error hooks. Start tracing before the operation and capture screenshot plus trace on every failure under `state/pristine/diagnostics`; retain max 10 screenshots, 5 traces, 500 MB total, 7 days; mark raw traces sensitive. Never log cookie/media values and never claim raw traces are scrubbed.
  Parallelization: Wave 2 | Blocked by: 1-3 | Blocks: 5-12
  References (executor has NO interview context - be exhaustive): `src/Core/Telemetry.cs:15-101`; `src/Services/Pristine/PristineBrowser.cs`; `src/Services/Pristine/PristinePollService.cs`; Playwright actionability, tracing, screenshot, and debug docs; Serilog structured logging/enrichment/file-sink docs.
  Acceptance criteria (agent-executable): every diagnostics artifact has a timestamped failure identity; JSONL has stable `Pristine.Playwright.*` properties; screenshot and trace exist after an observed live failure; cleanup enforces all three bounds; sanitized Telemetry and artifact manifest contain no auth/cookie/media values; raw trace is gitignored and labeled sensitive.
  QA scenarios (name exact tool + invocation): happy: run a deliberately invalid PASC or bounded selector failure and inspect JSONL + PNG + trace; failure: make diagnostics capture itself fail and confirm original exception remains primary and cleanup logs a secondary diagnostic. Evidence `.omo/evidence/task-4-pristine-hardening/`.
  Commit: Y | `feat(pristine): add structured browser diagnostics`

- [ ] 5. Isolate transient album retries on fresh pages
  What to do / Must NOT do: Refactor `DownloadSingleAlbumAsync` so each transient browser/protocol/action attempt creates a fresh page from the authenticated `PristineBrowserSession.Context`; use max 3 attempts with 2s/4s backoff; retry `TimeoutException`/transient `PlaywrightException`/`TargetClosedException` only; never retry caller cancellation, auth rejection, invalid selector, or deterministic not-found. Replace `Task.Run(..., ct)` with a direct async task factory after semaphore acquisition so pre-start cancellation cannot bypass `gate.Release`; cancel and observe all pending tasks before returning; detach request handlers in an outer `finally`; close pages with independent cleanup timeout.
  Parallelization: Wave 2 | Blocked by: 3-4 | Blocks: 6-12
  References (executor has NO interview context - be exhaustive): `src/Services/Pristine/PristinePollService.cs:14-600`; `src/Services/Pristine/PristineBrowser.cs:9-231`; existing `SemaphoreSlim(5)` and `pendingDownloads`; Playwright .NET timeout/cancellation limitation docs.
  Acceptance criteria (agent-executable): each transient retry logs fresh page identity and attempt; no page is reused after timeout/protocol failure; `Task.WhenAll` observes every pending task with cancellation rethrown; semaphore release is guaranteed even when cancellation occurs before delegate execution; request handler removal occurs in outer `finally`; cancellation leaves no unobserved task fault and `.part` cleanup is attempted.
  QA scenarios (name exact tool + invocation): happy: observe one live transient retry if site produces a timeout; failure: static branch inspection plus invalid-PASC live run verifies deterministic failures do not retry as transient; cancellation: Ctrl+C during pending downloads yields clean exit, bounded page close, and no live child page. Evidence `.omo/evidence/task-5-pristine-hardening/`.
  Commit: Y | `fix(pristine): isolate transient album retries`

- [ ] 6. Make album resolution state-driven and deterministic
  What to do / Must NOT do: Keep Locator-first search visibility/fill/press with explicit timeouts; remove brittle URL-code gate as the primary success condition; wait for visible result locator, extract/parse a positive numeric `/albums/<id>` href or click locator, and accept only when the result item/title contains normalized requested PASC code. Verify album URL/id/title and classify selector/navigation failures. Keep three bounded resolver attempts but use fresh-page recovery from Todo 5. No empty catches or arbitrary NetworkIdle dependency. Use current C# selectors/design docs; do not reference nonexistent `old/` paths.
  Parallelization: Wave 2 | Blocked by: 3-5 | Blocks: 7, 10-12
  References (executor has NO interview context - be exhaustive): `src/Services/Pristine/PristineAlbumService.cs:12-166`; current `ILocator` implementation; `Pristine Script.md`; design spec selectors; Playwright actionability/locator docs; last live evidence in `state/logs/pristine.jsonl`.
  Acceptance criteria (agent-executable): resolver logs `SearchVisible`, `FillOk`, `EnterOk`, `ResultsVisible`, and resolved id/title; a missing/hidden input fails within 5s instead of 45s; result href/id resolves without requiring code in URL; deterministic mismatch returns classified resolve error.
  QA scenarios (name exact tool + invocation): happy: authenticated PASC552 page resolves id; failure: selector hidden, result absent, navigation timeout, and title mismatch each produce distinct structured evidence and bounded retry. Evidence `.omo/evidence/task-6-pristine-hardening/`.
  Commit: Y | `fix(pristine): make album resolution state-driven`

- [ ] 7. Enforce explicit 16-bit candidate selection and post-download gate
  What to do / Must NOT do: Capture all audio candidates before download; log `Pristine.Poll.Candidate` with evidence; accept only a candidate tied to an explicit quality-control attribute/option matching a strict 16-bit token and FLAC selection. Body text containing generic `16`, `CD`, or `FLAC`, and URL substrings alone are insufficient. Emit `Pristine.Poll.Selected16`; return `Errors.Pristine.No16BitFlac` before download when no eligible candidate exists. After download, ffprobe immediately; delete/reject non-FLAC/non-16-bit files and do not count them. Do not claim remote URL ffprobe before bytes exist.
  Parallelization: Wave 3 | Blocked by: 6 | Blocks: 8, 10-12
  References (executor has NO interview context - be exhaustive): `src/Services/Pristine/PristinePollService.cs:190-383`; `src/Core/Errors.cs:189-190`; `src/Services/Pristine/PristineAudioVerifier.cs:9-113`; design spec §6.3/§9; user requirement against bit-depth assumptions.
  Acceptance criteria (agent-executable): no `DownloadAsync` starts without an eligible 16-bit candidate; candidate/tier/selection events appear; non-16-bit downloaded bytes are deleted and excluded; absent candidate returns `No16BitFlac`; ffprobe absence is a hard failure.
  QA scenarios (name exact tool + invocation): happy: observed candidate set with explicit 16-bit and MP3 selects only 16-bit; failure: only MP3/unknown candidates returns no-download error; failure: downloaded FLAC reports 24-bit and is removed. Evidence `.omo/evidence/task-7-pristine-hardening/`.
  Commit: Y | `fix(pristine): enforce 16-bit media gate`

- [ ] 8. Harden downloader and verifier state semantics
  What to do / Must NOT do: Delete `.part` files on cancellation and terminal failure; preserve atomic final move; move `PristineProbeResult` to its own file to respect one-class-per-file; remove extension-based ffprobe fallback and expose a classified `NoFfprobe` failure; inspect every `codec_type == audio` stream, require at least one audio stream, require every audio stream to be FLAC/16-bit, and treat missing bit depth/sample rate as unknown/failure for the requested guarantee; kill/reap ffprobe on cancellation; keep stream count/codec/bits/rate/channels logging.
  Parallelization: Wave 3 | Blocked by: 7 | Blocks: 9-12
  References (executor has NO interview context - be exhaustive): `src/Services/Pristine/PristineDownloader.cs:10-82`; `src/Services/Pristine/PristineAudioVerifier.cs:9-125`; `src/Services/Pristine/PristinePollService.cs:304-383`; repo AGENTS.md no-assumptions and one-class-per-file rules.
  Acceptance criteria (agent-executable): cancellation/exception removes `.part`; failed verification never leaves final non-16-bit file; missing ffprobe returns error without extension inference; verifier result type has its own file; build/LSP clean.
  QA scenarios (name exact tool + invocation): happy: valid FLAC produces atomic final file and complete probe record; failure: cancel during copy, simulate missing ffprobe, corrupt file, non-FLAC, 24-bit FLAC; each leaves no final success artifact. Evidence `.omo/evidence/task-8-pristine-hardening/`.
  Commit: Y | `fix(pristine): make media verification authoritative`

- [ ] 9. Correct failure result and CLI exit semantics
  What to do / Must NOT do: Add an explicit `Succeeded`/failure state to `PristineAlbumResult` or an equivalent structured status. Exit `0` only when every requested album succeeds and every required file is verified; exit `1` for any failed/unverified album, including mixed results; preserve partial-success reporting with explicit failed status/error details; update ErrorOr taxonomy only where consumed; keep CLI output safe and deterministic.
  Parallelization: Wave 3 | Blocked by: 5, 8 | Blocks: 10-12
  References (executor has NO interview context - be exhaustive): `src/Services/Pristine/PristineOrchestrator.cs:140-180`; `src/CLI/Pristine/PristineDownloadCommand.cs:28-50`; `src/Services/Pristine/PristineModels.cs:9-16`; `src/Core/Errors.cs:163-191`.
  Acceptance criteria (agent-executable): all-success returns `0`; all-failed returns `1`; mixed success/failure returns `1`; zero-track/unverified result returns `1`; no `[green]` output for failure results; errors remain structured.
  QA scenarios (name exact tool + invocation): happy: one valid + one invalid code yields one success and one failure marker; failure: all invalid codes exits nonzero and logs terminal error. Evidence `.omo/evidence/task-9-pristine-hardening/`.
  Commit: Y | `fix(pristine): report album failures truthfully`

- [ ] 10. Add state-safe Azure and runtime preflight verification
  What to do / Must NOT do: Add an agent-executed preflight checklist, not a secret-bearing product config change: verify `.env` exists and these 15 keys are present without printing values: `TEXT_ANALYTICS_ENDPOINT`, `TEXT_ANALYTICS_KEY`, `TRANSLATOR_ENDPOINT`, `TRANSLATOR_KEY`, `TRANSLATOR_REGION`, `DOCINTEL_ENDPOINT`, `DOCINTEL_KEY`, `VISION_ENDPOINT`, `VISION_KEY`, `OPENAI_ENDPOINT`, `OPENAI_KEY`, `OPENAI_DEPLOYMENT`, `SPEECH_ENDPOINT`, `SPEECH_KEY`, `SPEECH_REGION`; official Azure CLI read-only endpoint/deployment commands report `gpt-4o` provisioning `Succeeded`; verify Edge, bundled Playwright driver, auth path, Desktop output, and ffprobe. Do not run `keys list` in evidence.
  Parallelization: Wave 4 | Blocked by: 1-9 | Blocks: 11-12
  References (executor has NO interview context - be exhaustive): `src/App/Program.cs:35-123`; `src/Services/Azure/AzureCredentials.cs:21-43`; `src/Services/Pristine/PristineCredentials.cs:5-19`; `src/Services/Pristine/PristinePaths.cs:5-15`; Microsoft Learn `account show`, `account deployment list/show`; current local resource `ai-lance-openai`, group `rg-lance`, deployment `gpt-4o`.
  Acceptance criteria (agent-executable): all redacted preflight commands exit 0; deployment state is `Succeeded`; evidence contains endpoint/model/state only; missing prerequisite stops live QA with classified reason, not a misleading download result.
  QA scenarios (name exact tool + invocation): happy: run all preflight commands and all-key presence checks; failure: temporarily use an invalid deployment name in an isolated process and verify preflight fails without printing credentials; failure: omit one required startup key in an isolated environment and verify startup failure is classified before live QA. Evidence `.omo/evidence/task-10-pristine-hardening/`.
  Commit: N | verification-only, no source commit unless preflight code is explicitly required by executor.

- [ ] 11. Prove single-FLAC and strict ffprobe result
  What to do / Must NOT do: Clean only explicitly scoped output/diagnostic artifacts, run `dotnet run --project src/App -- pristine download PASC552 --single --headless --debug`, monitor `state/logs/pristine.jsonl` continuously, inspect artifacts, and run ffprobe on the final file. Stop on failure and preserve evidence; do not retry blindly.
  Parallelization: Wave 4 | Blocked by: 10 | Blocks: 12
  References (executor has NO interview context - be exhaustive): `src/CLI/Pristine/PristineDownloadCommand.cs:28-50`; `src/Services/Pristine/PristinePollService.cs:singleTrack path`; `src/Services/Pristine/PristineAudioVerifier.cs`; `state/pristine-refactor-log.md`; Playwright CLI tracing/snapshot guidance.
  Acceptance criteria (agent-executable): exactly one final FLAC exists below `Desktop\\Pristine`; JSONL contains resolver, candidate, selected16, download, probe, and completion events; ffprobe reports FLAC/16-bit/44100 when source reports rate; no `.part` remains; exit code 0 only on verified success.
  QA scenarios (name exact tool + invocation): happy: live command plus ffprobe; failure: no auth, hidden search, no 16-bit candidate, ffprobe failure, timeout, and Ctrl+C each produce evidence and no false success. Evidence `.omo/evidence/task-11-pristine-hardening/`.
  Commit: N | verification-only; append complete attempt record to running log.

- [ ] 12. Prove full-album max-five concurrency and sequential multi-PASC behavior
  What to do / Must NOT do: Run a full album without `--single`, observe `.part` files/process logs to prove max five in-flight track downloads, then run `PASC552 PASC553` and prove album 2 starts only after album 1 completes. Keep album order sequential; stop if any result is unverified.
  Parallelization: Wave 4 | Blocked by: 11 | Blocks: F1-F4
  References (executor has NO interview context - be exhaustive): `src/Services/Pristine/PristinePollService.cs:230-560`; `src/Services/Pristine/PristineOrchestrator.cs:140-180`; design spec §8 and §12.
  Acceptance criteria (agent-executable): observed concurrent download count never exceeds 5; all final tracks pass ffprobe; `AlbumStart PASC553` timestamp follows `AlbumDone PASC552`; no album-level overlap; output/log evidence is durable.
  QA scenarios (name exact tool + invocation): happy: full album then two-code run; failure: invalid-PASC run plus Ctrl+C during an album leave no unobserved task faults or false green completion; static inspection verifies the track-failure branch and semaphore release path. Evidence `.omo/evidence/task-12-pristine-hardening/`.
  Commit: N | verification-only; append complete attempt record to running log.

## Final verification wave

> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.

- [ ] F1. Plan compliance audit
  Verify every Must-have/Must-NOT-have, historical finding, owner decision, and acceptance criterion against diff, logs, artifacts, and commands; reject claims based only on agent summaries.
- [ ] F2. Code quality review
  Review changed C# for `.editorconfig`, no suppression pragmas, no `!`, no empty catches, one-class-per-file, cancellation/cleanup correctness, and no secret leakage.
- [ ] F3. Real manual QA
  Re-run preflight, single-FLAC, ffprobe, full-album concurrency, and sequential-PASC scenarios; inspect JSONL, trace, screenshots, `.part` cleanup, exit codes, and process cleanup.
- [ ] F4. Scope fidelity
  Confirm only Pristine scope, approved state hygiene, diagnostics, docs/evidence, and required Azure preflight changed; no API reverse engineering, transcoding, unrelated service changes, or auth deletion.

## Commit strategy

- One atomic commit per implementation todo, maximum 1-3 source/config files where practical.
- Verification-only todos append evidence and running-log records without source commits.
- Never stage auth, `.env`, output, screenshots, traces, or stale browser profiles.
- Build after every source edit; do not proceed with a broken worktree.
- Suggested sequence: `fix(pristine): harden error paths`; `chore(pristine): protect state`; `fix(pristine): browser lifecycle`; `feat(pristine): diagnostics`; `fix(pristine): retry isolation`; `fix(pristine): resolver`; `fix(pristine): media gate`; `fix(pristine): verifier cleanup`; `fix(pristine): failure semantics`.

## Success criteria

- `dotnet build --nologo -v q` and LSP diagnostics are clean.
- No current Pristine catch silently swallows errors or caller cancellation.
- Auth cookies restore without `__Host-` rejection; auth values never appear in logs/artifacts.
- Resolver either resolves PASC deterministically or fails within bounded, classified retries with page recreation.
- Missing ffprobe, unknown quality, non-FLAC, and non-16-bit content cannot produce final success.
- Single PASC produces exactly one verified 16-bit FLAC.
- Full album never exceeds five concurrent track downloads.
- Two PASC codes execute strictly sequentially.
- Failure screenshots/traces are always captured locally on failure, bounded to 10 screenshots/5 traces/500 MB/7 days, and documented as sensitive.
- CLI exit status truthfully reflects all-failed and mixed-result policies.
- Azure deployment preflight validates endpoint/deployment state without exposing keys.
