<h1>YouTube Architecture — Live Spec</h1>
<p><strong>Status:</strong> active reference (deduplicated, replaces <code>youtube-architecture-spec.md</code> + <code>toolbox-consolidated-spec.md</code> §3/§9 bucket excerpts)
<strong>Verified:</strong> 2026-08-18 | Live: <code>src/Services/Google/YouTube</code> (12 files, ~2678 LOC) + <code>src/Services/Google/GoogleSetup.cs</code></p>
<h2>Files (12)</h2>
<table>
<thead>
<tr>
<th>File</th>
<th>LOC</th>
<th>Role</th>
</tr>
</thead>
<tbody>
<tr>
<td><code>YouTubePlaylistOrchestrator.cs</code></td>
<td>~363</td>
<td>Top-level sync: fetch → detect → merge → process → sort; 4 <code>Execute*</code> → collapse to 2</td>
</tr>
<tr>
<td><code>YouTubeSyncProcessor.cs</code></td>
<td>~332</td>
<td>Batch <code>ProcessIfNeededAsync</code>, <code>SortPlaylistsAsync</code> (150-write budget), <code>Finalize</code>, archive</td>
</tr>
<tr>
<td><code>YouTubeSortService.cs</code></td>
<td>~369</td>
<td>LIS sort, <code>ExecuteSortPlanAsync</code> with <code>remainingBudget</code>, <code>IsQuotaOrRateLimit</code></td>
</tr>
<tr>
<td><code>YouTubePlaylistService.cs</code></td>
<td>~299</td>
<td>YouTube API: <code>PlaylistItems.Insert</code>/<code>Delete</code>/<code>List</code>, <code>PaginateAsync</code>, ETag</td>
</tr>
<tr>
<td><code>YouTubeDuplicateMerger.cs</code></td>
<td>~386</td>
<td>Live winner/loser merge, <code>YOUTUBE_MERGE_INSERT_CAP=100</code>, verification-before-delete</td>
</tr>
<tr>
<td><code>YouTubeDuplicateMergePolicy.cs</code></td>
<td>~54</td>
<td>Pure policy: grouping by <code>Title.Trim()</code> ordinal, winner select</td>
</tr>
<tr>
<td><code>YouTubeFetchState.cs</code></td>
<td>~115</td>
<td><code>YouTubeFetchState</code> + <code>PlaylistSnapshot</code> (incl. <code>LastSortMoves/Attempted/Completed</code>), <code>LoadAsync</code>/<code>SaveAsync</code> <code>state/youtube/manifest.json</code></td>
</tr>
<tr>
<td><code>YouTubeChangeDetector.cs</code></td>
<td>~51</td>
<td><code>DetectChanges</code> diff stored vs live (62 LOC in old audit → now 51)</td>
</tr>
<tr>
<td><code>YouTubePlaylistProcessor.cs</code></td>
<td>~313</td>
<td>Per-playlist: fetch videos, translate, save <code>state/youtube/{processed,raw}/</code></td>
</tr>
<tr>
<td><code>YouTubeTranslationService.cs</code></td>
<td>~231</td>
<td>Azure <code>TranslateService</code> batching</td>
</tr>
<tr>
<td><code>YouTubeVideoService.cs</code></td>
<td>~71</td>
<td>Video details</td>
</tr>
<tr>
<td><code>DashboardService.cs</code></td>
<td>126</td>
<td>Reads <code>state/youtube/</code>, builds dashboard model</td>
</tr>
</tbody>
</table>
<p><code>GoogleSetup.cs</code> (above YouTube): <code>extension AddGoogleServicesAsync()</code> — async OAuth2, registers YouTube stack + <code>DashboardService</code>.</p>
<h2>Features Preserved (F1–F10)</h2>
<p>All keep — 0 features proposed for removal. Deletes/shrinks preserve:</p>
<p>F1 state cache (<code>YouTubeFetchState</code>), F2 change detection (inline file but keep logic), F3 bulk sync (<code>Orchestrator</code>+<code>SyncProcessor</code>), F4 single-playlist by title (fix layer bypass), F5 duplicate merge (<code>DuplicateMerger</code>+<code>Policy</code>), F6 LIS sort, F7 resume incomplete sort (<code>ExecuteWithSortAsync</code>), F8 translate batch, F9 archive+incremental save, F10 <code>state/logs/youtube.jsonl</code> logging.</p>
<h2>Buckets (YouTube-Only)</h2>
<p><strong>A — DEAD (0 feature loss):</strong> <code>PlaylistNotFound/VideoNotFound</code> factories; <code>SyncResult.UpdatedSnapshots</code>; <code>SyncOutcome.IdsWithNewVideos</code>; <code>DuplicateMergeOutcome.GroupsProcessed/Deferred</code>; <code>ProcessResult.ShouldBreak</code>; <code>CombineNewAndChanged</code> inline; <code>DashboardService</code> DI singleton; <code>FetchState.ArchiveDeleted</code> duplicate.</p>
<p><strong>B — DUPE (one source):</strong> <code>StateRoot</code>/<code>Manifest</code> path×5 → <code>PathResolver</code> constant; <code>dict-filter Where(!ids).ToDictionary ×2</code> → <code>WithoutIds</code>; <code>Delete</code>/<code>Insert</code> <code>ApiError</code> copy-paste → typed <code>GoogleApiException</code> mapper.</p>
<p><strong>C — YAGNI file (inline):</strong> <code>YouTubeChangeDetector</code> 51 LOC → inline unless tested; 4 <code>Execute*</code> → 2 methods with <code>SyncOptions</code>; <code>ProcessResult.ShouldBreak</code> → <code>ErrorOr</code>.</p>
<p><strong>D — LAYER MISPLACE:</strong> title path bypasses <code>SyncProcessor</code> → delegate to <code>SyncProcessor</code>.</p>
<p><strong>E — KEEP (legit SRP):</strong> <code>SortService</code> LIS, <code>Merger</code>+<code>Policy</code>, <code>PaginateAsync</code>, processor checkpoint, translation batching.</p>
<h2>Points &amp; Overengineering (from old arch spec)</h2>
<ul>
<li>P-01 full spec 12 files not 13 — corrected.</li>
<li>P-02/<code>YT.RateLimit</code> taxonomy — see <code>error-taxonomy.md</code>.</li>
<li>P-03 wrappers/dupe — see <code>overengineering-verdict.md</code>.</li>
<li>P-06/P-07 <code>4 Execute*</code> sprawl — solo-dev: 1 interface with <code>SyncOptions</code>.</li>
<li><code>ChangeDetector</code> (1 caller), <code>ProcessResult.ShouldBreak</code> — inline unless test seam.</li>
</ul>
<h2>State Paths (Ground Truth)</h2>
<pre class="syntax-highlighting"><code><span class="text plain">state/youtube/manifest.json          # YouTubeFetchState (115 LOC)
state/youtube/processed/*.json       # 145 tracked
state/youtube/raw/*.json             # 145 tracked
state/youtube/deleted/*.json         # 3
state/youtube/merge-manifests/*.json # 1
state/logs/youtube.jsonl             # per-service log (Spec fix: PathResolver.RepoRoot/state/logs)
</span></code></pre>
