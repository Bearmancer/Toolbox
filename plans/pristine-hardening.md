---
slug: pristine-hardening
status: plan-written
intent: clear
review_required: true
plan_path: .omo/plans/pristine-hardening.md
plan_sha256: null
review_round_id: pristine-hardening-r1-20260819
pending-action: review .omo/plans/pristine-hardening.md
review:
  momus:
    status: pending
    workspace_root: C:\Users\Lance\Dev\Toolbox
    runtime_home: null
    target: .omo/plans/pristine-hardening.md
    round_id: pristine-hardening-r1-20260819
    plan_sha256: null
    launch_id: pristine-hardening-r1-momus
    session: null
    result: null
  independent:
    status: pending
    workspace_root: C:\Users\Lance\Dev\Toolbox
    runtime_home: null
    target: .omo/plans/pristine-hardening.md
    round_id: pristine-hardening-r1-20260819
    plan_sha256: null
    launch_id: pristine-hardening-r1-oracle
    session: null
    result: null
approach: Harden Pristine browser lifecycle, resolver readiness, cookie restoration, structured diagnostics, retry isolation, and live verification without adding test packages or logging secrets.
---

# Draft: pristine-hardening

## Components (topology ledger)
<!-- id | outcome (one line) | status: active|deferred | evidence path -->
| browser-auth | Authenticated Playwright context launches and restores valid cookies/local storage | active | `src/Services/Pristine/PristineBrowser.cs:9-230`, `state/auth/pristine/auth.json` |
| album-resolver | Search waits for rendered controls/results, resolves PASC deterministically, and fails classified | active | `src/Services/Pristine/PristineAlbumService.cs:12-166`, `state/logs/pristine.jsonl` |
| poll-download | Album playback, candidate selection, bounded downloads, ffprobe verification, and page cleanup are isolated | active | `src/Services/Pristine/PristinePollService.cs`, `PristineDownloader.cs`, `PristineAudioVerifier.cs` |
| diagnostics | Every browser failure exposes operation, target, timing, exception, and safe page state; failure artifacts are retained locally | active | `src/Core/Telemetry.cs:15-101`, `state/logs/pristine.jsonl` |
| live-verification | Single PASC proof and multi-PASC/concurrency assertions produce durable evidence | active | `src/App/Program.cs:114-123`, `playwright-cli` skill, `ffprobe` PATH |

## Open assumptions (announced defaults)
<!-- Record defaults; user may veto at approval gate. -->
| assumption | adopted default | rationale | reversible? |
|---|---|---|---|
| Playwright readiness | Locator-first actions with explicit Playwright timeouts plus caller `WaitAsync(ct)` | Official Playwright guidance and current `FillCancelled` evidence show caller cancellation alone is insufficient | yes |
| retry isolation | Recreate the page for transient browser/protocol failures; do not retry caller cancellation/auth/schema failures | Prevents stale DOM/page state from contaminating later attempts | yes |
| diagnostics | Capture screenshots/traces on every failure, store locally under `state/pristine/diagnostics`, and apply bounded cleanup/access warnings | User selected always-on failure artifacts; traces can contain authenticated/private content and network payloads | yes |
| test infrastructure | Tests-after using existing build/manual tooling; no new test NuGet packages | Repo explicitly prohibits test packages and has no test project | yes |
| secrets | Never edit or print `.env` values; validate Azure deployment metadata through redacted CLI queries | Existing credentials are local operational state, not product-code configuration | yes |

## Findings (cited - path:lines)

- Current LSP diagnostics: zero errors across `src/Services/Pristine` at planning time.
- Previous live run: `state/logs/pristine.jsonl` reached `Pristine.Album.ResolveStart` then `FillCancelled` after the 45-second resolver deadline; no FLAC attempt followed.
- Previous cookie run: two `__Host-pristine_*` cookies were rejected as invalid fields before normalization; `PristineBrowser` now normalizes `Domain`, `Path`, and `Secure`, but this needs live re-verification.
- `PristineAlbumService.ResolveAlbumIdAsync` now uses `ILocator`, visibility waits, explicit action timeouts, and caller cancellation, but still has an empty recovery `catch` at current lines ~151-160 and still relies on URL/code matching after result visibility.
- `PristinePollService` retains `tracklistResult.Match(t => t, _ => [])` at current line ~121; historical `CS0029` showed this shape is fragile under collection-expression inference. Replace with explicit `IsError` unwrapping even though current diagnostics are clean.
- `PristineAlbumService.ParseTracklistAsync` retains `raw is not null ? [.. raw] : []` at current line ~269; historical `CS8602` identified this as a nullable spread regression risk. Replace with `[ .. raw ?? [] ]` form and verify.
- `Errors.Pristine.TracklistParseFailed` exists at `src/Core/Errors.cs:186-187`; historical `CS0117` is resolved and becomes a regression gate, not a new implementation requirement.
- `Program.Main` already owns Ctrl+C: `appCts` and `Console.CancelKeyPress` at `src/App/Program.cs:114-121`, token passed into Spectre `RunAsync`; no second cancellation architecture is needed.
- Existing telemetry is service-scoped JSONL with optional Seq at `src/Core/Telemetry.cs:15-101`; preserve this contract and avoid global logging rewrites.
- No test files were found by repository glob; manual/CLI evidence is required.
- Official Microsoft Learn confirms `az cognitiveservices account deployment list` and `show`; local resource/deployment discovery previously identified `ai-lance-openai` / `rg-lance` / `gpt-4o`. Plan must validate without exposing keys.
- Official Playwright .NET sources confirm `Playwright.CreateAsync` and context creation have no native cancellation parameter; explicit Playwright timeouts plus caller deadline and page/context recovery are required.
- Official Playwright guidance: Locator actions auto-wait for actionability; per-operation/default timeouts are the supported deadline controls; .NET Playwright cancellation is not native and `WaitAsync` does not abort the underlying RPC. Sources: `https://playwright.dev/dotnet/docs/actionability`, `https://playwright.dev/dotnet/docs/api/class-locator`, `https://github.com/microsoft/playwright-dotnet/issues/1652`.
- Official Playwright tracing supports `Tracing.StartAsync`/`StopAsync` with screenshots, snapshots, and sources; traces may contain cookies, DOM values, auth headers, and request bodies. Sources: `https://playwright.dev/dotnet/docs/trace-viewer`, `https://playwright.dev/dotnet/docs/api/class-tracing`, `https://github.com/microsoft/playwright/issues/19992`.
- Official Microsoft Learn confirms endpoint lookup with `az cognitiveservices account show`, deployment lookup with `az cognitiveservices account deployment list`, and state validation with `deployment show`; local `az --help` confirmed required `--name` and `--resource-group` parameters.

## Decisions (with rationale)

- Keep cancellation centralized in `Program.Main`; propagate existing token through CLI → orchestrator → service methods. Do not introduce a second global token service.
- Keep output at user-requested `Desktop\\Pristine`; do not reintroduce an output environment variable. Diagnostics remain under `state/pristine/diagnostics` with always-on failure capture per owner decision.
- Preserve sequential album order and max five track-download slots; no album-level parallelism.
- No stream/bit-depth assumptions: candidate discovery precedes download; ffprobe must inspect every downloaded file, and missing ffprobe must fail rather than infer from extension.
- Use fresh locators rather than `IElementHandle` for dynamic search/result DOM.
- Treat `OperationCanceledException` as terminal caller cancellation; retry only bounded transient browser/protocol/action failures.
- Keep `Telemetry.ForService(ServiceName.Pristine)` and JSONL property names stable; add Pristine-scoped diagnostics instead of changing all services.

## Scope IN

- Validate and harden all Pristine browser async calls, Playwright timeouts, cancellation observation, and cleanup.
- Normalize storage-state cookies, especially `__Host-` invariants, with metadata-only logging.
- Make album resolution application-state driven: visible search locator, result locator, direct ID/href parsing, title verification, bounded recovery.
- Add structured Playwright failure diagnostics, safe page-state capture, request/console/page-failure hooks, and always-on failure trace/screenshot evidence with bounded cleanup.
- Isolate retries on fresh pages, preserve sequential albums/max-five track downloads, and make request-handler cleanup unconditional.
- Replace verified historical fragile ErrorOr/nullable collection-expression patterns.
- Validate Azure deployment prerequisite using official CLI commands without modifying secrets.
- Execute Playwright CLI/manual live QA, single-FLAC proof, ffprobe assertions, sequential PASC, and concurrency evidence.
- Append all attempts/failures/expected-vs-actual/rationale to the existing running log.

## Scope OUT (Must NOT have)

- No implementation by planner or delegated planner subagents.
- No new Playwright/NUnit/xUnit/MSTest test package or test project.
- No hardcoded credentials, cookie values, signed media URLs, or secrets in logs/artifacts.
- No API reverse engineering, GUI automation outside Playwright, stream transcoding, or inferred 16-bit output.
- No album-level concurrency, database/state-store migration, or unrelated Core/Telemetry redesign.
- No destructive deletion of user auth/profile/output data; cleanup commands must target only explicitly named diagnostic/test artifacts.

## Open questions

1. **Diagnostic artifact privacy** — recommendation: enable screenshots/traces only when `--debug`/Verbose is active, write to `state/pristine/diagnostics`, retain one failed run plus bounded cleanup, and document that artifacts may contain authenticated page data. Alternatives: always-on failure artifacts, or metadata-only/no screenshots/traces.
2. **Topology lock** — confirm the five components above are the intended independent work shape; no scope change is implied by confirmation.

## Resolved owner decisions

- Topology confirmed: browser/auth, album resolver, poll/download, diagnostics, live verification.
- Failure screenshots/traces: always capture on failure, local-only under `state/pristine/diagnostics`, bounded cleanup, explicit sensitive-artifact warning.

## Approval gate
status: plan-written
approach: Implement verified fixes in dependency waves: compile/history hardening; browser/auth and cancellation cleanup; resolver correctness and fresh-page retry isolation; poll/download/strict 16-bit selection; structured diagnostics/tracing and state hygiene; then Playwright CLI/manual live verification with ffprobe and Azure prerequisite checks.
 next-action: start worker with `$start-work pristine-hardening`; planning is complete and implementation remains separate.

## Metis gap analysis folded in

- Fresh-page retry, structured artifacts, strict ffprobe failure, and Azure validation are missing implementation—not merely verification.
- Strict audio rule is split into two gates: select only candidates with explicit 16-bit evidence before download; immediately ffprobe after download and delete/reject anything not FLAC/16-bit. Remote URLs cannot be ffprobed before bytes exist.
- Bounds adopted: max 3 transient retries with 2s/4s backoff; diagnostics retain 10 screenshots, 5 traces, 500 MB total, 7 days; cleanup never touches auth/output files.
- Cancellation is never retried. Cleanup uses an independent short timeout so cancelled tokens cannot prevent page/context disposal.
- Failure results must produce nonzero CLI status; no green success output for an album marked error.
<!-- After topology/privacy decisions are resolved, set status: awaiting-approval and present the brief. -->
