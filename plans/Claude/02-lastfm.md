---
concern: LastFm
status: active — must be redone, not resumed
ref: github.com/Bearmancer/Toolbox @ fe6e322d (master)
source_docs: [mega_plan.md Task 1-2, task-1-report.md]
---

# LastFm — Plan

## 1. Scope

Sync/scrobble service against the Last.fm API. Single concrete goal: stop mixing `throw` and `ErrorOr` return types in the same call chain.

## 2. Current state — corrects a false completion claim

`task-1-report.md` states *"Status: DONE, commit 88fdd1a."* That commit does not exist in this repository — `git cat-file -t 88fdd1a` fails against every ref. Direct source check confirms the work was never applied: `LastFmApiClient.cs` still throws `LastFmApiException` at lines 67 and 106, and `LastFmService.cs` still catches it in the retry loop.

This is not a resume-from-here plan. It's the original task, unstarted against real source, regardless of what the report says.

## 3. Findings

### F-1 — `ParseJsonResponse` and `ExecuteHttpRequestAsync` both declare `ErrorOr` and throw `[MEDIUM] [HIGH]`

Forces every caller to handle `if (result.IsError)` **and** `catch (LastFmApiException)` for the same failure class. `ExecuteHttpRequestAsync` throws on HTTP 429 specifically — a retryable condition, not an exceptional one.

## 4. CPM network

**Project duration: 4.0 h.**

| ID | Task | Dur | Deps | ES | EF | LS | LF | Float |
|---|---|---:|---|---:|---:|---:|---:|---:|
| L1 | Add `Errors.LastFm.RateLimited(TimeSpan)` / `Retryable(int, string)` | 0.5 | — | 0.0 | 0.5 | 0.0 | 0.5 | **0** |
| L2 | `ExecuteHttpRequestAsync` + `ParseJsonResponse`: throw → `ErrorOr` | 1.5 | L1 | 0.5 | 2.0 | 0.5 | 2.0 | **0** |
| L3 | `LastFmService` retry loop: `catch`-based → `IsError`-based | 1.0 | L2 | 2.0 | 3.0 | 2.0 | 3.0 | **0** |
| L4 | Delete `LastFmApiException` class (keep `LastFmErrorType` if `ClassifyError` still needs it) | 0.5 | L3 | 3.0 | 3.5 | 3.0 | 3.5 | **0** |
| L5 | Build gate; `rg "throw new LastFmApiException\|catch.*LastFmApiException"` → 0 hits | 0.5 | L4 | 3.5 | 4.0 | 3.5 | 4.0 | **0** |

Fully sequential, zero float — every task is critical.

## 5. Task detail

**L2 acceptance:** `ExecuteHttpRequestAsync` returns `Errors.LastFm.RateLimited(retryAfter)` on HTTP 429 instead of throwing; `ParseJsonResponse` returns a switch expression on `ClassifyError`, never a `throw`.

**L5 acceptance:** the grep is the actual gate, not the build — a clean build proves the code compiles, not that the mixing is gone.

## 6. Out of scope

`Microsoft.Extensions.Http.Resilience` as a retry replacement — Last.fm returns errors inside HTTP 200 bodies, which that library's status-code-based handlers don't see. This was correctly rejected in prior analysis; not reopened.
