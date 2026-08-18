<h1>YouTube Data Seams — Produced ↔ Consumed Gap Map</h1>
<p><strong>Source:</strong> <code>toolbox-consolidated-spec.md</code> §1 (N-01..N-05, S-09..S-17, S-01..S-08)
<strong>Verified:</strong> 2026-08-18 | Live: <code>src/Services/Google/YouTube/*</code> (12 files, ~2678 LOC) + <code>src/Core/Errors.cs</code> + <code>src/Services/Google/YouTube/YouTubeFetchState.cs</code></p>
<h2>Sort/Plan Seams (N-01..N-05)</h2>
<table>
<thead>
<tr>
<th>ID</th>
<th>Data</th>
<th>Verdict</th>
<th>Evidence</th>
</tr>
</thead>
<tbody>
<tr>
<td>N-01</td>
<td><code>SortResult.LastSortCompleted</code></td>
<td>consumed</td>
<td><code>SortService</code> → <code>SyncProcessor</code> resume</td>
</tr>
<tr>
<td>N-02</td>
<td><code>SortPlan TotalItems/LisSize</code></td>
<td>unconsumed</td>
<td>produced in <code>ComputeSortPlan</code>, only logged</td>
</tr>
<tr>
<td>N-03</td>
<td><code>SortPassResult Failures</code></td>
<td>logical-error</td>
<td>collapsed to <code>ApiError</code> string, count lost</td>
</tr>
<tr>
<td>N-04</td>
<td><code>PlaylistUpdate Item+NewPosition</code></td>
<td>consumed</td>
<td><code>SortService:219→:268</code></td>
</tr>
<tr>
<td>N-05</td>
<td><code>Translation DetectedLanguage hi</code></td>
<td>logical-error</td>
<td>transliterated <code>hi</code> counts as translated <code>en</code> — over-count</td>
</tr>
</tbody>
</table>
<h2>Dashboard/Sort/Video/Merge Seams (S-09..S-17)</h2>
<table>
<thead>
<tr>
<th>ID</th>
<th>Data</th>
<th>Verdict</th>
<th>Evidence</th>
</tr>
</thead>
<tbody>
<tr>
<td>S-09</td>
<td><code>SyncResult.SkippedVideos</code></td>
<td>unconsumed</td>
<td>logged in Finalize, never read by CLI</td>
</tr>
<tr>
<td>S-10</td>
<td><code>ChangeDetectionResult.UnchangedPlaylists</code></td>
<td>unconsumed</td>
<td>never iterated outside detector</td>
</tr>
<tr>
<td>S-11</td>
<td><code>SortStatistics Attempted/Modified</code></td>
<td>logical-error</td>
<td>logged then discarded, CLI never sees</td>
</tr>
<tr>
<td>S-12</td>
<td><code>SyncResult.TotalVideos vs ProcessedIds.Count</code></td>
<td>dupe</td>
<td>keep one metric</td>
</tr>
<tr>
<td>S-13</td>
<td><code>DashboardService.reverseLookup TryAdd</code></td>
<td>logical-error</td>
<td>sanitized Title collision drops data — key by <code>PlaylistId</code></td>
</tr>
<tr>
<td>S-14</td>
<td><code>YouTubeVideo.Description</code></td>
<td>consumed</td>
<td>display off, search on, ~1.5 MB bloat — keep for search</td>
</tr>
<tr>
<td>S-15</td>
<td><code>YouTubeFetchState.LastChecked/LastUpdated</code> top-level</td>
<td>unconsumed</td>
<td>written 5 places, never read — dead or missing throttle</td>
</tr>
<tr>
<td>S-16</td>
<td><code>PlaylistSnapshot.LastChecked</code></td>
<td>unconsumed</td>
<td>never read (ETag+count gates cache)</td>
</tr>
<tr>
<td>S-17</td>
<td><code>HmsTimeSpanConverter</code> scope</td>
<td>unused</td>
<td>Merger isolated <code>JsonOptions</code> — reuse <code>YouTubeFetchState.JsonOptions</code></td>
</tr>
</tbody>
</table>
<h2>Fresh Unconsumed Seams (S-01..S-08) — dupes flagged</h2>
<table>
<thead>
<tr>
<th>ID</th>
<th>Data</th>
<th>Verdict</th>
</tr>
</thead>
<tbody>
<tr>
<td>S-01</td>
<td><code>TranslatedTitle / DetectedLanguage</code></td>
<td>unconsumed — dashboard drops language</td>
</tr>
<tr>
<td>S-02</td>
<td><code>YouTubeVideo.Description</code></td>
<td>duplicate S-14</td>
</tr>
<tr>
<td>S-03</td>
<td><code>YouTubeVideo.Duration</code></td>
<td>overengineering — stored hh:mm:ss, reformatted same string</td>
</tr>
<tr>
<td>S-04</td>
<td><code>PlaylistSnapshot.ReportedVideoCount</code></td>
<td>consumed — detector logs delta</td>
</tr>
<tr>
<td>S-05</td>
<td><code>SortStatistics</code></td>
<td>duplicate S-11</td>
</tr>
<tr>
<td>S-06</td>
<td><code>SyncResult TotalVideos/SkippedVideos</code></td>
<td>duplicate S-09/S-12</td>
</tr>
<tr>
<td>S-07</td>
<td><code>ChangeDetectionResult UnchangedPlaylists</code></td>
<td>duplicate S-10</td>
</tr>
<tr>
<td>S-08</td>
<td><code>DashboardData PlaylistCount/VideoCount</code></td>
<td>unconsumed — scaffolding dead</td>
</tr>
</tbody>
</table>
<h2>Notes</h2>
<ul>
<li>Live YouTube count is 12 files (not 13): <code>DashboardService</code>, <code>YouTubeChangeDetector</code>, <code>YouTubeDuplicateMergePolicy</code>, <code>YouTubeDuplicateMerger</code>, <code>YouTubeFetchState</code>, <code>YouTubePlaylistOrchestrator</code>, <code>YouTubePlaylistProcessor</code>, <code>YouTubePlaylistService</code>, <code>YouTubeSortService</code>, <code>YouTubeSyncProcessor</code>, <code>YouTubeTranslationService</code>, <code>YouTubeVideoService</code> (+ <code>GoogleSetup.cs</code> above).</li>
<li><code>YouTubeFetchState</code> now has <code>LastSortMoves/LastSortAttempted/LastSortCompleted</code> — seams S-15/S-16 pre-date churn fix.</li>
</ul>
