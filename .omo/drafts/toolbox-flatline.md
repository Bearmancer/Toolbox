---
slug: toolbox-flatline
status: approved
intent: clear
review_required: true
plan_path: .omo/plans/toolbox-flatline.md
plan_sha256: 3C27E1D30DF33F267A3574E5A5185F8440AAF56C83215F34E362FE54EF3BDF03
review_round_id: metis-r3+momus-r2
round_status: completed
pending-action: handoff for /start-work
review:
  momus:
    status: approved
    workspace_root: C:\Users\Lance\Dev\Toolbox
    runtime_home: C:\Users\Lance\.omo
    target: .omo/plans/toolbox-flatline.md
    round_id: momus-r2
    plan_sha256: 3C27E1D30DF33F267A3574E5A5185F8440AAF56C83215F34E362FE54EF3BDF03
    launch_id: bg_cc67361e
    session: ses_00e500f6bffe1xkhc8wVNKPnD6
    result: APPROVED - zero findings across 12 categories
  metis:
    status: completed
    workspace_root: C:\Users\Lance\Dev\Toolbox
    runtime_home: C:\Users\Lance\.omo
    target: .omo/plans/toolbox-flatline.md
    round_id: metis-r3
    plan_sha256: 11173E6BB0EA85DE52B2ACE93D85FD4FCA1D2A78EF0DFCF3D655810403E324A7
    launch_id: bg_d8583557
    session: ses_00e540bfeffeNgkUchXI3Ul4q4
    result: zero blockers, 3 MAJORs folded, 3 MINORs folded
  independent_oracle:
    status: not_deployed
    note: user explicitly chose Metis + Momus (not Oracle) as the review pair
approach: Flatline Toolbox repo to one plan + one branch (main). Rescue canonical artifacts from sacd-deathloop-repro, drop in the two merged audio .cs files from Desktop\Claude + B9/B10 micro-fixes, commit all working state by domain, delete scratch, prune .omo/.superpowers (oci SDD archived to Dev\Old first, journal+v2 spec rescued to docs/), remove all worktrees + non-main branches, squash the 15 unpushed commits by adjacent topic via two-pass rebase, rename master->main, push + switch GitHub default branch, delete origin/master, drop stash.
---

# Draft: toolbox-flatline

## Components (topology ledger)

| id | outcome (one line) | status | evidence |
| --- | --- | --- | --- |
| C1 | Canonical audio fix lands on mainline (merged SaraconService + DffMetadataStripper, B9/B10 fixes, SacdProbe kept) | active | Desktop\Claude\*.cs; answered battery B1/B4/B6/B9/B10 |
| C2 | All working state committed (code + 298 state files by domain), scratch deleted | active | git status: 119 entries; state/ = 298 files |
| C3 | .omo + .superpowers flatlined to ONE plan file; oci SDD archived; journal+v2 spec rescued to docs/ | active | Toolbox\.omo (38 files), Toolbox\.superpowers (~100 files) |
| C4 | One branch (main), zero worktrees, history squashed by topic, pushed to origin | active | git branch -vv: 5 branches; worktree list: 4 entries (2 prunable) |
| C5 | Disc 10 converts clean (user-run interactive Saracon step, agent-verified) | active | prompt.md §2.3, §5; answered C3 step 8 |

## Open assumptions (announced defaults)

| assumption | adopted default | rationale | reversible? |
| --- | --- | --- | --- |
| "single branch i.e. main" | rename master -> main (asked, user chose rename) | user answer Q1 | yes (rename back) |
| squash granularity | squash ADJACENT same-topic runs among the 15 unpushed, NO reordering (zero conflict risk), ~5-6 commits result; user chose "by topic, then push" | user answer Q2 + user's literal "consecutive commits" | yes (reflog) |
| oci-arr SDD in .superpowers | archive to C:\Users\Lance\Dev\Old\toolbox-oci-sdd-archive\ then delete .superpowers | user answer Q3 | yes (archive) |
| high-accuracy review | REQUIRED (momus + independent oracle) | user answer Q3 append | n/a |
| stash `pre-rebase: .omo state files` | drop after rebase completes | contains only .omo state being pruned | no (but content = pruned artifacts) |
| unclassified src/** drift (~40 files beyond named list) | commit as own "working-state sync" commit; build gate; on build failure revert THAT commit and report BLOCKED | battery priority #1: working state survives | yes |
| B9 dead flags | remove --debug/--verbose from SacdConvertCommand.Settings (keep Program.cs blanket strip) | answered B9: pick one mechanism | yes |
| B10 gap | add Validate() to SpeechTtsCommand requiring exactly one of --text/--file | answered B10 | yes |
| v2 spec + journal | rescue to docs/superpowers/specs + docs/superpowers/audits, commit, then delete .superpowers | answered B7 | yes |
| UTF-8-wrong docs | correction banner at top pointing at journal run #4, no deletion | answered B5 | yes |
| SacdProbe | keep tools/SacdProbe (5 files already in master worktree, untracked) + Toolbox.slnx entry; verify identical to repro version first | answered B6 | yes |
| GitHub default branch switch | gh api PATCH default_branch=main; if gh unavailable/unauthenticated: keep origin/master, record follow-up, do NOT delete origin/master | deterministic fallback | yes |
| Disc 10 conversion run | USER-executed from interactive terminal (Saracon GUI needs attached desktop - spec §2.3); agent does precondition check + post-run log/size verification | physical constraint, evidence-based | n/a |

## Findings (cited)

- Repo: C:\Users\Lance\Dev\Toolbox; remote origin = github.com/Bearmancer/Toolbox.git; default branch master; master 15 ahead of origin/master, 0 behind.
- Branches: feat/youtube-duplicate-merge (0 unique), feature/process-runner-streaming (0 unique), oci-arr-exhaustive-repair (0 unique), sacd-deathloop-repro (17 unique: fixes e79e8e1/e14e92e/51193e3 + probe harness + journals).
- Worktrees: main tree; .worktrees/youtube-duplicate-playlist-merge (live, ~58MB); Toolbox-sacd-repro registered at C:/Users/Lance/Dev/Toolbox-sacd-repro but dir was MOVED inside main tree to Toolbox\Toolbox-sacd-repro (admin path stale -> flagged prunable); oci-arr-repair ghost (dir gone, admin remains).
- Uncommitted on master: 119 entries (code + state + docs deletions + .omo/goal,.omo/ulw-loop deletions + .gitignore/AGENTS.md/Toolbox.slnx mods).
- state/: 298 files (youtube 295: processed 145, raw 145, deleted 3, merge-manifests 1; dashboard 2; lastfm 1).
- .omo: Plan.md (OCI, completed), plans/{GIT-CLEANUP-DECISION-BATTERY, SACD-FIX-FINAL-REPORT, oracle-sacd-verification}.md, plans/aws-translate/phases (5), plans/reader/phases (6), run-continuation (27 json).
- .superpowers: audit/ (sacd-probe-journal.md + 3), sdd/youtube-duplicate-playlist-merge (20 reports), sdd/oci-arr-exhaustive-repair (python tools + venv + evidence).
- Scratch root: SACD errors.md (348KB), youtube-sync-log.md (708KB), .athena-state.json.
- tools/SacdProbe in master worktree: 5 files (ProbeRunner.cs, ProcessRunnerTests.cs, Program.cs, RealDffFixture.cs, SacdProbe.csproj).
- docs/: 7 files; docs/superpowers/specs/ exists EMPTY (v2 spec lives only on repro branch).
- ~/.omo (C:\Users\Lance\.omo) verified OUT of scope: agent runtime home only; its sole Toolbox artifact (oci-arr-repair worktree dir) already gone; residue = prunable git admin + fully-merged branch. Dev\.omo holds 1 disposable session json.
- Stash: stash@{0} "pre-rebase: .omo state files".
- Answered battery (Desktop\Claude\SACD-decision-battery-answered.md) = decision record; merged SaraconService.cs/DffMetadataStripper.cs verified drop-in for DsdConvertService call sites.

## Decisions (with rationale)

1. Rescue-before-delete ordering: v2 spec + SacdProbe verification + oci archive happen BEFORE any branch/worktree/.superpowers deletion.
2. Commit-then-squash: land ALL new commits first, then one rebase over origin/master..HEAD squashing only the bottom 15 (adjacent same-topic runs, no reorder); new commits replay on top.
3. Two-pass rebase: pass 1 fixup adjacent runs; pass 2 reword surviving run heads with prepared messages. Fully scripted (GIT_SEQUENCE_EDITOR/GIT_EDITOR), deterministic.
4. Branch rename AFTER squash + all commits; push main; switch GitHub default; delete origin/master only if switch succeeded.
5. Single surviving plan file = .omo/plans/toolbox-flatline.md; every other .omo file deleted (content subsumed: decisions live in this plan + docs/ rescues).
6. Disc 10 conversion is the ONE user-executed step (interactive desktop constraint); agent verifies precondition + post-run evidence; plan HALTS there until user reports, no skip.

## Scope IN

- Everything in approach line above; all 5 components C1-C5.

## Scope OUT (Must NOT have)

- No changes to C:\Users\Lance\.omo (agent runtime home) or C:\Users\Lance\Dev\.omo.
- No rewrite of already-pushed history (older 11 commits untouched; no force-push).
- No deletion/modification of docs/ existing 7 files except the B5 correction banner.
- No deletion of state/ file CONTENT (only commit); no media/ISO touching.
- No changes to aws-translate/reader feature CODE (only their .omo plan files get pruned).
- No new features, no refactors beyond B9/B10 micro-fixes, no test frameworks (repo rule: no test NuGet packages).
- No implementation in the planning session.

## Open questions

None - all forks answered (Q1 rename main, Q2 squash-by-topic+push, Q3 archive-then-delete + high-accuracy review).

## Approval gate
status: awaiting-approval
next-action: on approval -> write .omo/plans/toolbox-flatline.md, run Metis gap analysis, append todos, then dual high-accuracy review (momus + oracle) before handoff.
