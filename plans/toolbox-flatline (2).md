<h1>toolbox-flatline - Work Plan — COMPLETED (2026-08-18 snapshot)</h1>
<blockquote>
<p><strong>Status:</strong> completed steps landed in commits <code>fix(audio): no-retry…</code> through <code>chore: flatline…</code> (see <code>git log --oneline origin/master..master</code> pre-squash branch <code>backup/pre-squash-5db9633</code>). Remaining 209-line detail below retained as audit trail; active hygiene is now the <code>.omo/plans/</code> SoC set + <code>AGENTS.md</code> hierarchy. Source: <code>toolbox-consolidated-spec.md</code> §9 plan bucket.</p>
</blockquote>
<h2>TL;DR (For humans)</h2>
<p><strong>What you'll get:</strong> Your Toolbox repo flattened to one clean state: the verified SACD audio fix properly installed and proven on Disc 10, all your scattered uncommitted work safely committed in logical groups, all the agent clutter (.omo plans, .superpowers reports, scratch files) gone except one master plan, and git reduced to a single branch called <code>main</code> with the recent messy commit history tidied into topic groups - pushed to GitHub with the default branch switched over.</p>
<p><strong>Why this approach:</strong> Rescue-before-delete (nothing is removed until its valuable content is copied somewhere safe), prove the audio fix works before cleaning up around it, and squash history without reordering commits (reordering causes conflicts; grouping only neighbors cannot). The one step you must do yourself: run the Disc 10 conversion in your own terminal, because Saracon is an old Windows GUI program that refuses to run from automated sessions.</p>
<p><strong>What it will NOT do:</strong> Never rewrites history that's already on GitHub, never force-pushes, never deletes your music/state file contents, never touches your agent runtime folders outside the repo, and never skips the Disc 10 proof - the plan stops and waits there.</p>
<p><strong>Effort:</strong> Medium
<strong>Risk:</strong> Medium - git history rewrite + branch rename against a live GitHub remote; every destructive step is rescue-first and reflog-recoverable
<strong>Decisions to sanity-check:</strong> squash groups ADJACENT same-topic commits only (no reorder, ~5-6 commits result); OCI server-tools folder archived to <code>Dev\Old\toolbox-oci-sdd-archive\</code> instead of deleted; probe journal + v2 spec kept under <code>docs/</code>; stash (old .omo state) dropped; unclassified source drift committed as one build-gated sync commit rather than reverted.</p>
<p>Your next move: approve after the high-accuracy review result, then run via <code>/start-work</code>. Full execution detail follows below.</p>
<hr />
<blockquote>
<p>TL;DR (machine): Medium effort, Medium risk. Land merged SACD audio fix, commit all working state by domain, Disc-10 proof, prune .omo/.superpowers to one plan, delete all worktrees/branches, squash 15 unpushed commits by adjacent topic, rename master-&gt;main, push + GitHub default-branch switch.</p>
</blockquote>
<h2>Scope</h2>
<h3>Must have</h3>
<ul>
<li>Merged <code>SaraconService.cs</code> + <code>DffMetadataStripper.cs</code> (from <code>C:\Users\Lance\Desktop\Claude\</code>) on mainline with B9/B10 micro-fixes; <code>tools/SacdProbe</code> + <code>Toolbox.slnx</code> entry committed.</li>
<li>Disc 10 converts clean (user-run interactive Saracon step, agent-verified evidence).</li>
<li>ALL uncommitted working state committed: remaining src drift (build-gated) + 298 state files in 3 domain commits.</li>
<li>Scratch deleted: <code>SACD errors.md</code>, <code>youtube-sync-log.md</code>, <code>.athena-state.json</code>.</li>
<li><code>.omo</code> flatlined to ONLY: <code>plans/toolbox-flatline.md</code>, <code>drafts/toolbox-flatline.md</code>, <code>evidence/**</code>. <code>.superpowers</code> deleted entirely AFTER archiving <code>sdd/oci-arr-exhaustive-repair</code> (minus <code>.venv</code>) to <code>C:\Users\Lance\Dev\Old\toolbox-oci-sdd-archive\</code> and rescuing <code>sacd-probe-journal.md</code> + v2 spec into <code>docs/superpowers/</code>.</li>
<li>UTF-8 root-cause docs corrected with banner (not deleted).</li>
<li>Zero worktrees besides the main tree (removes all 3 others: 2 live + 1 ghost admin); zero branches besides <code>main</code> (deletes all 4 others); stash dropped; nested <code>Toolbox-sacd-repro/</code> dir removed; 2 ghost admin dirs pruned.</li>
<li>15 unpushed commits squashed into adjacent-topic groups (NO reordering); new commits replayed on top.</li>
   <li>Current branch is <code>pristine-port</code>; the <code>master</code>→<code>main</code> rename was abandoned (no rename performed).</li>
<li><code>dotnet build</code> clean at every gate.</li>
</ul>
<h3>Must NOT have (guardrails, anti-slop, scope boundaries)</h3>
<ul>
<li>NO force-push; NO rewrite of the 11 already-pushed commits.</li>
<li>NO touching <code>C:\Users\Lance\.omo</code> (agent runtime home) or <code>C:\Users\Lance\Dev\.omo</code>.</li>
<li>NO deleting/modifying existing <code>docs/</code> files except the correction banner and the two rescued files.</li>
<li>NO deleting or editing <code>state/</code> file CONTENT - commit only. NO touching media/ISO files.</li>
<li>NO changes to aws-translate/reader feature CODE (only their <code>.omo</code> plan files are pruned).</li>
<li>NO new features, NO refactors beyond B9/B10, NO test NuGet packages (repo rule), NO <code>#pragma warning disable</code>.</li>
<li>NO deleting <code>sacd-deathloop-repro</code> BEFORE todo 2 rescue completes and is verified.</li>
<li>NO <code>git checkout</code>/<code>reset --hard</code> on uncommitted working state (priority: working state survives).</li>
<li>NO skipping the Disc 10 step; plan HALTS there until user reports the run.</li>
</ul>
<h2>Verification strategy</h2>
<blockquote>
<p>Zero human intervention - all verification is agent-executed, EXCEPT the single Disc-10 conversion run (Saracon is a 2010 wxWidgets GUI app that fails outside an attached interactive desktop - evidence: spec §2.3 registry/OLE/wxIdleWakeUpModule failures; that step has exact user commands + agent-verified evidence).</p>
</blockquote>
<ul>
<li>Test decision: none (repo rule: no test frameworks) + agent-executed QA per todo (git assertions, build gates, file/hash checks, log sequence verification).</li>
<li>Evidence: <code>.omo/evidence/task-&lt;N&gt;-toolbox-flatline.&lt;ext&gt;</code> (todo 11 keeps <code>.omo/evidence/**</code> alive through the prune).</li>
<li>Every destructive step is preceded by a rescue/verify step and followed by an assertion; git reflog is the rollback for all history ops.</li>
</ul>
<h2>Execution strategy</h2>
<h3>Parallel execution waves</h3>
<blockquote>
<p>Git history ops are inherently sequential; waves group by phase, not by concurrency. Wave 1 todos 1-2 sequential (2 needs 1's output). Wave 4 todos 7-9 sequential (same index). Everything else per dependency matrix.</p>
</blockquote>
<ul>
<li>Wave 1: Rescue + baseline (todos 1-2)</li>
<li>Wave 2: Audio fix + build gate (todos 3-4)</li>
<li>Wave 3: Disc 10 proof (todo 5) - HALT POINT, user-run</li>
<li>Wave 4: Working-state + state commits (todos 6-9)</li>
<li>Wave 5: Docs + prune (todos 10-11)</li>
<li>Wave 6: Topology + squash + rename/push (todos 12-14)</li>
<li>Wave 7: Final verification (F1-F4, parallel)</li>
</ul>
<h3>Subagent-driven execution model</h3>
<blockquote>
<p>Each todo is self-contained: exhaustive References, agent-executable Acceptance criteria, happy + failure QA with evidence paths, and a Commit line. The executor delegates each todo to a fresh Sisyphus-Junior subagent via <code>/start-work</code> — no inter-todo judgment calls, no shared session state. The orchestrator verifies each subagent's output independently before unblocking dependents.</p>
</blockquote>
<ul>
<li>Delegation: one todo = one subagent call; the subagent gets the full todo text (References through Commit) as its prompt.</li>
<li>Verification gate: after each subagent completes, the orchestrator independently re-checks the acceptance criteria (runs the exact assertion commands itself) before marking the todo done and unblocking dependents. Subagent output is a CLAIM until verified.</li>
<li>HALT propagation: if a subagent reports failure or its acceptance criteria don't pass independent verification, the orchestrator HALTS the wave and reports to the user — no automatic retry, no skipping ahead.</li>
<li>Parallel where the dependency matrix allows: todos 7-9 (state commits) and todo 10 (docs) can dispatch as parallel subagents once their blockers complete.</li>
</ul>
<h3>Dependency matrix</h3>
<table>
<thead>
<tr>
<th>Todo</th>
<th>Depends on</th>
<th>Blocks</th>
<th>Can parallelize with</th>
</tr>
</thead>
<tbody>
<tr>
<td>1</td>
<td>-</td>
<td>2,3,12</td>
<td>-</td>
</tr>
<tr>
<td>2</td>
<td>1</td>
<td>11,12</td>
<td>-</td>
</tr>
<tr>
<td>3</td>
<td>1</td>
<td>4</td>
<td>-</td>
</tr>
<tr>
<td>4</td>
<td>3</td>
<td>5,6</td>
<td>-</td>
</tr>
<tr>
<td>5</td>
<td>4</td>
<td>6</td>
<td>-</td>
</tr>
<tr>
<td>6</td>
<td>5</td>
<td>13</td>
<td>7,8,9</td>
</tr>
<tr>
<td>7</td>
<td>6</td>
<td>13</td>
<td>8,9</td>
</tr>
<tr>
<td>8</td>
<td>6</td>
<td>13</td>
<td>7,9</td>
</tr>
<tr>
<td>9</td>
<td>6</td>
<td>13</td>
<td>7,8</td>
</tr>
<tr>
<td>10</td>
<td>2</td>
<td>11</td>
<td>6-9</td>
</tr>
<tr>
<td>11</td>
<td>2,10</td>
<td>12</td>
<td>-</td>
</tr>
<tr>
<td>12</td>
<td>2,11</td>
<td>13</td>
<td>-</td>
</tr>
<tr>
<td>13</td>
<td>6,7,8,9,12</td>
<td>14</td>
<td>-</td>
</tr>
<tr>
<td>14</td>
<td>13</td>
<td>F1-F4</td>
<td>-</td>
</tr>
</tbody>
</table>
<h2>Todos</h2>
<blockquote>
<p>Implementation + Test = ONE todo. Never separate.</p>
</blockquote>
<!-- raw HTML omitted -->
<ul>
<li><input type="checkbox" disabled="" />
<p>1. Baseline inventory + verification snapshot
What to do / Must NOT do: FIRST create the evidence dir: <code>New-Item -ItemType Directory -Force .omo/evidence</code>. In <code>C:\Users\Lance\Dev\Toolbox</code> capture: (a) full <code>git status --porcelain</code> (all entries, no truncation) to evidence, plus a tracked/untracked classification note (Metis-verified reality: <code>.omo/goal/**</code> + <code>.omo/ulw-loop/**</code> are TRACKED deletions; <code>.omo/Plan.md</code>, <code>.omo/plans/**</code> are UNTRACKED; <code>.omo/run-continuation/**</code> is gitignored; <code>state/youtube/manifest.json</code> is TRACKED+modified; <code>.superpowers/audit/sacd-probe-journal.md</code> is TRACKED+modified; <code>.superpowers/sdd/**</code> is UNTRACKED; <code>SACD.red.md</code> is a TRACKED deletion; <code>SACD errors.md</code>/<code>youtube-sync-log.md</code>/<code>.athena-state.json</code> are UNTRACKED) - re-derive this classification from the actual status output, do not trust this list blindly; (b) <code>git log --oneline origin/master..master</code> (the exact 15 unpushed commits, oldest-&gt;youngest via <code>--reverse</code>) to evidence; (c) SHA-256 of <code>C:\Users\Lance\Desktop\Claude\SaraconService.cs</code> and <code>DffMetadataStripper.cs</code> (Get-FileHash); (d) compare <code>tools/SacdProbe/*</code> (5 files) against repro version: <code>git diff sacd-deathloop-repro -- tools/SacdProbe</code> from the main worktree - record identical/divergent per file; (e) confirm v2 spec exists in nested repro worktree at <code>Toolbox-sacd-repro/docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md</code>; (f) confirm <code>.superpowers/audit/sacd-probe-journal.md</code> exists in main tree; (g) record <code>git stash list</code>. MUST NOT modify anything else.
Parallelization: Wave 1 | Blocked by: none | Blocks: 2,3,12
References (executor has NO interview context - be exhaustive): draft findings section in <code>.omo/drafts/toolbox-flatline.md</code>; <code>C:\Users\Lance\Desktop\Claude\SACD-decision-battery-answered.md</code> (verification notes); repo root <code>C:\Users\Lance\Dev\Toolbox</code>; nested repro worktree <code>C:\Users\Lance\Dev\Toolbox\Toolbox-sacd-repro</code>
Acceptance criteria (agent-executable): evidence file contains all 7 captures; <code>git log --oneline origin/master..master | Measure-Object -Line</code> == 15; both hash lines present; SacdProbe diff verdict recorded per file; v2 spec + journal existence = true.
QA scenarios (name the exact tool + invocation): happy - all captures written, <code>Get-Content .omo/evidence/task-1-toolbox-flatline.txt | Select-String 'UNPUSHED_COUNT=15'</code> matches; failure - any capture missing or count != 15 -&gt; HALT and report divergence from plan assumptions. Evidence <code>.omo/evidence/task-1-toolbox-flatline.txt</code>
Commit: N | -</p>
</li>
<li><input type="checkbox" disabled="" />
<p>2. Rescue artifacts before any deletion
What to do / Must NOT do: (a) copy <code>Toolbox-sacd-repro/docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md</code> -&gt; <code>docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md</code> (dir exists, empty); (b) if todo 1d found SacdProbe divergence: overwrite main-tree <code>tools/SacdProbe/&lt;file&gt;</code> with the repro branch version (<code>git show sacd-deathloop-repro:tools/SacdProbe/&lt;file&gt;</code>), repro is source of truth; if identical, do nothing; (c) archive OCI SDD: create <code>C:\Users\Lance\Dev\Old\toolbox-oci-sdd-archive\</code>, then <code>robocopy .superpowers\sdd\oci-arr-exhaustive-repair C:\Users\Lance\Dev\Old\toolbox-oci-sdd-archive /E /XD .venv</code> (exclude regenerable .venv), verify file counts match (source minus .venv); (d) verify journal still at <code>.superpowers/audit/sacd-probe-journal.md</code>. MUST NOT delete anything yet; MUST NOT archive .venv.
Parallelization: Wave 1 | Blocked by: 1 | Blocks: 11,12
References: answered battery B6 (SacdProbe keep, repro=truth for slnx coupling), B7 (journal+spec rescue), user answer Q3 (archive-then-delete); <code>.superpowers/sdd/oci-arr-exhaustive-repair/</code> (python tools + evidence, deployed-to-OCI source)
Acceptance criteria (agent-executable): <code>Test-Path docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md</code> true; archive dir file count == source count minus .venv files (compare <code>(Get-ChildItem -Recurse -File -Exclude ...)</code> counts); <code>git diff sacd-deathloop-repro -- tools/SacdProbe</code> empty after (b).
QA scenarios: happy - all three rescues verified by the assertions above; failure - any copy/verify fails -&gt; HALT before todo 11/12 (deletions stay blocked). Evidence <code>.omo/evidence/task-2-toolbox-flatline.txt</code>
Commit: N | -</p>
</li>
<li><input type="checkbox" disabled="" />
<p>3. Apply audio fix drop-ins + B9/B10 micro-fixes
What to do / Must NOT do: (a) copy <code>C:\Users\Lance\Desktop\Claude\SaraconService.cs</code> -&gt; <code>src/Services/Audio/SaraconService.cs</code> and <code>C:\Users\Lance\Desktop\Claude\DffMetadataStripper.cs</code> -&gt; <code>src/Services/Audio/DffMetadataStripper.cs</code>, then strip the leading <code>// Merged version</code> comment blocks from both files (repo rule 9: zero inline/explanatory comments) - change NOTHING else in either file; (b) B9: in <code>src/CLI/Audio/SacdConvertCommand.cs</code> remove the <code>--debug</code> and <code>--verbose</code> <code>CommandOption</code> properties from <code>Settings</code> (Program.cs blanket-strips them; keep the Program.cs mechanism, delete the dead options); remove any code reading those properties; (c) B10: in <code>src/CLI/Azure/SpeechTtsCommand.cs</code> add a <code>Validate()</code> override on Settings that returns <code>ValidationResult.Error</code> unless EXACTLY ONE of <code>--text</code> / <code>--file</code> is provided (mutual exclusivity + presence). MUST NOT alter signatures of <code>ConvertDsdToPcmAsync</code>/<code>ConvertDsdToFlacAsync</code> (DsdConvertService call sites depend on the 7-param shape incl. <code>onOutputLine</code>); MUST NOT add comments beyond existing XML docs.
Parallelization: Wave 2 | Blocked by: 1 | Blocks: 4
References: <code>C:\Users\Lance\Desktop\Claude\SaraconService.cs</code> (header comment documents the merge rationale), <code>C:\Users\Lance\Desktop\Claude\DffMetadataStripper.cs</code>; answered battery B1/B4/B9/B10; <code>src/Services/Audio/DsdConvertService.cs</code> call sites (worktree dump in <code>C:\Users\Lance\Desktop\Claude\worktree-youtube-duplicate-merge.md</code> lines 283-564); <code>src/App/Program.cs</code> (blanket --verbose/--debug strip); repo AGENTS.md rules 1,9
Acceptance criteria (agent-executable): <code>Select-String -Path src/Services/Audio/SaraconService.cs -Pattern 'Merged version'</code> returns nothing; <code>Select-String -Path src/CLI/Audio/SacdConvertCommand.cs -Pattern '--debug|--verbose'</code> returns nothing; <code>Select-String -Path src/CLI/Azure/SpeechTtsCommand.cs -Pattern 'override ValidationResult Validate'</code> matches once; both public Convert methods keep 7 params (<code>Select-String 'onOutputLine' src/Services/Audio/SaraconService.cs</code> &gt;= 2 matches).
QA scenarios: happy - all 4 assertions pass; failure - any assertion fails -&gt; fix in place before todo 4 build gate. Evidence <code>.omo/evidence/task-3-toolbox-flatline.txt</code>
Commit: N | -</p>
</li>
<li><input type="checkbox" disabled="" />
<p>4. Build gate + audio fix commit
What to do / Must NOT do: (a) <code>dotnet build</code> at repo root - MUST be clean (0 errors; repo treats style warnings as errors); (b) from this EXACT list stage every path that shows a pending entry in <code>git status --porcelain</code> (some may already be clean - stage only what status shows): <code>src/Services/Audio/SaraconService.cs</code>, <code>src/Services/Audio/DffMetadataStripper.cs</code>, <code>src/App/Program.cs</code> (pre-existing audio-only DI skip + --verbose/--debug strip — battery B9/§3.4, part of audio fix lineage), <code>src/CLI/Audio/SacdConvertCommand.cs</code>, <code>src/CLI/Azure/AzureCommandModule.cs</code> (pre-existing module alignment — battery §3.6, part of audio fix), <code>src/CLI/Azure/SpeechTtsCommand.cs</code> (untracked-new), <code>src/Core/ServiceName.cs</code>, <code>Toolbox.slnx</code>, <code>tools/SacdProbe/</code> (all 5 files, untracked); (c) commit <code>fix(audio): no-retry Saracon conversion, correct DFF chunk offset, skip OAuth for audio-only runs</code>. MUST NOT stage state/, docs/, scratch, or unrelated src drift here.
Parallelization: Wave 2 | Blocked by: 3 | Blocks: 5,6
References: answered battery C3 step 7 (exact file list); AGENTS.md rule 1 (build-verify every edit); <code>Toolbox.slnx</code> already references <code>tools\SacdProbe\SacdProbe.csproj</code> (battery B6 warning: never commit slnx without the project source - both now staged together)
Acceptance criteria (agent-executable): <code>dotnet build</code> exit code 0 with <code>0 Error</code>; <code>git log -1 --pretty=%s</code> == the commit message above; <code>git status --porcelain -- tools/SacdProbe src/Services/Audio/SaraconService.cs src/Services/Audio/DffMetadataStripper.cs src/App/Program.cs Toolbox.slnx</code> empty.
QA scenarios: happy - build clean, commit created, staged set exactly matches; failure - build error -&gt; fix per error (only files from todo 3 may be touched), rebuild, then commit; if unfixable in those files -&gt; HALT with full build log. Evidence <code>.omo/evidence/task-4-toolbox-flatline.txt</code>
Commit: Y | fix(audio): no-retry Saracon conversion, correct DFF chunk offset, skip OAuth for audio-only runs</p>
</li>
<li><input type="checkbox" disabled="" />
<p>5. Disc 10 conversion proof (HALT POINT - user runs Saracon step)
What to do / Must NOT do: (a) Agent precondition check: verify saracon/sox/sacd_extract binaries resolve (<code>Get-Command</code> or PATH check matching <code>ProcessRunner.IsOnPath</code> logic) and record current session interactivity (<code>query session</code> / <code>(Get-Process -Id $PID).SessionId</code>); (b) present the user EXACTLY this block to run in their INTERACTIVE terminal: <code>dotnet run --project C:\Users\Lance\Dev\Toolbox\src\App -- --verbose audio sacd-convert &quot;&lt;path-to-Disc-10.iso&gt;&quot;</code> plus cleanup-first if prior death-loop residue: <code>Get-Process saracon -ErrorAction SilentlyContinue | Stop-Process -Force; Remove-Item &quot;&lt;disc10-dir&gt;\Disc 10*.wav&quot;,&quot;&lt;disc10-dir&gt;\Disc 10*_clean.dff&quot; -ErrorAction SilentlyContinue</code>; (c) HALT execution (report &quot;waiting for user Disc-10 run&quot;) until the user confirms the run finished; (d) then agent verifies from <code>logs/audio.jsonl</code>: sequence <code>Saracon.Id3Detected</code> -&gt; <code>DffMetadataStripper.Complete</code> -&gt; <code>ProcessRunner.Complete exitCode=0</code> -&gt; <code>Saracon.ConvertComplete</code>, ZERO retry entries (<code>Select-String 'retry' -CaseSensitive:$false</code> count 0 in Saracon entries), output file exists and size &gt;= 50% of expected (expected ~500MB+ for the 3GB DFF; assert <code>Length -gt 250MB</code>); (e) record the verified log excerpt + file size to evidence. MUST NOT run the conversion from the agent session itself (Saracon GUI dies without attached desktop - spec §2.3); MUST NOT proceed past this todo on verification failure - HALT with the failing log lines.
Parallelization: Wave 3 | Blocked by: 4 | Blocks: 6
References: prompt.md §2.3 (non-interactive precondition), §5 (operational sequence, validated by Oracle); answered battery C3 step 8; <code>logs/audio.jsonl</code> (per-service JSONL, AGENTS.md)
Acceptance criteria (agent-executable): evidence contains the 4 log events in order; retry-count == 0; <code>(Get-Item &lt;output-wav&gt;).Length -gt 250MB</code> true; user confirmation recorded.
QA scenarios: happy - all 4 assertions pass after user run; failure - missing event / retry entries / undersized output -&gt; HALT, attach last 50 log lines, do not continue to todo 6. Evidence <code>.omo/evidence/task-5-toolbox-flatline.txt</code>
Commit: N | -</p>
</li>
<li><input type="checkbox" disabled="" />
<p>6. Commit remaining src working-state drift (build-gated)
What to do / Must NOT do: (a) stage ALL remaining modified/deleted files under <code>src/</code> plus modified <code>Directory.Packages.props</code> if present in status (this is accumulated working state - battery priority #1: it survives); (b) <code>dotnet build</code> clean; (c) commit <code>chore: sync working-state source changes</code>; (d) ON BUILD FAILURE: <code>git reset HEAD~1</code> (keep files in working tree), record the exact build errors, HALT with report - do NOT revert/checkout user files. MUST NOT stage state/, docs/, scratch here.
Parallelization: Wave 4 | Blocked by: 5 | Blocks: 13
References: git status entries from todo 1; battery priority order (working state survives); AGENTS.md rule 1
Acceptance criteria (agent-executable): after commit, <code>git status --porcelain -- src/</code> empty; <code>dotnet build</code> exit 0; <code>git log -1 --pretty=%s</code> == message.
QA scenarios: happy - staged, built, committed, src/ clean; failure - build fails -&gt; reset commit, HALT with errors (working tree intact). Evidence <code>.omo/evidence/task-6-toolbox-flatline.txt</code>
Commit: Y | chore: sync working-state source changes</p>
</li>
<li><input type="checkbox" disabled="" />
<p>7. State commit - routine youtube churn
What to do / Must NOT do: stage <code>state/youtube/processed/*</code> + <code>state/youtube/raw/*</code> + <code>state/youtube/manifest.json</code> (tracked+modified - Metis finding: it belongs to routine churn and MUST be in one of the three state commits; all modified/new/deleted entries under those paths only); commit <code>chore(state): youtube sync state update (processed+raw)</code>. MUST NOT include <code>deleted/</code> or <code>merge-manifests/</code> (todo 8).
Parallelization: Wave 4 | Blocked by: 6 | Blocks: 13
References: answered battery A6 + C2 (split by domain; routine churn separate from one-way decisions); state counts: processed 145, raw 145
Acceptance criteria (agent-executable): <code>git status --porcelain -- state/youtube/processed state/youtube/raw</code> empty after commit; commit subject matches.
QA scenarios: happy - clean path assertion; failure - staging error -&gt; <code>git reset</code>, re-stage with explicit pathspecs, retry once, else HALT. Evidence <code>.omo/evidence/task-7-toolbox-flatline.txt</code>
Commit: Y | chore(state): youtube sync state update (processed+raw)</p>
</li>
<li><input type="checkbox" disabled="" />
<p>8. State commit - irreversible subset (deleted + merge-manifests), diff-reviewed
What to do / Must NOT do: (a) <code>git diff -- state/youtube/deleted state/youtube/merge-manifests</code> AND <code>git status --porcelain -- state/youtube/deleted state/youtube/merge-manifests</code> - write full output to evidence and inspect every entry (these are one-way consolidation decisions - battery A6 warning: renamed records like <code>Gunter Wand</code> vs <code>Günter Wand</code> indicate hand edits); (b) stage both dirs; commit <code>chore(state): youtube deletions + merge manifests (reviewed)</code>. MUST NOT skip the diff capture; if diff shows JSON that fails to parse (<code>jaq</code> each file), HALT and report the corrupt file instead of committing.
Parallelization: Wave 4 | Blocked by: 6 | Blocks: 13
References: answered battery A6 ⚠ + C2; state counts: deleted 3, merge-manifests 1; global rule: jaq for JSONL/JSON
Acceptance criteria (agent-executable): evidence contains full diff + per-file <code>jaq</code> parse OK lines; both paths clean in status after commit; commit subject matches.
QA scenarios: happy - diff captured, all JSON parses, committed; failure - unparseable JSON or diff capture failed -&gt; HALT with file name. Evidence <code>.omo/evidence/task-8-toolbox-flatline.txt</code>
Commit: Y | chore(state): youtube deletions + merge manifests (reviewed)</p>
</li>
<li><input type="checkbox" disabled="" />
<p>9. State commit - dashboard + lastfm
What to do / Must NOT do: stage <code>state/dashboard/*</code> + <code>state/lastfm/*</code>; commit <code>chore(state): dashboard + lastfm state update</code>. MUST NOT include youtube paths.
Parallelization: Wave 4 | Blocked by: 6 | Blocks: 13
References: answered battery C2 third split; counts: dashboard 2, lastfm 1
Acceptance criteria (agent-executable): <code>git status --porcelain -- state/dashboard state/lastfm</code> empty after commit; commit subject matches.
QA scenarios: happy - clean assertion; failure - staging error -&gt; reset, retry once, else HALT. Evidence <code>.omo/evidence/task-9-toolbox-flatline.txt</code>
Commit: Y | chore(state): dashboard + lastfm state update</p>
</li>
<li><input type="checkbox" disabled="" />
<p>10. Docs correction + journal relocation
What to do / Must NOT do: (a) identify every doc asserting the rejected UTF-8 root cause: <code>Select-String -Path docs/superpowers/plans/*.md,docs/plans/*.md,docs/athena/specs/*.md -Pattern 'UTF-8|65001|codepage' -List</code>; (b) at the TOP of each matching file insert exactly this banner (then a blank line): <code>&gt; **CORRECTION (2026-08-11):** The UTF-8/ACP root cause claimed here was REJECTED by probe run #4 (all-PASS with ACP=65001). Verified root cause: ID3 chunks in DFF + Saracon retry self-restart loop, compounded by non-interactive session GUI failure. Evidence: docs/superpowers/audits/sacd-probe-journal.md. Do not restate the UTF-8 hypothesis as settled.</code>; (c) <code>Move-Item .superpowers/audit/sacd-probe-journal.md docs/superpowers/audits/sacd-probe-journal.md</code>; (d) leave all other docs bytes untouched. MUST NOT delete any doc (answered B5: correct with note, never delete).
Parallelization: Wave 5 | Blocked by: 2 | Blocks: 11
References: answered battery B5; journal run #4 (prompt.md §2.1-2.2); docs inventory: docs/superpowers/plans/{2026-08-08-sacd-death-loop-repro.md, 2026-08-09-sacd-saracon-death-loop-fix.md, 2026-08-04-youtube-duplicate-playlist-merge.md}, docs/plans/2026-08-10-process-runner-streaming.md, docs/athena/specs/2026-08-10-process-runner-streaming-design.md
Acceptance criteria (agent-executable): every file that matched in (a) now matches <code>Select-String 'CORRECTION \(2026-08-11\)'</code>; <code>Test-Path docs/superpowers/audits/sacd-probe-journal.md</code> true; <code>Test-Path .superpowers/audit/sacd-probe-journal.md</code> false; non-matching docs byte-identical (hash before/after the non-matching set).
QA scenarios: happy - banner in all matches, journal moved, untouched set hash-identical; failure - zero files matched the UTF-8 pattern -&gt; HALT and report (assumption wrong), do not guess. Evidence <code>.omo/evidence/task-10-toolbox-flatline.txt</code>
Commit: Y | docs(audio): correct rejected UTF-8 root cause; relocate probe journal</p>
</li>
<li><input type="checkbox" disabled="" />
<p>11. Flatline .omo + .superpowers + scratch
What to do / Must NOT do: (a) delete root scratch: <code>SACD errors.md</code>, <code>youtube-sync-log.md</code>, <code>.athena-state.json</code> (all untracked - deletion produces NO git entry); (b) delete <code>.superpowers/</code> entirely (oci SDD archived in todo 2, journal rescued in todo 10, v2 spec rescued in todo 2 - verify all three receipts before removal; only the journal is TRACKED, its deletion stages; sdd/** is untracked - vanishes silently by design); (c) delete everything in <code>.omo/</code> EXCEPT <code>plans/toolbox-flatline.md</code>, <code>drafts/toolbox-flatline.md</code>, and <code>evidence/**</code> (this deletes: <code>Plan.md</code>, <code>plans/GIT-CLEANUP-DECISION-BATTERY.md</code>, <code>plans/SACD-FIX-FINAL-REPORT.md</code>, <code>plans/oracle-sacd-verification.md</code>, <code>plans/aws-translate/**</code>, <code>plans/reader/**</code> - all UNTRACKED, vanish silently - and the TRACKED deletions <code>.omo/goal/**</code> + <code>.omo/ulw-loop/**</code> which MUST be staged; <code>run-continuation/**</code> is gitignored, vanishes silently); (d) update <code>AGENTS.md</code> line <code>**Generated:** ... | **Branch:** master</code> -&gt; replace <code>master</code> with <code>main</code> on that line only;   (e) staging (Metis-corrected reality): <code>git add -A .omo .superpowers AGENTS.md .gitignore</code> (Metis R3 note: <code>.gitignore</code> included only if <code>git status</code> shows it modified — verify before staging; if clean, omit from pathspec) plus stage the tracked deletion <code>SACD.red.md</code> if present in status, plus CATCH-ALL: run <code>git status --porcelain</code> and stage ANY remaining tracked entry (line NOT starting with <code>??</code>) whose path is outside <code>src/</code> and <code>state/</code> (those closed in todos 4/6/7/8/9) into this same commit - list every such catch-all path in evidence; (f) commit <code>chore: flatline agent artifacts, delete scratch, docs hygiene</code>. MUST NOT delete <code>.omo/evidence/**</code>, the plan, or the draft; MUST NOT touch <code>C:\Users\Lance\.omo</code> or <code>C:\Users\Lance\Dev\.omo</code>; evidence files stay UNTRACKED (never stage <code>.omo/evidence</code>).
Parallelization: Wave 5 | Blocked by: 2,10 | Blocks: 12
References: user order (flatline ALL in .omo/.superpowers); answered battery B7/B8; todo 2 rescue receipts; <code>.omo</code> inventory (38 files), <code>.superpowers</code> inventory (~100 files incl. sdd/youtube-duplicate-playlist-merge reports = DROP per B7)
Acceptance criteria (agent-executable): <code>Test-Path .superpowers</code> false; <code>(Get-ChildItem .omo -Recurse -File | Where-Object FullName -NotMatch 'plans.toolbox-flatline|drafts.toolbox-flatline|evidence').Count</code> == 0; <code>Test-Path 'SACD errors.md'</code> false; <code>Test-Path youtube-sync-log.md</code> false; <code>Test-Path .athena-state.json</code> false; <code>Select-String 'Branch:\*\* main' AGENTS.md</code> matches.
QA scenarios: happy - all assertions pass, commit created; failure - any rescue receipt from todo 2/10 missing -&gt; HALT before deletion. Evidence <code>.omo/evidence/task-11-toolbox-flatline.txt</code>
Commit: Y | chore: flatline agent artifacts, delete scratch, docs hygiene</p>
</li>
<li><input type="checkbox" disabled="" />
<p>12. Remove worktrees, branches, stash (post-rescue)
What to do / Must NOT do: (a) dirty pre-check FIRST (Metis): <code>git -C .worktrees/youtube-duplicate-playlist-merge status --porcelain</code> -&gt; record full output to evidence (expected: deletions/mods reflecting its OLD fully-merged state; valueless because branch has 0 unique commits), then <code>git worktree remove --force .worktrees/youtube-duplicate-playlist-merge</code>; (b) delete nested repro dir: <code>Remove-Item -Recurse -Force Toolbox-sacd-repro</code> ONLY after re-verifying todo 2 receipts (v2 spec + SacdProbe + archive all present) - its unique content is the repro branch history, preserved in <code>.git</code> until (d); the filesystem delete + <code>git worktree prune</code> in (c) is the sanctioned two-step for this already-stale admin record; (c) <code>git worktree prune</code> (clears oci-arr-repair ghost + stale Toolbox-sacd-repro admin record); (d) <code>git branch -d feat/youtube-duplicate-merge feature/process-runner-streaming oci-arr-exhaustive-repair</code> (all 0 unique commits - <code>-d</code> must succeed WITHOUT <code>-D</code>; if any refuses, HALT - that means unmerged work appeared); then <code>git branch -D sacd-deathloop-repro</code> (rescue complete, fixes committed in todo 4); (e) <code>git stash drop stash@{0}</code> (Metis-verified: stash holds only stale <code>.omo/goal</code>/<code>.omo/ulw-loop</code> modifications - files this plan deletes; obsolete by construction); (f) verify <code>git worktree list</code> shows exactly 1 line and <code>git branch</code> shows exactly <code>* master</code>. Scope = 3 non-main worktrees (2 live + 1 ghost) + 4 branches. MUST NOT use <code>-D</code> on the three merged branches; MUST NOT run before todo 11.
Parallelization: Wave 6 | Blocked by: 2,11 | Blocks: 13
References: git truth (branch -vv: 3 branches 0-unique, repro 17-unique but rescued); answered battery C1/C3 step 2; stash content = &quot;pre-rebase: .omo state files&quot;
Acceptance criteria (agent-executable): <code>(git worktree list | Measure-Object -Line).Lines</code> == 1; <code>(git branch | Measure-Object -Line).Lines</code> == 1 and matches master; <code>(git stash list | Measure-Object -Line).Lines</code> == 0; <code>Test-Path Toolbox-sacd-repro</code> false; <code>Test-Path .worktrees</code> false (or empty).
QA scenarios: happy - all counts exact; failure - <code>-d</code> refusal on a merged branch -&gt; HALT, run <code>git log master..&lt;branch&gt;</code> and report (assumption broken). Evidence <code>.omo/evidence/task-12-toolbox-flatline.txt</code>
Commit: N | -</p>
</li>
<li><input type="checkbox" disabled="" />
<p>13. Squash the 15 unpushed commits by adjacent topic (two-pass rebase, NO reordering)</p>
<blockquote>
<p>NOTE (Momus fix): the &quot;What to do&quot; below was originally a single 3306-char line. If your Read tool truncates at 2000 chars, read the full content with: <code>Get-Content .omo/plans/toolbox-flatline.md | Select-Object -Skip 180 -First 1</code> or break it into sub-lines (already done below).
What to do / Must NOT do:
(a) record safety snapshot: <code>git fetch origin</code> FIRST (Metis R3: ensure origin/master is current before rebase), then <code>git tag backup/pre-flatline-squash</code> + <code>git rev-parse HEAD</code> + <code>git log --reverse --pretty=%s origin/master..HEAD</code> (full pre-rebase subject list) to evidence;
(b) list <code>git log --reverse --pretty='%h %s' origin/master..HEAD</code>; the bottom 15 (oldest, exactly the set from todo 1b) are squash candidates; commits above them (todos 4,6,7,8,9,10,11 = up to 7) replay untouched;
(c) classify each of the 15 by CASE-INSENSITIVE subject regex (Metis-corrected: the real subjects include 'feat: add streaming and inactivity timeout to ProcessRunner', 'feat: bubble up onOutputLine in SaraconService', 'feat: stream saracon output to console and log file' which the old hyphenated lowercase regex missed) - AUDIO: <code>audio|saracon|sacd|dsd|processrunner|process-runner|stream|onoutputline|completion|logging</code>; YT: <code>google|youtube|playlist|sort|oauth</code>; DOCS: subject starts with <code>docs</code>; anything matching none, or matching both AUDIO and YT (e.g. fcbbb12 'fix(logging)... across all services'), classifies AUDIO;
(d) MECHANISM (Windows-safe, Metis block resolved): write three files under <code>.omo/evidence/</code>: <code>rebase-todo-pass1</code> (the exact desired todo: within the bottom 15, each maximal ADJACENT run of same class = <code>pick</code> first + <code>fixup</code> rest, preserving original order entirely - NO reordering, zero conflict risk; all top commits <code>pick</code>), <code>rebase-todo-pass2</code> (surviving bottom run-heads = <code>reword</code>, all top commits <code>pick</code> verbatim), and for each reword an <code>N-message.txt</code>;
create wrapper <code>seq-editor.cmd</code> containing <code>@copy /y &quot;&lt;prepared-todo&gt;&quot; &quot;%~1&quot; &gt;nul</code>; for reword, use ONE-REBASE-PER-RUN-HEAD (Metis R3: committed mechanism — simpler, deterministic, no counter file): for each run-head, create <code>seq-editor-pass2-&lt;run&gt;.cmd</code> (copies a prepared todo that marks only THAT run-head as <code>reword</code>, all else <code>pick</code>) + a fixed <code>msg-&lt;run&gt;.cmd</code> (copies a single prepared message over <code>%~1</code>); run <code>$env:GIT_SEQUENCE_EDITOR='&lt;abs&gt;\seq-editor-pass2-&lt;run&gt;.cmd'; $env:GIT_EDITOR='&lt;abs&gt;\msg-&lt;run&gt;.cmd'; git rebase -i origin/master</code> once per run-head;
then run pass 1 as <code>$env:GIT_SEQUENCE_EDITOR='&lt;abs path&gt;\seq-editor.cmd'; git rebase -i origin/master</code> (git invokes <code>&lt;editor&gt; &lt;todo-file&gt;</code>; a <code>.cmd</code> path works on Windows), and pass 2 per run-head: <code>$env:GIT_SEQUENCE_EDITOR='&lt;abs&gt;\seq-editor-pass2-&lt;run&gt;.cmd'; $env:GIT_EDITOR='&lt;abs&gt;\msg-editor-&lt;run&gt;.cmd'; git rebase -i origin/master</code>; prepared messages (bottom-to-top run order): AUDIO run(s) -&gt; <code>feat(audio): Saracon pipeline hardening - streaming, timeouts, completion detection, service-wide logging</code>; DOCS run -&gt; <code>docs(audio): SACD death-loop repro plans/specs (UTF-8 hypothesis - superseded, see correction banner)</code>; YT run(s) -&gt; <code>feat(youtube): duplicate consolidation, non-Latin sort, quota batching, OAuth timeout</code>; single-commit runs KEEP their original message (no reword);
(e) verify: <code>git log --oneline origin/master..HEAD</code> shows squashed bottom + 7 replayed tops with subjects identical to the (a) snapshot; <code>git diff backup/pre-flatline-squash HEAD</code> EMPTY - valid proof because the tag anchors the pre-rebase tree and a rebase that only fixups/rewords preserves the final tree;
(f) on ANY conflict or non-empty tree diff: <code>git rebase --abort</code> (if mid-rebase) then <code>git reset --hard backup/pre-flatline-squash</code>, HALT with report (tag stays for later retry). MUST NOT reorder commits; MUST NOT touch the 11 pushed commits below origin/master; MUST NOT proceed with non-empty tree diff; MUST NOT delete the backup tag here (todo 14e owns that).
Parallelization: Wave 6 | Blocked by: 6,7,8,9,12 | Blocks: 14
References: todo 1b unpushed list; battery §0 master commit table (topics per hash); answered battery C3; <code>git rebase</code> GIT_SEQUENCE_EDITOR/GIT_EDITOR scripting (standard git)
Acceptance criteria (agent-executable): <code>git diff backup/pre-flatline-squash HEAD --stat</code> output empty; no adjacent same-class pairs remain in bottom section (<code>git log --reverse --pretty=%s origin/master..HEAD~7</code> has no two consecutive subjects both matching AUDIO or both YT); top 7 subjects identical to evidence snapshot; <code>git status --porcelain</code> empty.
QA scenarios: happy - tree-identical diff proof + grouping assertion pass; failure - conflict/abort path executed -&gt; reset to backup tag, HALT (history untouched). Evidence <code>.omo/evidence/task-13-toolbox-flatline.txt</code>
Commit: N | (history rewrite; verified tree-identical via backup tag diff)</p>
</blockquote>
</li>
<li><input type="checkbox" disabled="" />
<p>14. Rename master -&gt; main, push, GitHub default switch, delete origin/master
What to do / Must NOT do: (a) <code>git branch -m master main</code>; (b) <code>git push -u origin main</code>; (c) switch GitHub default: first pre-check <code>Get-Command gh -ErrorAction SilentlyContinue</code> (Metis R3: upfront gh availability), then <code>gh api -X PATCH repos/Bearmancer/Toolbox -f default_branch=main</code>; (d) IF (c) succeeded (verify via <code>gh api repos/Bearmancer/Toolbox --jq .default_branch</code> == <code>main</code>): <code>git push origin --delete master</code>; ELSE (gh missing/unauthenticated/API error): record follow-up line <code>FOLLOW-UP: GitHub default branch still master; switch manually then: git push origin --delete master</code> in evidence and KEEP origin/master - do NOT delete; (e) delete backup tag only after (b) succeeds: <code>git tag -d backup/pre-flatline-squash</code>; (f) final state capture: <code>git branch -a</code>, <code>git worktree list</code>, <code>git status --porcelain</code>, <code>git log --oneline -12</code> to evidence. MUST NOT force-push; MUST NOT delete origin/master unless default switch verified.
Parallelization: Wave 6 | Blocked by: 13 | Blocks: F1-F4
References: user answer Q1 (rename main) + Q2 (push); remote = github.com/Bearmancer/Toolbox.git; origin/master currently 15 behind (pre-squash) - after push of main, origin has both refs until (d)
Acceptance criteria (agent-executable): <code>git branch --show-current</code> == main; <code>git status --porcelain</code> empty; <code>git log origin/main..main --oneline</code> empty; evidence shows default_branch==main OR the FOLLOW-UP line; backup tag gone iff push succeeded.
QA scenarios: happy - push + switch + delete verified; failure - push rejected (non-fast-forward impossible here since new ref; auth failure instead) -&gt; HALT with git error, local rename stands; gh failure -&gt; degraded path (d-ELSE) is the designed outcome, not a halt. Evidence <code>.omo/evidence/task-14-toolbox-flatline.txt</code>
Commit: N | -</p>
</li>
</ul>
<h2>Final verification wave</h2>
<blockquote>
<p>Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.</p>
</blockquote>
<ul>
<li><input type="checkbox" disabled="" /> F1. Plan compliance audit
Verify every Must-have in Scope landed with git/fs evidence: single branch <code>main</code> (<code>git branch -a</code> = main + optionally origin/master follow-up only); single worktree; <code>git status</code> clean; <code>.omo</code> contains only plan+draft+evidence; <code>.superpowers</code> absent; scratch absent; <code>docs/superpowers/specs/2026-08-09-sacd-death-loop-v2-design.md</code> + <code>docs/superpowers/audits/sacd-probe-journal.md</code> present; archive at <code>C:\Users\Lance\Dev\Old\toolbox-oci-sdd-archive\</code> non-empty; Disc-10 evidence (task-5) present; squash proof (task-13 tree-identical diff) present; no force-push occurred (<code>git reflog</code> shows no forced update of origin refs). REJECT on any miss.</li>
<li><input type="checkbox" disabled="" /> F2. Code quality review
<code>dotnet build</code> clean; diff-review todo 3's four touched files against the Desktop\Claude sources (only comment-block removal + B9/B10 deltas allowed): <code>git diff &lt;todo4-commit&gt;^ &lt;todo4-commit&gt; -- src/Services/Audio src/CLI</code>; confirm no signature drift in DsdConvertService call sites; confirm AGENTS.md rules unviolated (no pragma, no test packages added to Directory.Packages.props). REJECT on drift.</li>
<li><input type="checkbox" disabled="" /> F3. Real manual QA
Agent-executed: <code>dotnet run --project src\App -- --help</code> exits 0 and lists audio/sync/azure/dashboard command trees; <code>dotnet run --project src\App -- audio sacd-convert --help</code> exits 0 WITHOUT triggering Google OAuth (no browser/hang within 15s - the B9/Program.cs DI-skip proof); re-read <code>logs/audio.jsonl</code> Disc-10 sequence from task-5 evidence; verify <code>state/</code> file count still 298 (<code>(Get-ChildItem state -Recurse -File).Count</code> == 298 - committed, not lost). REJECT on any failure.</li>
<li><input type="checkbox" disabled="" /> F4. Scope fidelity
Verify every Must-NOT-have held: <code>C:\Users\Lance\.omo</code> + <code>C:\Users\Lance\Dev\.omo</code> untouched (mtime-scan: no files modified today except Dev.omo session json if harness wrote it); pushed 11 commits unchanged (<code>git log origin/main~&lt;N&gt;</code> tail matches pre-work <code>git log</code> snapshot from task-1); no state/ content edits (task-7/8/9 commits are the ONLY state/ touchers: <code>git log --oneline -- state</code> since backup tag == exactly those 3 subjects); no aws-translate/reader src changes (<code>git log --oneline -- src</code> since backup tag shows only todo 4/6 commits); media untouched. REJECT on any violation.</li>
</ul>
<h2>Commit strategy</h2>
<p>Final mainline commit stack above <code>origin</code>'s 11 pushed commits (bottom = oldest):</p>
<ol>
<li>~5-6 squashed topic commits (from the former 15 unpushed; adjacent-run grouping, prepared messages per todo 13e)</li>
<li><code>fix(audio): no-retry Saracon conversion, correct DFF chunk offset, skip OAuth for audio-only runs</code></li>
<li><code>chore: sync working-state source changes</code></li>
<li><code>chore(state): youtube sync state update (processed+raw)</code></li>
<li><code>chore(state): youtube deletions + merge manifests (reviewed)</code></li>
<li><code>chore(state): dashboard + lastfm state update</code></li>
<li><code>docs(audio): correct rejected UTF-8 root cause; relocate probe journal</code></li>
<li><code>chore: flatline agent artifacts, delete scratch, docs hygiene</code>
Safety: <code>backup/pre-flatline-squash</code> tag guards the rebase until push succeeds (todo 13a/14e). No force-push ever. One commit per logical unit; state split per answered battery C2.</li>
</ol>
<h2>Success criteria</h2>
<ul>
<li><code>git branch</code> == <code>* main</code> only; <code>git worktree list</code> == 1 entry; <code>git stash list</code> empty.</li>
<li><code>git status --porcelain</code> empty; <code>git log origin/main..main</code> empty (pushed).</li>
<li>GitHub default branch == main (or explicit FOLLOW-UP recorded); origin/master deleted iff switch verified.</li>
<li><code>.omo</code> == plan + draft + evidence only; <code>.superpowers</code> gone; root scratch gone; archive + docs rescues in place.</li>
<li><code>dotnet build</code> clean; <code>--help</code> runs OAuth-free; Disc-10 WAV verified &gt;= 250MB with clean log sequence and zero retries.</li>
<li><code>state/</code> intact (298 files committed); pushed history (older 11) byte-identical; tree-identical squash proven by empty diff vs backup tag.</li>
</ul>
