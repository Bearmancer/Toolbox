<h1>YouTube Duplicate Playlist Merge — COMPLETED</h1>
<blockquote>
<p><strong>Status: completed (2026-08-18).</strong> <code>YouTubeDuplicateMerger.cs</code> (386 LOC) + <code>YouTubeDuplicateMergePolicy.cs</code> (54 LOC) exist live. <code>YOUTUBE_MERGE_INSERT_CAP=100</code>, verification-before-delete implemented. Retained as reference; active truth is <code>youtube-architecture.md</code> + live merger files.</p>
</blockquote>
<blockquote>
<p><em>Original header:</em> For agentic workers: Use <code>subagent-driven-development</code> — retained below as spec history.</p>
</blockquote>
<p><strong>Goal:</strong> Automatically consolidate duplicate YouTube playlists during every sync by transferring live playlist items into a deterministic winner, verifying the complete video-ID union, then deleting losers safely.</p>
<p><strong>Architecture:</strong> Fetch all current playlist summaries before duplicate processing. Group playlists by trimmed exact title using <code>StringComparer.OrdinalIgnoreCase</code>. Keep the playlist with the largest reported count; break ties by oldest <code>LastUpdated</code>. Transfer missing live video IDs through <code>playlistItems.insert</code>, verify the winner contains every transferable source video, delete each loser only after verification, archive local loser state after deletion, then process and sort affected winners through the existing pipeline.</p>
<p><strong>Tech Stack:</strong> .NET 11 preview, Google.Apis.YouTube.v3, ErrorOr, Serilog through <code>Core.Telemetry</code>, Spectre.Console.Cli.</p>
<h2>Global Constraints</h2>
<ul>
<li>Automatic duplicate consolidation runs on every <code>sync youtube</code> execution.</li>
<li>Duplicate identity is <code>Title.Trim()</code> compared with <code>StringComparer.OrdinalIgnoreCase</code>.</li>
<li>Winner selection is descending <code>ReportedVideoCount</code>, then ascending <code>LastUpdated</code>, then ascending <code>PlaylistId</code> for a fully deterministic final tie-break.</li>
<li>Live YouTube items are the source of truth. Local processed JSON never authorizes deletion.</li>
<li>Transfer uses <code>playlistItems.insert</code> with <code>part=snippet</code>, <code>snippet.playlistId</code>, <code>snippet.resourceId.kind=&quot;youtube#video&quot;</code>, and <code>snippet.resourceId.videoId</code>.</li>
<li><code>playlistItems.insert</code> is non-idempotent and costs 50 quota units; re-list the winner before each run and deduplicate by <code>videoId</code>.</li>
<li>Delete a loser only after exact video-ID union verification succeeds for every transferable item in that loser.</li>
<li>Invalid or missing source <code>videoId</code>, failed insertion, failed verification, or over-cap transfer blocks that loser’s deletion.</li>
<li>Per-group insertion cap is configured by <code>YOUTUBE_MERGE_INSERT_CAP</code>; default is <code>100</code> missing videos.</li>
<li>Failed or over-cap groups remain retryable. Do not archive local files for a loser that was not deleted.</li>
<li>Archive local loser state using playlist ID in the archive filename to avoid sanitized-title collisions.</li>
<li>Move method names, item IDs, timing, and API diagnostics to Debug. Info remains concise and user-facing.</li>
<li>Suppress already-sorted playlist Info logs. Emit Info only for actual repositioning and successful duplicate mutations; use Warn/Error for deferred or failed merges.</li>
<li>Preserve existing ErrorOr railway style, cancellation propagation, one class per file, PascalCase JSON, and no inline comments.</li>
<li>Do not add test NuGet packages. Repository has no test project; use focused build checks, static pure-policy verification, and controlled live API verification.</li>
<li>Run <code>dotnet build</code> after every implementation task and before any live API execution.</li>
<li>Do not commit, push, or execute destructive live deletion during plan creation.</li>
</ul>
<h2>Current State and Gaps</h2>
<table>
<thead>
<tr>
<th>Component</th>
<th>Current state</th>
<th>Desired state</th>
<th>Gap</th>
</tr>
</thead>
<tbody>
<tr>
<td>Duplicate scope</td>
<td><code>MergeDuplicatePlaylistsAsync</code> receives only new/changed playlists</td>
<td>Scan all current summaries every sync</td>
<td>Unchanged duplicates survive indefinitely</td>
</tr>
<tr>
<td>Duplicate key</td>
<td><code>Text.SanitizeFileName(p.Title)</code></td>
<td>Trimmed exact title, ordinal case-insensitive</td>
<td>Filename sanitization can merge distinct titles</td>
</tr>
<tr>
<td>Winner</td>
<td>Largest count; API enumeration decides ties</td>
<td>Largest count, oldest timestamp, stable ID tie-break</td>
<td>Nondeterministic deletion</td>
</tr>
<tr>
<td>Content merge</td>
<td>Local processed JSON only</td>
<td>Live API item transfer and exact ID verification</td>
<td>Source-only videos can be lost before deletion</td>
</tr>
<tr>
<td>Deletion</td>
<td><code>playlists.delete</code> after local merge</td>
<td>Delete only after transfer and verification</td>
<td>Destructive ordering unsafe</td>
</tr>
<tr>
<td>Failure archive</td>
<td>Archives raw state even when delete fails</td>
<td>Archive only after successful delete</td>
<td>State can falsely imply deletion</td>
</tr>
<tr>
<td>Insert protection</td>
<td>No cap</td>
<td>Configurable cap, default 100</td>
<td>Large groups can exhaust quota</td>
</tr>
<tr>
<td>Sort Info</td>
<td>Includes method name, item count, milliseconds; logs already-sorted Info</td>
<td>Concise mutation Info; detailed Debug; no already-sorted Info</td>
<td>Default logs too noisy</td>
</tr>
</tbody>
</table>
<h2>Dependency and Subagent Order</h2>
<table>
<thead>
<tr>
<th>Task</th>
<th>Depends on</th>
<th>Domain</th>
<th>Subagent category</th>
<th>Review gate</th>
</tr>
</thead>
<tbody>
<tr>
<td>1. Playlist-item insert API</td>
<td>None</td>
<td>C# API wrapper</td>
<td><code>quick</code></td>
<td>Build, API shape review</td>
</tr>
<tr>
<td>2. Pure duplicate policy and merge planner</td>
<td>None</td>
<td>C# logic</td>
<td><code>ultrabrain</code></td>
<td>Manual policy verification, build, logic review</td>
</tr>
<tr>
<td>3. Live merger and archive safety</td>
<td>1, 2</td>
<td>API orchestration</td>
<td><code>deep</code></td>
<td>Build, failure-path review</td>
</tr>
<tr>
<td>4. Orchestrator/state integration</td>
<td>3</td>
<td>Cross-file C#</td>
<td><code>unspecified-high</code></td>
<td>Build, reference scan, state-flow review</td>
</tr>
<tr>
<td>5. Sort and duplicate logging</td>
<td>None</td>
<td>C# logging</td>
<td><code>quick</code></td>
<td>Build, output-template review</td>
</tr>
<tr>
<td>6. Full verification and controlled live run</td>
<td>1-5</td>
<td>QA/operations</td>
<td><code>deep</code></td>
<td>Build, diagnostics, API evidence</td>
</tr>
</tbody>
</table>
<p>Tasks 1, 2, and 5 are logically independent, but implementation agents must run sequentially in the current session because each task receives a separate review gate and no overlapping writes are allowed. Tasks 3 and 4 follow the critical path.</p>
<h2>Subagent-Driven Execution Protocol</h2>
<p>For each task:</p>
<ol>
<li>Record <code>BASE=$(git rev-parse HEAD)</code> before dispatch.</li>
<li>Generate a task brief containing only that task’s requirements.</li>
<li>Dispatch one fresh implementer with the task brief, exact files, constraints, and report path.</li>
<li>Implementer writes code, runs required verification, self-reviews, and reports status.</li>
<li>Inspect the diff and dispatch a separate task reviewer for spec compliance and code quality.</li>
<li>If reviewer finds Critical/Important issues, resume implementer for fix rounds 1-3; use a fresh stronger implementer for rounds 4-5. Re-review every fix.</li>
<li>Record task completion and commit range in the SDD ledger before the next task.</li>
<li>Never fix reviewer findings in the controller session.</li>
</ol>
<p>Use <code>using-git-worktrees</code> before implementation. Keep a plan-specific ledger under <code>.superpowers/sdd/&lt;plan-basename&gt;/progress.md</code>. Do not run multiple implementation agents against overlapping files.</p>
<h2>Task 1: Add Live Playlist-Item Insert API</h2>
<p><strong>Files:</strong></p>
<ul>
<li>Modify: <code>src/Services/Google/YouTube/YouTubePlaylistService.cs</code></li>
</ul>
<p><strong>Interface produced:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">Task&lt;ErrorOr&lt;string&gt;&gt; InsertPlaylistItemAsync(
    string playlistId,
    string videoId,
    CancellationToken ct
)
</span></code></pre>
<p><strong>Implementation steps:</strong></p>
<ul>
<li><input type="checkbox" disabled="" /> Add <code>InsertPlaylistItemAsync</code> after existing playlist mutation methods.</li>
<li><input type="checkbox" disabled="" /> Construct <code>PlaylistItem</code> with <code>PlaylistItemSnippet.PlaylistId</code>, <code>ResourceId.Kind = &quot;youtube#video&quot;</code>, and <code>ResourceId.VideoId</code>.</li>
<li><input type="checkbox" disabled="" /> Call <code>yt.PlaylistItems.Insert(item, &quot;snippet&quot;).ExecuteAsync(ct)</code>.</li>
<li><input type="checkbox" disabled="" /> Return inserted playlist-item ID through <code>ErrorOr&lt;string&gt;</code>.</li>
<li><input type="checkbox" disabled="" /> Follow existing <code>Telemetry.ForService</code>, <code>StartActivity</code>, cancellation, and <code>Errors.YouTube.ApiError</code> patterns.</li>
<li><input type="checkbox" disabled="" /> Keep request/response details at Debug; do not add Info noise per inserted item.</li>
<li><input type="checkbox" disabled="" /> Run <code>dotnet build</code> and <code>lsp_diagnostics</code> on the changed file.</li>
<li><input type="checkbox" disabled="" /> Commit: <code>feat(youtube): add playlist item insert API</code>.</li>
</ul>
<p><strong>Reviewer checks:</strong> request body uses video ID, not playlist-item ID; cancellation reaches API; failure returns ErrorOr; no deletion behavior is introduced.</p>
<h2>Task 2: Add Pure Duplicate Policy and Transfer Planning</h2>
<p><strong>Files:</strong></p>
<ul>
<li>Create: <code>src/Services/Google/YouTube/YouTubeDuplicateMergePolicy.cs</code></li>
</ul>
<p><strong>Interfaces produced:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">public static IReadOnlyList&lt;DuplicatePlaylistGroup&gt; FindGroups(
    IReadOnlyList&lt;PlaylistSnapshot&gt; playlists
)

public static PlaylistSnapshot SelectWinner(
IReadOnlyList&lt;PlaylistSnapshot&gt; group
)

public static TransferCandidateSet GetTransferCandidates(
IReadOnlySet&lt;string&gt; winnerVideoIds,
IReadOnlyList&lt;PlaylistItem&gt; loserItems
)

public static bool ContainsAll(
IReadOnlySet&lt;string&gt; winnerVideoIds,
IReadOnlySet&lt;string&gt; sourceVideoIds
)
</span></code></pre>

<p><code>DuplicatePlaylistGroup</code> is <code>record struct DuplicatePlaylistGroup(string Key, IReadOnlyList&lt;PlaylistSnapshot&gt; Playlists)</code>. <code>TransferCandidateSet</code> is <code>record struct TransferCandidateSet(IReadOnlyList&lt;string&gt; MissingVideoIds, bool HasInvalidItems)</code>.</p>
<p><strong>Policy steps:</strong></p>
<ul>
<li><input type="checkbox" disabled="" /> Group by <code>playlist.Title.Trim()</code> using <code>StringComparer.OrdinalIgnoreCase</code>.</li>
<li><input type="checkbox" disabled="" /> Ignore singleton groups.</li>
<li><input type="checkbox" disabled="" /> Select winner by <code>ReportedVideoCount</code> descending, <code>LastUpdated</code> ascending, <code>PlaylistId</code> ascending.</li>
<li><input type="checkbox" disabled="" /> Extract only non-empty <code>Snippet.ResourceId.VideoId</code> values. Preserve source order, remove duplicates, and never insert a video already present in winner.</li>
<li><input type="checkbox" disabled="" /> Return <code>HasInvalidItems = true</code> when any source item has no usable video ID. Caller must block deletion for that source; it must not silently discard the item.</li>
<li><input type="checkbox" disabled="" /> Make verification set-based, not count-only. <code>winnerVideoIds</code> must contain every transferable source ID; count is only supplementary telemetry.</li>
<li><input type="checkbox" disabled="" /> Keep policy deterministic and side-effect free so it can be checked without Google credentials.</li>
<li><input type="checkbox" disabled="" /> Write a temporary standalone harness at <code>.superpowers/sdd/&lt;plan-basename&gt;/YouTubeDuplicateMergePolicyVerification.cs</code> with <code>Main()</code> and a <code>#:project</code> reference to <code>src/Services/Google/Google.csproj</code>. Run <code>dotnet run --file .superpowers/sdd/&lt;plan-basename&gt;/YouTubeDuplicateMergePolicyVerification.cs</code>; verify no duplicates, case/whitespace duplicate, punctuation-distinct titles, largest winner, oldest equal-count winner, duplicate source IDs, empty IDs, and cap boundary values. Delete harness after the task review.</li>
<li><input type="checkbox" disabled="" /> Run <code>dotnet build</code> and <code>lsp_diagnostics</code>.</li>
<li><input type="checkbox" disabled="" /> Commit: <code>feat(youtube): add deterministic duplicate merge policy</code>.</li>
</ul>
<p><strong>Reviewer checks:</strong> sanitized filenames are absent from identity logic; equal-size selection is stable; missing IDs cannot silently authorize deletion; policy does not call APIs or mutate files.</p>
<h2>Task 3: Implement Live Merger and Safe Archive Ordering</h2>
<p><strong>Files:</strong></p>
<ul>
<li>Create: <code>src/Services/Google/YouTube/YouTubeDuplicateMerger.cs</code></li>
</ul>
<p><strong>Interface produced:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">Task&lt;DuplicateMergeOutcome&gt; MergeDuplicateGroupsAsync(
    IReadOnlyList&lt;PlaylistSnapshot&gt; allCurrentPlaylists,
    CancellationToken ct
)
</span></code></pre>
<p><code>DuplicateMergeOutcome</code> is:</p>
<pre class="syntax-highlighting"><code><span class="text plain">public readonly record struct DuplicateMergeOutcome(
    IReadOnlyList&lt;PlaylistSnapshot&gt; Survivors,
    IReadOnlyList&lt;PlaylistSnapshot&gt; RemovedLosers,
    IReadOnlySet&lt;string&gt; WinnersRequiringProcessing,
    int GroupsProcessed,
    int GroupsDeferred
)
</span></code></pre>
<p>The merger owns duplicate-delete archive creation. <code>YouTubeSyncProcessor</code> keeps only ordinary deleted-playlist archival.</p>
<p><strong>Implementation steps:</strong></p>
<ul>
<li><input type="checkbox" disabled="" /> Read <code>YOUTUBE_MERGE_INSERT_CAP</code>; use <code>100</code> when absent, invalid, or non-positive; log invalid configuration at Warn.</li>
<li><input type="checkbox" disabled="" /> Process groups serially to avoid concurrent mutations and quota spikes.</li>
<li><input type="checkbox" disabled="" /> For each group, list complete winner items and complete loser items through the existing paginated <code>GetPlaylistItemsAsync</code> method.</li>
<li><input type="checkbox" disabled="" /> Build target video-ID set. For each loser, reject deletion eligibility if any item lacks a video ID.</li>
<li><input type="checkbox" disabled="" /> Build the complete missing-ID list across all losers before inserting anything. If missing count exceeds cap, log Warn and leave every playlist intact.</li>
<li><input type="checkbox" disabled="" /> Insert missing IDs through <code>InsertPlaylistItemAsync</code>, one at a time, with cancellation checks. If any insert fails, stop the group and delete nothing.</li>
<li><input type="checkbox" disabled="" /> Re-list winner after inserts. Verify every transferable source ID is present. Do not rely solely on <code>ReportedVideoCount</code> because item-count metadata can lag.</li>
<li><input type="checkbox" disabled="" /> Persist a deletion archive manifest containing winner ID, loser ID, source item IDs/video IDs, transfer counts, and timestamp before deletion. Use playlist ID in archive paths.</li>
<li><input type="checkbox" disabled="" /> Delete losers only after union verification. Because YouTube has no transaction, delete sequentially and record any loser delete failure without deleting that loser’s local archive.</li>
<li><input type="checkbox" disabled="" /> Archive loser processed/raw files only after the corresponding <code>playlists.delete</code> succeeds.</li>
<li><input type="checkbox" disabled="" /> If one loser deletion fails after another succeeds, report partial group completion; remaining loser stays retryable and winner remains authoritative.</li>
<li><input type="checkbox" disabled="" /> Return winner IDs needing reprocessing when inserts occurred. Return no winner-processing requirement when loser content was already a verified subset.</li>
<li><input type="checkbox" disabled="" /> Never call old local-JSON merge logic as a substitute for live transfer.</li>
<li><input type="checkbox" disabled="" /> Run build and diagnostics on every changed file.</li>
<li><input type="checkbox" disabled="" /> Commit: <code>feat(youtube): merge duplicate playlists through live API</code>.</li>
</ul>
<p><strong>Reviewer checks:</strong> no source deletion before exact union verification; over-cap path performs zero inserts; partial insert path performs zero deletes; archive occurs after delete only; rerun after partial inserts skips already-present IDs.</p>
<h2>Task 4: Wire All-Playlist Consolidation and State Flow</h2>
<p><strong>Files:</strong></p>
<ul>
<li>Modify: <code>src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs</code></li>
<li>Modify: <code>src/Services/Google/YouTube/YouTubeSyncProcessor.cs</code></li>
<li>Modify: <code>src/Services/Google/GoogleSetup.cs</code></li>
</ul>
<p><strong>Implementation steps:</strong></p>
<ul>
<li><input type="checkbox" disabled="" /> Register <code>YouTubeDuplicateMerger</code> in the existing Google service setup.</li>
<li><input type="checkbox" disabled="" /> Extend <code>SyncContext</code> so merge stage can access all current summaries, not only <code>CombineNewAndChanged</code> output.</li>
<li><input type="checkbox" disabled="" /> Invoke duplicate consolidation for all current playlists after summaries are fetched and before normal processing.</li>
<li><input type="checkbox" disabled="" /> Remove deleted loser IDs from stored manifest snapshots and all change lists before <code>ProcessIfNeededAsync</code> and <code>Finalize</code> calculate counts.</li>
<li><input type="checkbox" disabled="" /> Add affected winner IDs to processing when live inserts changed winner contents, including winners previously classified unchanged.</li>
<li><input type="checkbox" disabled="" /> Refresh winner snapshot after merge where needed; do not leave stale <code>ReportedVideoCount</code> or <code>ETag</code> in state.</li>
<li><input type="checkbox" disabled="" /> Preserve normal new/changed processing for non-duplicate playlists.</li>
<li><input type="checkbox" disabled="" /> Remove obsolete <code>MergeDuplicatePlaylistsAsync</code>, <code>MergeProcessedVideosAsync</code>, and any local-only duplicate merge path after the new merger is wired.</li>
<li><input type="checkbox" disabled="" /> Keep archive behavior for ordinary YouTube-deleted playlists separate from duplicate-delete archives.</li>
<li><input type="checkbox" disabled="" /> Run <code>dotnet build</code>, <code>lsp_diagnostics</code>, and a workspace search confirming removed methods have no callers.</li>
<li><input type="checkbox" disabled="" /> Commit: <code>feat(youtube): run duplicate consolidation across all playlists</code>.</li>
</ul>
<p><strong>Reviewer checks:</strong> unchanged duplicate groups are detected; deleted losers do not remain in manifest or final counters; merged winners are processed; no duplicate group can be processed twice in one sync due stale context.</p>
<h2>Task 5: Refactor Sort and Duplicate Logging</h2>
<p><strong>Files:</strong></p>
<ul>
<li>Modify: <code>src/Services/Google/YouTube/YouTubeSortService.cs</code></li>
</ul>
<p><strong>Implementation steps:</strong></p>
<ul>
<li><input type="checkbox" disabled="" /> Move existing already-sorted summary from Info to Debug, retaining item count and elapsed milliseconds there.</li>
<li><input type="checkbox" disabled="" /> Change repositioning Info to omit <code>YouTube.SortPlaylist</code>, method wording, and milliseconds. Use a concise template equivalent to <code>{PlaylistName} — {Repositioned}/{ItemCount} repositioned</code>.</li>
<li><input type="checkbox" disabled="" /> Keep pass timings, method names, item IDs, and API timings at Debug/Verbose.</li>
<li><input type="checkbox" disabled="" /> Emit duplicate detection and successful deletion summaries at Info without method names or timing.</li>
<li><input type="checkbox" disabled="" /> Emit deferred cap groups at Warn; failed transfer/verification/delete paths at Error or Warn according to existing Telemetry conventions.</li>
<li><input type="checkbox" disabled="" /> Run build and diagnostics.</li>
<li><input type="checkbox" disabled="" /> Commit: <code>refactor(youtube): reduce default playlist logging noise</code>.</li>
</ul>
<p><strong>Reviewer checks:</strong> no already-sorted Info output; mutation Info contains playlist/user outcome only; detailed diagnostics remain available at Debug; no sorting behavior changes.</p>
<h2>Task 6: Full Verification and Controlled Live Run</h2>
<p><strong>Files:</strong> None for verification; do not alter state until preflight is captured.</p>
<p><strong>Implementation steps:</strong></p>
<ul>
<li><input type="checkbox" disabled="" /> Run <code>dotnet build</code> on the full solution. Require exit code 0 and no warnings/errors.</li>
<li><input type="checkbox" disabled="" /> Run <code>lsp_diagnostics</code> on every changed C# file.</li>
<li><input type="checkbox" disabled="" /> Inspect <code>git diff</code>, <code>git status</code>, and recent commits. Confirm only planned files changed.</li>
<li><input type="checkbox" disabled="" /> Capture a preflight export of duplicate candidate playlist summaries and live item video IDs before the first destructive run.</li>
<li><input type="checkbox" disabled="" /> Set <code>YOUTUBE_MERGE_INSERT_CAP=100</code> explicitly for first live run.</li>
<li><input type="checkbox" disabled="" /> Run <code>dotnet run --project src\App -- sync youtube</code> once.</li>
<li><input type="checkbox" disabled="" /> Verify logs show duplicate detection, transfer counts, exact verification, deletion only after verification, concise sort Info, and no already-sorted Info.</li>
<li><input type="checkbox" disabled="" /> Verify <code>state/youtube/manifest.json</code>: loser IDs absent, winner ID present with refreshed count/ETag.</li>
<li><input type="checkbox" disabled="" /> Verify <code>state/youtube/deleted/</code>: loser archive manifest and local files exist only for successfully deleted losers.</li>
<li><input type="checkbox" disabled="" /> Verify live YouTube: winner contains the union of all preflight source/winner video IDs; loser no longer exists; no duplicate video IDs were introduced.</li>
<li><input type="checkbox" disabled="" /> Run sync a second time. Expected: no repeat inserts/deletes for successfully consolidated groups; deferred/failed groups retry with existing target IDs skipped.</li>
<li><input type="checkbox" disabled="" /> Exercise cap behavior using a known group requiring more than 100 inserts: expect Warn, zero deletion, loser remains live.</li>
<li><input type="checkbox" disabled="" /> Exercise failure behavior only with a controlled invalid/non-transferable source item if available: expect no deletion and retryable state.</li>
<li><input type="checkbox" disabled="" /> Do not claim completion until all evidence is recorded in the SDD ledger.</li>
</ul>
<p><strong>Live-run rollback reality:</strong> YouTube playlist deletion is not transactional. Local archives preserve IDs and metadata for manual recreation, but cannot restore a deleted playlist automatically without additional API inserts. Do not run live deletion against production duplicates until the preflight export is complete.</p>
<h2>Commit and Review Strategy</h2>
<table>
<thead>
<tr>
<th>Commit</th>
<th>Scope</th>
<th>Reviewer focus</th>
</tr>
</thead>
<tbody>
<tr>
<td>1</td>
<td>Playlist insert API</td>
<td>Request shape, ErrorOr, cancellation</td>
</tr>
<tr>
<td>2</td>
<td>Pure duplicate policy</td>
<td>Identity, deterministic winner, ID-set correctness</td>
</tr>
<tr>
<td>3</td>
<td>Live merger</td>
<td>Cap, partial failures, verification-before-delete, archives</td>
</tr>
<tr>
<td>4</td>
<td>Orchestrator/state</td>
<td>All-playlist scope, survivor state, affected winners</td>
</tr>
<tr>
<td>5</td>
<td>Logging</td>
<td>Info/Debug separation, no behavior regression</td>
</tr>
</tbody>
</table>
<p>Every commit receives a task-scoped review package. After all tasks, dispatch one broad whole-branch reviewer against the merge base. Any final Critical/Important finding gets one fix subagent and one scoped re-review; residual load-bearing findings block handoff.</p>
<h2>Success Criteria</h2>
<ul>
<li>All current playlists scanned every sync.</li>
<li>Only trimmed exact case-insensitive title matches become duplicate groups.</li>
<li>Largest playlist survives; equal-size ties keep oldest playlist.</li>
<li>Live missing items transfer through YouTube API.</li>
<li>Exact source video-ID union verified before any loser deletion.</li>
<li>Over-cap and failed groups remain intact and retryable.</li>
<li>Local archives created only after successful deletion.</li>
<li>Manifest and processing pipeline reflect survivors and affected winners.</li>
<li>Default Info logs contain no method names, timing, or already-sorted lines.</li>
<li><code>dotnet build</code> exits 0 with zero warnings/errors.</li>
<li>Second sync is idempotent for successfully merged groups.</li>
</ul>
