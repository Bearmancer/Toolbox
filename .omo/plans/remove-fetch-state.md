# remove-fetch-state - Work Plan

## TL;DR (For humans)

**What you'll get:** Last.fm sync no longer depends on a separate `fetch-state.json` file. The sync window is derived from existing scrobbles data, and all sync history is tracked via logs.

**Why this approach:** The `fetch-state.json` file was already deleted. Rather than maintaining redundant state, we derive the incremental sync point from the scrobbles file itself (newest `PlayedAt`). Logs already capture every sync run with counts and timestamps.

**What it will NOT do:** No changes to YouTube `manifest.json` (that serves a different purpose — ETag change detection). No changes to the `LastFmService` itself.

**Effort:** Quick
**Risk:** Low - simple refactor, no behavioral change
**Decisions to sanity-check:** None — straightforward removal of dead state tracking.

Your next move: approve. Full execution detail follows below.

---

> TL;DR (machine): Quick, Low risk — remove `LastFmFetchState` from `SyncLastFmCommand`, derive sync window from scrobbles.

## Scope
### Must have
- Remove `LastFmFetchState` usage from `SyncLastFmCommand.cs`
- Derive incremental sync window from existing scrobbles (`LastFmScrobble.PlayedAt`)
- Keep `--since` flag for force resync
- Delete `LastFmFetchState.cs` if unused elsewhere

### Must NOT have (guardrails, anti-slop, scope boundaries)
- No changes to YouTube `manifest.json` or `YouTubeFetchState`
- No changes to `LastFmService.cs` (fetch/merge/save logic stays)
- No new files or abstractions

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: none (solo-dev rules — no test NuGet packages; manual verification via build)
- Evidence: .omo/evidence/task-1-remove-fetch-state.txt (build output)

## Execution strategy
### Parallel execution waves
> Target 5-8 todos per wave. Fewer than 3 (except the final) means you under-split.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 | none | 2 | none |
| 2 | 1 | none | none |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [ ] 1. Remove `LastFmFetchState` from `SyncLastFmCommand.cs`
  What to do / Must NOT do:
    - Remove `StatePath` field (line 17)
    - Remove `LastFmFetchState.LoadAsync` call (line 24)
    - Remove `LastFmFetchState.SaveAsync` call (line 68)
    - Remove `newState` construction (lines 61-67)
    - Derive `fetchAfter` from existing scrobbles: `existing.Max(s => s.PlayedAt)` when `state.FetchComplete` was true
    - Keep `--since` flag logic unchanged
    - Remove `using Services.LastFm.Models` if `LastFmFetchState` was the only import from that namespace
  Parallelization: Wave 1 | Blocked by: none | Blocks: 2
  References (executor has NO interview context - be exhaustive):
    - `src/CLI/Sync/LastFm/SyncLastFmCommand.cs` (entire file)
    - `src/Services/LastFm/Models/LastFmScrobble.cs` (for `PlayedAt` property)
  Acceptance criteria (agent-executable): `dotnet build src/CLI/CLI.csproj` succeeds with zero errors
  QA scenarios (name the exact tool + invocation):
    - happy: `dotnet build src/App/App.csproj` — must succeed
    - failure: grep for `LastFmFetchState` in `SyncLastFmCommand.cs` — must return zero matches
  Commit: Y | refactor(lastfm): remove fetch-state.json dependency from sync command

- [ ] 2. Delete `LastFmFetchState.cs` if unused
  What to do / Must NOT do:
    - Grep entire `src/` for `LastFmFetchState` references
    - If zero references remain after task 1, delete `src/Services/LastFm/Models/LastFmFetchState.cs`
    - If references remain (e.g., from a future-implementation file), leave the file but note which files reference it
  Parallelization: Wave 2 | Blocked by: 1 | Blocks: none
  References (executor has NO interview context - be exhaustive):
    - `src/Services/LastFm/Models/LastFmFetchState.cs` (the file to potentially delete)
  Acceptance criteria (agent-executable): `rg LastFmFetchState src/` returns zero matches (or only future-implementation notes)
  QA scenarios (name the exact tool + invocation):
    - happy: `dotnet build src/App/App.csproj` — must succeed after deletion
    - failure: file does not exist and build succeeds — confirmed safe deletion
  Commit: Y | cleanup(lastfm): delete unused LastFmFetchState model

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. Plan compliance audit: `rg LastFmFetchState src/` returns zero matches
- [ ] F2. Code quality review: `dotnet build src/App/App.csproj` — zero warnings
- [ ] F3. Scope fidelity: no new files created, no changes to YouTube code

## Commit strategy
1. `refactor(lastfm): remove fetch-state.json dependency from sync command` — SyncLastFmCommand.cs
2. `cleanup(lastfm): delete unused LastFmFetchState model` — LastFmFetchState.cs (if safe)

## Success criteria
- `rg LastFmFetchState src/` returns zero matches
- `dotnet build src/App/App.csproj` succeeds with zero errors
- `--since` flag still works for force resync
- Incremental sync derives window from scrobbles data
