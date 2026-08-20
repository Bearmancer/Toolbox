# toolbox-flatline - Work Plan

## TL;DR (For humans)

**What you'll get:** Your Toolbox repo flattened to one clean state: the verified SACD audio fix properly installed and proven on Disc 10, all your scattered uncommitted work safely committed in logical groups, all the agent clutter (.omo plans, .superpowers reports, scratch files) gone except one master plan, and git reduced to a single branch called `main` with the recent messy commit history tidied into topic groups - pushed to GitHub with the default branch switched over.

**Why this approach:** Rescue-before-delete (nothing is removed until its valuable content is copied somewhere safe), prove the audio fix works before cleaning up around it, and squash history without reordering commits (reordering causes conflicts; grouping only neighbors cannot). The one step you must do yourself: run the Disc 10 conversion in your own terminal, because Saracon is an old Windows GUI program that refuses to run from automated sessions.

**What it will NOT do:** Never rewrites history that's already on GitHub, never force-pushes, never deletes your music/state file contents, never touches your agent runtime folders outside the repo, and never skips the Disc 10 proof - the plan stops and waits there.

**Effort:** Medium
**Risk:** Medium - git history rewrite + branch rename against a live GitHub remote; every destructive step is rescue-first and reflog-recoverable
**Decisions to sanity-check:** squash groups ADJACENT same-topic commits only (no reorder, ~5-6 commits result); OCI server-tools folder archived to `Dev\Old\toolbox-oci-sdd-archive\` instead of deleted; probe journal + v2 spec kept under `docs/`; stash (old .omo state) dropped; unclassified source drift committed as one build-gated sync commit rather than reverted.

Your next move: approve after the high-accuracy review result, then run via `/start-work`. Full execution detail follows below.

---

> TL;DR (machine): Medium effort, Medium risk. Land merged SACD audio fix, commit all working state by domain, Disc-10 proof, prune .omo/.superpowers to one plan, delete all worktrees/branches, squash 15 unpushed commits by adjacent topic, rename master->main, push + GitHub default-branch switch.

## Scope
### Must have
- Merged `SaraconService.cs` + `DffMetadataStripper.cs` (from `C:\Users\Lance\Desktop\Claude\`) on mainline with B9/B10 micro-fixes; `tools/SacdProbe` + `Toolbox.slnx` entry committed.
- Disc 10 converts clean (user-run interactive Saracon step, agent-verified evidence).
- ALL uncommitted working state committed: remaining src drift (build-gated) + 298 state files in 3 domain commits.
- Scratch deleted: `SACD errors.md`, `youtube-sync-log.md`, `.athena-state.json`.
- `.omo` flatlined to ONLY: `plans/toolbox-flatline.md`, `drafts/toolbox-flatline.md`, `evidence/**`. `.superpowers` deleted entirely AFTER archiving `sdd/oci-arr-exhaustive-repair` (minus `.venv`) to `C:\Users\Lance\Dev\Old\toolbox-oci-sdd-archive\` and rescuing `sacd-probe-journal.md` + v2 spec into `docs/superpowers/`.
- UTF-8 root-cause docs corrected with banner (not deleted).
- Zero worktrees besides the main tree (removes all 3 others: 2 live + 1 ghost admin); zero branches besides `main` (deletes all 4 others); stash dropped; nested `Toolbox-sacd-repro/` dir removed; 2 ghost admin dirs pruned.
- 15 unpushed commits squashed into adjacent-topic groups (NO reordering); new commits replayed on top.
- `master` renamed `main`, pushed, GitHub default branch switched; `origin/master` deleted only after switch succeeds.
- `dotnet build` clean at every gate.

### Must NOT have (guardrails, anti-slop, scope boundaries)
- NO force-push; NO rewrite of the 11 already-pushed commits.
- NO touching `C:\Users\Lance\.omo` (agent runtime home) or `C:\Users\Lance\Dev\.omo`.
- NO deleting/modifying existing `docs/` files except the correction banner and the two rescued files.
- NO deleting or editing `state/` file CONTENT - commit only. NO touching media/ISO files.
- NO changes to aws-translate/reader feature CODE (only their `.omo` plan files are pruned).
- NO new features, NO refactors beyond B9/B10, NO test NuGet packages (repo rule), NO `#pragma warning disable`.
- NO deleting `sacd-deathloop-repro` BEFORE todo 2 rescue completes and is verified.
- NO `git checkout`/`reset --hard` on uncommitted working state (priority: working state survives).
- NO skipping the Disc 10 step; plan HALTS there until user reports the run.

## Verification strategy
> Zero human intervention - all verification is agent-executed, EXCEPT the single Disc-10 conversion run (Saracon is a 2010 wxWidgets GUI app that fails outside an attached interactive desktop - evidence: spec §2.3 registry/OLE/wxIdleWakeUpModule failures; that step has exact user commands + agent-verified evidence).
- Test decision: none (repo rule: no test frameworks) + agent-executed QA per todo (git assertions, build gates, file/hash checks, log sequence verification).
- Evidence: `.omo/evidence/task-<N>-toolbox-flatline.<ext>` (todo 11 keeps `.omo/evidence/**` alive through the prune).
- Every destructive step is preceded by a rescue/verify step and followed by an assertion; git reflog is the rollback for all history ops.

## Execution strategy
### Parallel execution waves
> Git history ops are inherently sequential; waves group by phase, not by concurrency. Wave 1 todos 1-2 sequential (2 needs 1's output). Wave 4 todos 7-9 sequential (same index). Everything else per dependency matrix.

- Wave 1: Rescue + baseline (todos 1-2)
- Wave 2: Audio fix + build gate (todos 3-4)
- Wave 3: Disc 10 proof (todo 5) - HALT POINT, user-run
- Wave 4: Working-state + state commits (todos 6-9)
- Wave 5: Docs + prune (todos 10-11)
- Wave 6: Topology + squash + rename/push (todos 12-14)
- Wave 7: Final verification (F1-F4, parallel)

### Subagent-driven execution model
> Each todo is self-contained: exhaustive References, agent-executable Acceptance criteria, happy + failure QA with evidence paths, and a Commit line. The executor delegates each todo to a fresh Sisyphus-Junior subagent via `/start-work` — no inter-todo judgment calls, no shared session state. The orchestrator verifies each subagent's output independently before unblocking dependents.
- Delegation: one todo = one subagent call; the subagent gets the full todo text (References through Commit) as its prompt.
- Verification gate: after each subagent completes, the orchestrator independently re-checks the acceptance criteria (runs the exact assertion commands itself) before marking the todo done and unblocking dependents. Subagent output is a CLAIM until verified.
- HALT propagation: if a subagent reports failure or its acceptance criteria don't pass independent verification, the orchestrator HALTS the wave and reports to the user — no automatic retry, no skipping ahead.
- Parallel where the dependency matrix allows: todos 7-9 (state commits) and todo 10 (docs) can dispatch as parallel subagents once their blockers complete.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 | - | 2,3,12 | - |
| 2 | 1 | 11,12 | - |
| 3 | 1 | 4 | - |
| 4 | 3 | 5,6 | - |
| 5 | 4 | 6 | - |
| 6 | 5 | 13 | 7,8,9 |
| 7 | 6 | 13 | 8,9 |
| 8 | 6 | 13 | 7,9 |
| 9 | 6 | 13 | 7,8 |
| 10 | 2 | 11 | 6-9 |
| 11 | 2,10 | 12 | - |
| 12 | 2,11 | 13 | - |
| 13 | 6,7,8,9,12 | 14 | - |
| 14 | 13 | F1-F4 | - |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [ ] 1. Baseline inventory + verification snapshot
  What to do / Must NOT do: FIRST create the evidence dir: `New-Item -ItemType Directory -Force .omo/evidence`. In `C:\Users\Lance\Dev\Toolbox` capture: (a) full `git status --porcelain` (all entries, no truncation) to evidence, plus a tracked/untracked classification note (Metis-verified reality: `.omo/goal/**` + `.omo/ulw-loop/**` are TRACKED deletions; `.omo/Plan.md`, `.omo/plans/**` are UNTRACKED; `.omo/run-continuation/**` is gitignored; `state/youtube/manifest.json` is TRACKED+modified; `.superpowers/audit/sacd-probe-journal.md` is TRACKED+modified; `.superpowers/sdd/**` is UNTRACKED; `SACD.red.md` is a TRACKED deletion; `SACD errors.md`/`youtube-sync-log.md`/`.athena-state.json` are UNTRACKED) - re-derive this classification from the actual status output, do not trust this list blindly; (b) `git log --oneline origin/master..master` (the exact 15 unpushed commits, oldest->youngest via `--reverse`) to evidence; (c) SHA-256 of `C:\Users\Lance\Desktop\Claude\SaraconService.cs` and `DffMetadataStripper.cs` (Get-FileHash); (d) compare `tools/SacdProbe/*` (5 files) against repro version: `git diff sacd-deathloop-repro -- tools/SacdProbe` from the main worktree - record identical/divergent per file; (e) confirm v2 spec exists in nested repro worktree at `Toolbox-sacd-repro/docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md`; (f) confirm `.superpowers/audit/sacd-probe-journal.md` exists in main tree; (g) record `git stash list`. MUST NOT modify anything else.
  Parallelization: Wave 1 | Blocked by: none | Blocks: 2,3,12
  References (executor has NO interview context - be exhaustive): draft findings section in `.omo/drafts/toolbox-flatline.md`; `C:\Users\Lance\Desktop\Claude\SACD-decision-battery-answered.md` (verification notes); repo root `C:\Users\Lance\Dev\Toolbox`; nested repro worktree `C:\Users\Lance\Dev\Toolbox\Toolbox-sacd-repro`
  Acceptance criteria (agent-executable): evidence file contains all 7 captures; `git log --oneline origin/master..master | Measure-Object -Line` == 15; both hash lines present; SacdProbe diff verdict recorded per file; v2 spec + journal existence = true.
  QA scenarios (name the exact tool + invocation): happy - all captures written, `Get-Content .omo/evidence/task-1-toolbox-flatline.txt | Select-String 'UNPUSHED_COUNT=15'` matches; failure - any capture missing or count != 15 -> HALT and report divergence from plan assumptions. Evidence `.omo/evidence/task-1-toolbox-flatline.txt`
  Commit: N | -

- [ ] 2. Rescue artifacts before any deletion
  What to do / Must NOT do: (a) copy `Toolbox-sacd-repro/docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md` -> `docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md` (dir exists, empty); (b) if todo 1d found SacdProbe divergence: overwrite main-tree `tools/SacdProbe/<file>` with the repro branch version (`git show sacd-deathloop-repro:tools/SacdProbe/<file>`), repro is source of truth; if identical, do nothing; (c) archive OCI SDD: create `C:\Users\Lance\Dev\Old\toolbox-oci-sdd-archive\`, then `robocopy .superpowers\sdd\oci-arr-exhaustive-repair C:\Users\Lance\Dev\Old\toolbox-oci-sdd-archive /E /XD .venv` (exclude regenerable .venv), verify file counts match (source minus .venv); (d) verify journal still at `.superpowers/audit/sacd-probe-journal.md`. MUST NOT delete anything yet; MUST NOT archive .venv.
  Parallelization: Wave 1 | Blocked by: 1 | Blocks: 11,12
  References: answered battery B6 (SacdProbe keep, repro=truth for slnx coupling), B7 (journal+spec rescue), user answer Q3 (archive-then-delete); `.superpowers/sdd/oci-arr-exhaustive-repair/` (python tools + evidence, deployed-to-OCI source)
  Acceptance criteria (agent-executable): `Test-Path docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md` true; archive dir file count == source count minus .venv files (compare `(Get-ChildItem -Recurse -File -Exclude ...)` counts); `git diff sacd-deathloop-repro -- tools/SacdProbe` empty after (b).
  QA scenarios: happy - all three rescues verified by the assertions above; failure - any copy/verify fails -> HALT before todo 11/12 (deletions stay blocked). Evidence `.omo/evidence/task-2-toolbox-flatline.txt`
  Commit: N | -

- [ ] 3. Apply audio fix drop-ins + B9/B10 micro-fixes
  What to do / Must NOT do: (a) copy `C:\Users\Lance\Desktop\Claude\SaraconService.cs` -> `src/Services/Audio/SaraconService.cs` and `C:\Users\Lance\Desktop\Claude\DffMetadataStripper.cs` -> `src/Services/Audio/DffMetadataStripper.cs`, then strip the leading `// Merged version` comment blocks from both files (repo rule 9: zero inline/explanatory comments) - change NOTHING else in either file; (b) B9: in `src/CLI/Audio/SacdConvertCommand.cs` remove the `--debug` and `--verbose` `CommandOption` properties from `Settings` (Program.cs blanket-strips them; keep the Program.cs mechanism, delete the dead options); remove any code reading those properties; (c) B10: in `src/CLI/Azure/SpeechTtsCommand.cs` add a `Validate()` override on Settings that returns `ValidationResult.Error` unless EXACTLY ONE of `--text` / `--file` is provided (mutual exclusivity + presence). MUST NOT alter signatures of `ConvertDsdToPcmAsync`/`ConvertDsdToFlacAsync` (DsdConvertService call sites depend on the 7-param shape incl. `onOutputLine`); MUST NOT add comments beyond existing XML docs.
  Parallelization: Wave 2 | Blocked by: 1 | Blocks: 4
  References: `C:\Users\Lance\Desktop\Claude\SaraconService.cs` (header comment documents the merge rationale), `C:\Users\Lance\Desktop\Claude\DffMetadataStripper.cs`; answered battery B1/B4/B9/B10; `src/Services/Audio/DsdConvertService.cs` call sites (worktree dump in `C:\Users\Lance\Desktop\Claude\worktree-youtube-duplicate-merge.md` lines 283-564); `src/App/Program.cs` (blanket --verbose/--debug strip); repo AGENTS.md rules 1,9
  Acceptance criteria (agent-executable): `Select-String -Path src/Services/Audio/SaraconService.cs -Pattern 'Merged version'` returns nothing; `Select-String -Path src/CLI/Audio/SacdConvertCommand.cs -Pattern '--debug|--verbose'` returns nothing; `Select-String -Path src/CLI/Azure/SpeechTtsCommand.cs -Pattern 'override ValidationResult Validate'` matches once; both public Convert methods keep 7 params (`Select-String 'onOutputLine' src/Services/Audio/SaraconService.cs` >= 2 matches).
  QA scenarios: happy - all 4 assertions pass; failure - any assertion fails -> fix in place before todo 4 build gate. Evidence `.omo/evidence/task-3-toolbox-flatline.txt`
  Commit: N | -

- [ ] 4. Build gate + audio fix commit
  What to do / Must NOT do: (a) `dotnet build` at repo root - MUST be clean (0 errors; repo treats style warnings as errors); (b) from this EXACT list stage every path that shows a pending entry in `git status --porcelain` (some may already be clean - stage only what status shows): `src/Services/Audio/SaraconService.cs`, `src/Services/Audio/DffMetadataStripper.cs`, `src/App/Program.cs` (pre-existing audio-only DI skip + --verbose/--debug strip — battery B9/§3.4, part of audio fix lineage), `src/CLI/Audio/SacdConvertCommand.cs`, `src/CLI/Azure/AzureCommandModule.cs` (pre-existing module alignment — battery §3.6, part of audio fix), `src/CLI/Azure/SpeechTtsCommand.cs` (untracked-new), `src/Core/ServiceName.cs`, `Toolbox.slnx`, `tools/SacdProbe/` (all 5 files, untracked); (c) commit `fix(audio): no-retry Saracon conversion, correct DFF chunk offset, skip OAuth for audio-only runs`. MUST NOT stage state/, docs/, scratch, or unrelated src drift here.
  Parallelization: Wave 2 | Blocked by: 3 | Blocks: 5,6
  References: answered battery C3 step 7 (exact file list); AGENTS.md rule 1 (build-verify every edit); `Toolbox.slnx` already references `tools\SacdProbe\SacdProbe.csproj` (battery B6 warning: never commit slnx without the project source - both now staged together)
  Acceptance criteria (agent-executable): `dotnet build` exit code 0 with `0 Error`; `git log -1 --pretty=%s` == the commit message above; `git status --porcelain -- tools/SacdProbe src/Services/Audio/SaraconService.cs src/Services/Audio/DffMetadataStripper.cs src/App/Program.cs Toolbox.slnx` empty.
  QA scenarios: happy - build clean, commit created, staged set exactly matches; failure - build error -> fix per error (only files from todo 3 may be touched), rebuild, then commit; if unfixable in those files -> HALT with full build log. Evidence `.omo/evidence/task-4-toolbox-flatline.txt`
  Commit: Y | fix(audio): no-retry Saracon conversion, correct DFF chunk offset, skip OAuth for audio-only runs

- [ ] 5. Disc 10 conversion proof (HALT POINT - user runs Saracon step)
  What to do / Must NOT do: (a) Agent precondition check: verify saracon/sox/sacd_extract binaries resolve (`Get-Command` or PATH check matching `ProcessRunner.IsOnPath` logic) and record current session interactivity (`query session` / `(Get-Process -Id $PID).SessionId`); (b) present the user EXACTLY this block to run in their INTERACTIVE terminal: `dotnet run --project C:\Users\Lance\Dev\Toolbox\src\App -- --verbose audio sacd-convert "<path-to-Disc-10.iso>"` plus cleanup-first if prior death-loop residue: `Get-Process saracon -ErrorAction SilentlyContinue | Stop-Process -Force; Remove-Item "<disc10-dir>\Disc 10*.wav","<disc10-dir>\Disc 10*_clean.dff" -ErrorAction SilentlyContinue`; (c) HALT execution (report "waiting for user Disc-10 run") until the user confirms the run finished; (d) then agent verifies from `logs/audio.jsonl`: sequence `Saracon.Id3Detected` -> `DffMetadataStripper.Complete` -> `ProcessRunner.Complete exitCode=0` -> `Saracon.ConvertComplete`, ZERO retry entries (`Select-String 'retry' -CaseSensitive:$false` count 0 in Saracon entries), output file exists and size >= 50% of expected (expected ~500MB+ for the 3GB DFF; assert `Length -gt 250MB`); (e) record the verified log excerpt + file size to evidence. MUST NOT run the conversion from the agent session itself (Saracon GUI dies without attached desktop - spec §2.3); MUST NOT proceed past this todo on verification failure - HALT with the failing log lines.
  Parallelization: Wave 3 | Blocked by: 4 | Blocks: 6
  References: prompt.md §2.3 (non-interactive precondition), §5 (operational sequence, validated by Oracle); answered battery C3 step 8; `logs/audio.jsonl` (per-service JSONL, AGENTS.md)
  Acceptance criteria (agent-executable): evidence contains the 4 log events in order; retry-count == 0; `(Get-Item <output-wav>).Length -gt 250MB` true; user confirmation recorded.
  QA scenarios: happy - all 4 assertions pass after user run; failure - missing event / retry entries / undersized output -> HALT, attach last 50 log lines, do not continue to todo 6. Evidence `.omo/evidence/task-5-toolbox-flatline.txt`
  Commit: N | -

- [ ] 6. Commit remaining src working-state drift (build-gated)
  What to do / Must NOT do: (a) stage ALL remaining modified/deleted files under `src/` plus modified `Directory.Packages.props` if present in status (this is accumulated working state - battery priority #1: it survives); (b) `dotnet build` clean; (c) commit `chore: sync working-state source changes`; (d) ON BUILD FAILURE: `git reset HEAD~1` (keep files in working tree), record the exact build errors, HALT with report - do NOT revert/checkout user files. MUST NOT stage state/, docs/, scratch here.
  Parallelization: Wave 4 | Blocked by: 5 | Blocks: 13
  References: git status entries from todo 1; battery priority order (working state survives); AGENTS.md rule 1
  Acceptance criteria (agent-executable): after commit, `git status --porcelain -- src/` empty; `dotnet build` exit 0; `git log -1 --pretty=%s` == message.
  QA scenarios: happy - staged, built, committed, src/ clean; failure - build fails -> reset commit, HALT with errors (working tree intact). Evidence `.omo/evidence/task-6-toolbox-flatline.txt`
  Commit: Y | chore: sync working-state source changes

- [ ] 7. State commit - routine youtube churn
  What to do / Must NOT do: stage `state/youtube/processed/*` + `state/youtube/raw/*` + `state/youtube/manifest.json` (tracked+modified - Metis finding: it belongs to routine churn and MUST be in one of the three state commits; all modified/new/deleted entries under those paths only); commit `chore(state): youtube sync state update (processed+raw)`. MUST NOT include `deleted/` or `merge-manifests/` (todo 8).
  Parallelization: Wave 4 | Blocked by: 6 | Blocks: 13
  References: answered battery A6 + C2 (split by domain; routine churn separate from one-way decisions); state counts: processed 145, raw 145
  Acceptance criteria (agent-executable): `git status --porcelain -- state/youtube/processed state/youtube/raw` empty after commit; commit subject matches.
  QA scenarios: happy - clean path assertion; failure - staging error -> `git reset`, re-stage with explicit pathspecs, retry once, else HALT. Evidence `.omo/evidence/task-7-toolbox-flatline.txt`
  Commit: Y | chore(state): youtube sync state update (processed+raw)

- [ ] 8. State commit - irreversible subset (deleted + merge-manifests), diff-reviewed
  What to do / Must NOT do: (a) `git diff -- state/youtube/deleted state/youtube/merge-manifests` AND `git status --porcelain -- state/youtube/deleted state/youtube/merge-manifests` - write full output to evidence and inspect every entry (these are one-way consolidation decisions - battery A6 warning: renamed records like `Gunter Wand` vs `Günter Wand` indicate hand edits); (b) stage both dirs; commit `chore(state): youtube deletions + merge manifests (reviewed)`. MUST NOT skip the diff capture; if diff shows JSON that fails to parse (`jaq` each file), HALT and report the corrupt file instead of committing.
  Parallelization: Wave 4 | Blocked by: 6 | Blocks: 13
  References: answered battery A6 ⚠ + C2; state counts: deleted 3, merge-manifests 1; global rule: jaq for JSONL/JSON
  Acceptance criteria (agent-executable): evidence contains full diff + per-file `jaq` parse OK lines; both paths clean in status after commit; commit subject matches.
  QA scenarios: happy - diff captured, all JSON parses, committed; failure - unparseable JSON or diff capture failed -> HALT with file name. Evidence `.omo/evidence/task-8-toolbox-flatline.txt`
  Commit: Y | chore(state): youtube deletions + merge manifests (reviewed)

- [ ] 9. State commit - dashboard + lastfm
  What to do / Must NOT do: stage `state/dashboard/*` + `state/lastfm/*`; commit `chore(state): dashboard + lastfm state update`. MUST NOT include youtube paths.
  Parallelization: Wave 4 | Blocked by: 6 | Blocks: 13
  References: answered battery C2 third split; counts: dashboard 2, lastfm 1
  Acceptance criteria (agent-executable): `git status --porcelain -- state/dashboard state/lastfm` empty after commit; commit subject matches.
  QA scenarios: happy - clean assertion; failure - staging error -> reset, retry once, else HALT. Evidence `.omo/evidence/task-9-toolbox-flatline.txt`
  Commit: Y | chore(state): dashboard + lastfm state update

- [ ] 10. Docs correction + journal relocation
  What to do / Must NOT do: (a) identify every doc asserting the rejected UTF-8 root cause: `Select-String -Path docs/superpowers/plans/*.md,docs/plans/*.md,docs/athena/specs/*.md -Pattern 'UTF-8|65001|codepage' -List`; (b) at the TOP of each matching file insert exactly this banner (then a blank line): `> **CORRECTION (2026-08-11):** The UTF-8/ACP root cause claimed here was REJECTED by probe run #4 (all-PASS with ACP=65001). Verified root cause: ID3 chunks in DFF + Saracon retry self-restart loop, compounded by non-interactive session GUI failure. Evidence: docs/superpowers/audits/sacd-probe-journal.md. Do not restate the UTF-8 hypothesis as settled.`; (c) `Move-Item .superpowers/audit/sacd-probe-journal.md docs/superpowers/audits/sacd-probe-journal.md`; (d) leave all other docs bytes untouched. MUST NOT delete any doc (answered B5: correct with note, never delete).
  Parallelization: Wave 5 | Blocked by: 2 | Blocks: 11
  References: answered battery B5; journal run #4 (prompt.md §2.1-2.2); docs inventory: docs/superpowers/plans/{2026-08-08-sacd-death-loop-repro.md, 2026-08-09-sacd-saracon-death-loop-fix.md, 2026-08-04-youtube-duplicate-playlist-merge.md}, docs/plans/2026-08-10-process-runner-streaming.md, docs/athena/specs/2026-08-10-process-runner-streaming-design.md
  Acceptance criteria (agent-executable): every file that matched in (a) now matches `Select-String 'CORRECTION \(2026-08-11\)'`; `Test-Path docs/superpowers/audits/sacd-probe-journal.md` true; `Test-Path .superpowers/audit/sacd-probe-journal.md` false; non-matching docs byte-identical (hash before/after the non-matching set).
  QA scenarios: happy - banner in all matches, journal moved, untouched set hash-identical; failure - zero files matched the UTF-8 pattern -> HALT and report (assumption wrong), do not guess. Evidence `.omo/evidence/task-10-toolbox-flatline.txt`
  Commit: Y | docs(audio): correct rejected UTF-8 root cause; relocate probe journal

- [ ] 11. Flatline .omo + .superpowers + scratch
  What to do / Must NOT do: (a) delete root scratch: `SACD errors.md`, `youtube-sync-log.md`, `.athena-state.json` (all untracked - deletion produces NO git entry); (b) delete `.superpowers/` entirely (oci SDD archived in todo 2, journal rescued in todo 10, v2 spec rescued in todo 2 - verify all three receipts before removal; only the journal is TRACKED, its deletion stages; sdd/** is untracked - vanishes silently by design); (c) delete everything in `.omo/` EXCEPT `plans/toolbox-flatline.md`, `drafts/toolbox-flatline.md`, and `evidence/**` (this deletes: `Plan.md`, `plans/GIT-CLEANUP-DECISION-BATTERY.md`, `plans/SACD-FIX-FINAL-REPORT.md`, `plans/oracle-sacd-verification.md`, `plans/aws-translate/**`, `plans/reader/**` - all UNTRACKED, vanish silently - and the TRACKED deletions `.omo/goal/**` + `.omo/ulw-loop/**` which MUST be staged; `run-continuation/**` is gitignored, vanishes silently); (d) update `AGENTS.md` line `**Generated:** ... | **Branch:** master` -> replace `master` with `main` on that line only;   (e) staging (Metis-corrected reality): `git add -A .omo .superpowers AGENTS.md .gitignore` (Metis R3 note: `.gitignore` included only if `git status` shows it modified — verify before staging; if clean, omit from pathspec) plus stage the tracked deletion `SACD.red.md` if present in status, plus CATCH-ALL: run `git status --porcelain` and stage ANY remaining tracked entry (line NOT starting with `??`) whose path is outside `src/` and `state/` (those closed in todos 4/6/7/8/9) into this same commit - list every such catch-all path in evidence; (f) commit `chore: flatline agent artifacts, delete scratch, docs hygiene`. MUST NOT delete `.omo/evidence/**`, the plan, or the draft; MUST NOT touch `C:\Users\Lance\.omo` or `C:\Users\Lance\Dev\.omo`; evidence files stay UNTRACKED (never stage `.omo/evidence`).
  Parallelization: Wave 5 | Blocked by: 2,10 | Blocks: 12
  References: user order (flatline ALL in .omo/.superpowers); answered battery B7/B8; todo 2 rescue receipts; `.omo` inventory (38 files), `.superpowers` inventory (~100 files incl. sdd/youtube-duplicate-playlist-merge reports = DROP per B7)
  Acceptance criteria (agent-executable): `Test-Path .superpowers` false; `(Get-ChildItem .omo -Recurse -File | Where-Object FullName -NotMatch 'plans.toolbox-flatline|drafts.toolbox-flatline|evidence').Count` == 0; `Test-Path 'SACD errors.md'` false; `Test-Path youtube-sync-log.md` false; `Test-Path .athena-state.json` false; `Select-String 'Branch:\*\* main' AGENTS.md` matches.
  QA scenarios: happy - all assertions pass, commit created; failure - any rescue receipt from todo 2/10 missing -> HALT before deletion. Evidence `.omo/evidence/task-11-toolbox-flatline.txt`
  Commit: Y | chore: flatline agent artifacts, delete scratch, docs hygiene

- [ ] 12. Remove worktrees, branches, stash (post-rescue)
  What to do / Must NOT do: (a) dirty pre-check FIRST (Metis): `git -C .worktrees/youtube-duplicate-playlist-merge status --porcelain` -> record full output to evidence (expected: deletions/mods reflecting its OLD fully-merged state; valueless because branch has 0 unique commits), then `git worktree remove --force .worktrees/youtube-duplicate-playlist-merge`; (b) delete nested repro dir: `Remove-Item -Recurse -Force Toolbox-sacd-repro` ONLY after re-verifying todo 2 receipts (v2 spec + SacdProbe + archive all present) - its unique content is the repro branch history, preserved in `.git` until (d); the filesystem delete + `git worktree prune` in (c) is the sanctioned two-step for this already-stale admin record; (c) `git worktree prune` (clears oci-arr-repair ghost + stale Toolbox-sacd-repro admin record); (d) `git branch -d feat/youtube-duplicate-merge feature/process-runner-streaming oci-arr-exhaustive-repair` (all 0 unique commits - `-d` must succeed WITHOUT `-D`; if any refuses, HALT - that means unmerged work appeared); then `git branch -D sacd-deathloop-repro` (rescue complete, fixes committed in todo 4); (e) `git stash drop stash@{0}` (Metis-verified: stash holds only stale `.omo/goal`/`.omo/ulw-loop` modifications - files this plan deletes; obsolete by construction); (f) verify `git worktree list` shows exactly 1 line and `git branch` shows exactly `* master`. Scope = 3 non-main worktrees (2 live + 1 ghost) + 4 branches. MUST NOT use `-D` on the three merged branches; MUST NOT run before todo 11.
  Parallelization: Wave 6 | Blocked by: 2,11 | Blocks: 13
  References: git truth (branch -vv: 3 branches 0-unique, repro 17-unique but rescued); answered battery C1/C3 step 2; stash content = "pre-rebase: .omo state files"
  Acceptance criteria (agent-executable): `(git worktree list | Measure-Object -Line).Lines` == 1; `(git branch | Measure-Object -Line).Lines` == 1 and matches master; `(git stash list | Measure-Object -Line).Lines` == 0; `Test-Path Toolbox-sacd-repro` false; `Test-Path .worktrees` false (or empty).
  QA scenarios: happy - all counts exact; failure - `-d` refusal on a merged branch -> HALT, run `git log master..<branch>` and report (assumption broken). Evidence `.omo/evidence/task-12-toolbox-flatline.txt`
  Commit: N | -

- [ ] 13. Squash the 15 unpushed commits by adjacent topic (two-pass rebase, NO reordering)
  > NOTE (Momus fix): the "What to do" below was originally a single 3306-char line. If your Read tool truncates at 2000 chars, read the full content with: `Get-Content .omo/plans/toolbox-flatline.md | Select-Object -Skip 180 -First 1` or break it into sub-lines (already done below).
  What to do / Must NOT do:
  (a) record safety snapshot: `git fetch origin` FIRST (Metis R3: ensure origin/master is current before rebase), then `git tag backup/pre-flatline-squash` + `git rev-parse HEAD` + `git log --reverse --pretty=%s origin/master..HEAD` (full pre-rebase subject list) to evidence;
  (b) list `git log --reverse --pretty='%h %s' origin/master..HEAD`; the bottom 15 (oldest, exactly the set from todo 1b) are squash candidates; commits above them (todos 4,6,7,8,9,10,11 = up to 7) replay untouched;
  (c) classify each of the 15 by CASE-INSENSITIVE subject regex (Metis-corrected: the real subjects include 'feat: add streaming and inactivity timeout to ProcessRunner', 'feat: bubble up onOutputLine in SaraconService', 'feat: stream saracon output to console and log file' which the old hyphenated lowercase regex missed) - AUDIO: `audio|saracon|sacd|dsd|processrunner|process-runner|stream|onoutputline|completion|logging`; YT: `google|youtube|playlist|sort|oauth`; DOCS: subject starts with `docs`; anything matching none, or matching both AUDIO and YT (e.g. fcbbb12 'fix(logging)... across all services'), classifies AUDIO;
  (d) MECHANISM (Windows-safe, Metis block resolved): write three files under `.omo/evidence/`: `rebase-todo-pass1` (the exact desired todo: within the bottom 15, each maximal ADJACENT run of same class = `pick` first + `fixup` rest, preserving original order entirely - NO reordering, zero conflict risk; all top commits `pick`), `rebase-todo-pass2` (surviving bottom run-heads = `reword`, all top commits `pick` verbatim), and for each reword an `N-message.txt`;
  create wrapper `seq-editor.cmd` containing `@copy /y "<prepared-todo>" "%~1" >nul`; for reword, use ONE-REBASE-PER-RUN-HEAD (Metis R3: committed mechanism — simpler, deterministic, no counter file): for each run-head, create `seq-editor-pass2-<run>.cmd` (copies a prepared todo that marks only THAT run-head as `reword`, all else `pick`) + a fixed `msg-<run>.cmd` (copies a single prepared message over `%~1`); run `$env:GIT_SEQUENCE_EDITOR='<abs>\seq-editor-pass2-<run>.cmd'; $env:GIT_EDITOR='<abs>\msg-<run>.cmd'; git rebase -i origin/master` once per run-head;
  then run pass 1 as `$env:GIT_SEQUENCE_EDITOR='<abs path>\seq-editor.cmd'; git rebase -i origin/master` (git invokes `<editor> <todo-file>`; a `.cmd` path works on Windows), and pass 2 per run-head: `$env:GIT_SEQUENCE_EDITOR='<abs>\seq-editor-pass2-<run>.cmd'; $env:GIT_EDITOR='<abs>\msg-editor-<run>.cmd'; git rebase -i origin/master`; prepared messages (bottom-to-top run order): AUDIO run(s) -> `feat(audio): Saracon pipeline hardening - streaming, timeouts, completion detection, service-wide logging`; DOCS run -> `docs(audio): SACD death-loop repro plans/specs (UTF-8 hypothesis - superseded, see correction banner)`; YT run(s) -> `feat(youtube): duplicate consolidation, non-Latin sort, quota batching, OAuth timeout`; single-commit runs KEEP their original message (no reword);
  (e) verify: `git log --oneline origin/master..HEAD` shows squashed bottom + 7 replayed tops with subjects identical to the (a) snapshot; `git diff backup/pre-flatline-squash HEAD` EMPTY - valid proof because the tag anchors the pre-rebase tree and a rebase that only fixups/rewords preserves the final tree;
  (f) on ANY conflict or non-empty tree diff: `git rebase --abort` (if mid-rebase) then `git reset --hard backup/pre-flatline-squash`, HALT with report (tag stays for later retry). MUST NOT reorder commits; MUST NOT touch the 11 pushed commits below origin/master; MUST NOT proceed with non-empty tree diff; MUST NOT delete the backup tag here (todo 14e owns that).
  Parallelization: Wave 6 | Blocked by: 6,7,8,9,12 | Blocks: 14
  References: todo 1b unpushed list; battery §0 master commit table (topics per hash); answered battery C3; `git rebase` GIT_SEQUENCE_EDITOR/GIT_EDITOR scripting (standard git)
  Acceptance criteria (agent-executable): `git diff backup/pre-flatline-squash HEAD --stat` output empty; no adjacent same-class pairs remain in bottom section (`git log --reverse --pretty=%s origin/master..HEAD~7` has no two consecutive subjects both matching AUDIO or both YT); top 7 subjects identical to evidence snapshot; `git status --porcelain` empty.
  QA scenarios: happy - tree-identical diff proof + grouping assertion pass; failure - conflict/abort path executed -> reset to backup tag, HALT (history untouched). Evidence `.omo/evidence/task-13-toolbox-flatline.txt`
  Commit: N | (history rewrite; verified tree-identical via backup tag diff)

- [ ] 14. Rename master -> main, push, GitHub default switch, delete origin/master
  What to do / Must NOT do: (a) `git branch -m master main`; (b) `git push -u origin main`; (c) switch GitHub default: first pre-check `Get-Command gh -ErrorAction SilentlyContinue` (Metis R3: upfront gh availability), then `gh api -X PATCH repos/Bearmancer/Toolbox -f default_branch=main`; (d) IF (c) succeeded (verify via `gh api repos/Bearmancer/Toolbox --jq .default_branch` == `main`): `git push origin --delete master`; ELSE (gh missing/unauthenticated/API error): record follow-up line `FOLLOW-UP: GitHub default branch still master; switch manually then: git push origin --delete master` in evidence and KEEP origin/master - do NOT delete; (e) delete backup tag only after (b) succeeds: `git tag -d backup/pre-flatline-squash`; (f) final state capture: `git branch -a`, `git worktree list`, `git status --porcelain`, `git log --oneline -12` to evidence. MUST NOT force-push; MUST NOT delete origin/master unless default switch verified.
  Parallelization: Wave 6 | Blocked by: 13 | Blocks: F1-F4
  References: user answer Q1 (rename main) + Q2 (push); remote = github.com/Bearmancer/Toolbox.git; origin/master currently 15 behind (pre-squash) - after push of main, origin has both refs until (d)
  Acceptance criteria (agent-executable): `git branch --show-current` == main; `git status --porcelain` empty; `git log origin/main..main --oneline` empty; evidence shows default_branch==main OR the FOLLOW-UP line; backup tag gone iff push succeeded.
  QA scenarios: happy - push + switch + delete verified; failure - push rejected (non-fast-forward impossible here since new ref; auth failure instead) -> HALT with git error, local rename stands; gh failure -> degraded path (d-ELSE) is the designed outcome, not a halt. Evidence `.omo/evidence/task-14-toolbox-flatline.txt`
  Commit: N | -

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. Plan compliance audit
  Verify every Must-have in Scope landed with git/fs evidence: single branch `main` (`git branch -a` = main + optionally origin/master follow-up only); single worktree; `git status` clean; `.omo` contains only plan+draft+evidence; `.superpowers` absent; scratch absent; `docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md` + `docs/superpowers/audits/sacd-probe-journal.md` present; archive at `C:\Users\Lance\Dev\Old\toolbox-oci-sdd-archive\` non-empty; Disc-10 evidence (task-5) present; squash proof (task-13 tree-identical diff) present; no force-push occurred (`git reflog` shows no forced update of origin refs). REJECT on any miss.
- [ ] F2. Code quality review
  `dotnet build` clean; diff-review todo 3's four touched files against the Desktop\Claude sources (only comment-block removal + B9/B10 deltas allowed): `git diff <todo4-commit>^ <todo4-commit> -- src/Services/Audio src/CLI`; confirm no signature drift in DsdConvertService call sites; confirm AGENTS.md rules unviolated (no pragma, no test packages added to Directory.Packages.props). REJECT on drift.
- [ ] F3. Real manual QA
  Agent-executed: `dotnet run --project src\App -- --help` exits 0 and lists audio/sync/azure/dashboard command trees; `dotnet run --project src\App -- audio sacd-convert --help` exits 0 WITHOUT triggering Google OAuth (no browser/hang within 15s - the B9/Program.cs DI-skip proof); re-read `logs/audio.jsonl` Disc-10 sequence from task-5 evidence; verify `state/` file count still 298 (`(Get-ChildItem state -Recurse -File).Count` == 298 - committed, not lost). REJECT on any failure.
- [ ] F4. Scope fidelity
  Verify every Must-NOT-have held: `C:\Users\Lance\.omo` + `C:\Users\Lance\Dev\.omo` untouched (mtime-scan: no files modified today except Dev\.omo session json if harness wrote it); pushed 11 commits unchanged (`git log origin/main~<N>` tail matches pre-work `git log` snapshot from task-1); no state/ content edits (task-7/8/9 commits are the ONLY state/ touchers: `git log --oneline -- state` since backup tag == exactly those 3 subjects); no aws-translate/reader src changes (`git log --oneline -- src` since backup tag shows only todo 4/6 commits); media untouched. REJECT on any violation.

## Commit strategy
Final mainline commit stack above `origin`'s 11 pushed commits (bottom = oldest):
1. ~5-6 squashed topic commits (from the former 15 unpushed; adjacent-run grouping, prepared messages per todo 13e)
2. `fix(audio): no-retry Saracon conversion, correct DFF chunk offset, skip OAuth for audio-only runs`
3. `chore: sync working-state source changes`
4. `chore(state): youtube sync state update (processed+raw)`
5. `chore(state): youtube deletions + merge manifests (reviewed)`
6. `chore(state): dashboard + lastfm state update`
7. `docs(audio): correct rejected UTF-8 root cause; relocate probe journal`
8. `chore: flatline agent artifacts, delete scratch, docs hygiene`
Safety: `backup/pre-flatline-squash` tag guards the rebase until push succeeds (todo 13a/14e). No force-push ever. One commit per logical unit; state split per answered battery C2.

## Success criteria
- `git branch` == `* main` only; `git worktree list` == 1 entry; `git stash list` empty.
- `git status --porcelain` empty; `git log origin/main..main` empty (pushed).
- GitHub default branch == main (or explicit FOLLOW-UP recorded); origin/master deleted iff switch verified.
- `.omo` == plan + draft + evidence only; `.superpowers` gone; root scratch gone; archive + docs rescues in place.
- `dotnet build` clean; `--help` runs OAuth-free; Disc-10 WAV verified >= 250MB with clean log sequence and zero retries.
- `state/` intact (298 files committed); pushed history (older 11) byte-identical; tree-identical squash proven by empty diff vs backup tag.
