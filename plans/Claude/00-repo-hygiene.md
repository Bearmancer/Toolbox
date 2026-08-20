---
concern: Repo-Hygiene
status: active — read this file first
ref: github.com/Bearmancer/Toolbox @ fe6e322d (master, "Pre-Pristine adding commit")
source_docs: [toolbox-flatline.md, "toolbox-flatline (2).md", progress.md, "progress (2).md", task-1-report.md]
---

# Repo Hygiene — Plan

## 1. Scope

This file exists because reconciling the plan corpus against real GitHub state surfaced something more urgent than any documentation cleanup: **two branches of finished work are not on GitHub and may not exist anywhere but one machine.**

## 2. Current state — verified directly against `git clone`, not inferred

`git ls-remote --heads origin` on the real repo returns exactly one ref: `master`. No `main`, no `pristine-port`, no `sacd-completion-v2`.

`toolbox-flatline (2).md` opens *"— COMPLETED (2026-08-18 snapshot)"* and describes squashing to a single `main`, deleting all other branches/worktrees, and pushing with the default branch switched. That did not fully happen, or was reverted: GitHub's default branch is still `master`, and HEAD is a commit literally titled **"Pre-Pristine adding commit"** — the repo is paused one commit before Pristine was meant to land.

Cross-checked two specific claims against the real history (`git log --all`, 93 commits, every ref):

| Claim | Source | Verified |
|---|---|---|
| Pristine (22-task build) complete, wired into `Program.cs` | `progress (2).md`, `4-cli-di.md` Task 22 | **Not on GitHub.** `find . -iname "*pristine*"` in the real checkout returns only `.omo/plans/pristine` — zero `.cs` files. `Program.cs` has no `CLI.Pristine` using, no `AddPristineServices()`. |
| LastFm ErrorOr fix, commit `88fdd1a` | `task-1-report.md`: *"Status: DONE, commit 88fdd1a"* | **Commit does not exist.** `git cat-file -t 88fdd1a` → `fatal: Not a valid object name`, checked with `--all`. `LastFmApiException` still throws at three sites in the real source. |
| Pristine hardening (12-todo plan), OciDeployer ErrorOr fix | `progress.md` (branch `pristine-port`, base `f84ebec`) | **Base commit `f84ebec` is also absent from GitHub history.** The entire branch this ledger describes was never pushed. |

What **is** confirmed present on GitHub `master`: the Audio/SACD mega-plan (M1–M4, commit `0451bf4`), YouTube seam fixes, Dashboard relocation, `DffHeaderReader` extraction, `LogPaths` deletion, `SacdProbeService`/`SacdProbeRunner` removal, and a later Audio fix (`5858a75`) that changed `--format` from rejecting 24-bit to genuinely supporting it. All of that matches what the plan corpus describes as done — it's specifically the **most recent** branch of work (Pristine + LastFm ErrorOr + Pristine hardening) that's missing.

## 3. Decision register

**D-1 (superseded):** an earlier session in this consolidation locked SACD output to 16-bit only. Real source now shows `--format` genuinely supports `24` (commit `5858a75`, description reads *"16 (default, CD-compatible) or 24 (hi-res)"*). Per the instruction to update plans to match local state when they conflict, **D-1 is dropped**. The Audio concern file documents current behavior, not the earlier lock.

**D-2:** YouTube and Dashboard content is excluded from this consolidated plan set at the *documentation* level — no forward-looking YouTube plan file exists after this pass. This is not a plan to delete `Services/Google/YouTube` from the repo. Dashboard is included in that exclusion, not carried as its own concern, because `DashboardDataBuilder`/`DashboardOrchestrator`/`DashboardSetup` all `using Services.Google.YouTube` directly and type on `PlaylistSnapshot`/`YouTubeVideo` — verified via grep, zero non-YouTube consumers exist. If actual code deletion is wanted, that's a separate, larger, and destructive undertaking that deserves its own explicit go-ahead rather than being bundled into a plan-corpus cleanup.

**D-3:** Firefox DevTools MCP references are dropped everywhere. Every instance in the corpus (`1-types.md` Task 10, `4-cli-di.md`'s post-wire check) is explicitly a one-time dev-time selector-verification step, never a product dependency.

## 4. Findings

### F-1 — Two branches of completed work are not recoverable from anything I have access to `[CRITICAL] [HIGH]`

If `pristine-port` (or whatever local branch/worktree held it) still exists on the machine that produced these plans, it needs to be pushed. If it doesn't — if it was deleted as part of the flatline "zero branches besides main" step without merging first — then the Pristine implementation, the LastFm ErrorOr fix, and the Pristine hardening work described throughout `plans.zip` **do not exist anywhere I can verify**, and the corresponding concern files in this consolidation (Pristine especially) are plans against a target that may need to be rebuilt from scratch rather than resumed.

This is not something I can resolve by reading further files. It needs a direct answer: does `pristine-port` still exist locally?

### F-2 — Duplicate-named files in the corpus, resolved by evidence, not by asking

No further input needed here — recorded for the audit trail:

| Pair | Resolution | Evidence |
|---|---|---|
| `toolbox-flatline.md` vs `(2)` | `(2)` is authoritative — it's the same document with a "COMPLETED" banner prepended | `(2)` opens with the completion status; original doesn't |
| `pristine-hardening.md` vs `(2)` | `(2)` is authoritative — SDD draft front-matter replaced by finished TL;DR + 12 numbered todos | Timestamp `(2)` is 7 min later; content structure is more complete |
| `mega_plan.md` vs `2026-08-19-erroror-railway-and-pristine-hardening.md` | Same plan, one-line diff (`.NET 9` vs `.NET 11`). `2026-08-19-...` is correct — verified `net11.0` in `Directory.Build.props` | Byte-diff + direct source check |
| `progress.md` vs `progress (2).md` | Different plans, not duplicates — `(2)` is the completed 22-task Pristine build ledger, plain `progress.md` is the newer (unpushed) ErrorOr-railway ledger | Header lines name different plan paths |
| `youtube-quota-logging.md` vs `(2)` | Moot — YouTube excluded from this consolidation regardless | — |

## 5. CPM network

**Project duration: 2.0 h.**

| ID | Task | Dur | Deps | ES | EF | LS | LF | Float |
|---|---|---:|---|---:|---:|---:|---:|---:|
| H1 | Confirm whether `pristine-port` still exists locally; if not, confirm `88fdd1a`'s parent commit is truly unrecoverable | 0.5 | — | 0.0 | 0.5 | 0.0 | 0.5 | **0** |
| H2 | Push recovered branch(es) to GitHub — or, if lost, formally re-scope Pristine/LastFm-ErrorOr as new work | 1.0 | H1 | 0.5 | 1.5 | 0.5 | 1.5 | **0** |
| H3 | Delete superseded plan files from `.omo/plans` (see §6 deletion list) | 1.0 | — | 0.0 | 1.0 | 0.0 | 1.0 | **0** |
| H4 | Update `AGENTS.md` hierarchy to match disk (post-M1–M4, post this consolidation) | 0.5 | H3 | 1.0 | 1.5 | 1.0 | 1.5 | **0** |
| H5 | Confirm single branch, single plan set, GitHub authoritative | 0.5 | H2,H4 | 1.5 | 2.0 | 1.5 | 2.0 | **0** |

Critical path: `H1 → H3 → H2 → H4 → H5` (H3 has no dependency so it can run in parallel with H1, but nothing after it can close until H1's answer is known).

## 6. Deletion list — safe to remove once this file set is accepted

`sacd-pipeline-rescue.md`, `sacdprobe-editorconfig.md`, `2026-08-09-sacd-death-loop-v2-design.md`, `2026-08-10-logging-audit.md`, `2026-08-10-logging-audit-spec.md`, `2026-08-10-process-runner-streaming.md`, `2026-08-12-sacd-consolidated.md`, `2026-08-14-audio-design-assessment.md`, `2026-08-18-toolbox-mega-plan.md` (executed), `mega_plan.md` (duplicate), `dead-code-catalog.md`, `error-taxonomy.md`, `overengineering-verdict.md`, `god-audit-spec.md`, `audio-cli-spec.md`, `telemetry-spec.md`, `youtube-*.md` (all), `2026-08-04-youtube-duplicate-playlist-merge.md`, `toolbox-flatline.md` (superseded by `(2)`, then both archived once read), `agent.md`, `task-1-brief.md`, `task-2-brief.md`, `task-1-report.md`, `task-1-report (2).md`, `session_analysis.md`, `p5_p6_execution_plan.md`, `G305_Engineer_Spec_Sheet.md` (unrelated).

**Keep, do not delete:** `sacd-guide.md` (authority, not present in this corpus but referenced — confirm it still exists in `docs/`), `erroror_migration_assessment.md`, `ponytail_audit_verified.md` (both cross-checked and still useful as a "don't re-propose this" record), `Pristine Script.md`, `content.md`, `removals.json`/`removals_analysis.*`, `youtube_search_results.json` — keep as raw evidence, not as active plans.

## 7. Out of scope

Actually deleting YouTube/Dashboard source code. Rewriting git history. Anything involving the `toolbox-flatline` squash/rename mechanics again — that already ran once; running it twice is how you lose a second branch.
