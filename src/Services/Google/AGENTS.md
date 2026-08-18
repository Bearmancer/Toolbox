<h1>Google Services</h1>
<p>YouTube Data API v3 sync pipeline. Depends on <code>Services.Azure.TranslateService</code>.</p>
<h2>STRUCTURE</h2>
<pre class="syntax-highlighting"><code><span class="text plain">Google/
├── GoogleSetup.cs                       # DI: extension AddGoogleServicesAsync(), OAuth2 (FileDataStore state/google-auth, scope Youtube)
└── YouTube/                             # 12 files
    ├── YouTubePlaylistOrchestrator.cs   # Top-level sync: fetch → detect → merge → process → sort (ExecuteAsync/ExecuteWithSortAsync)
    ├── YouTubePlaylistProcessor.cs      # Per-playlist: fetch videos, translate, save processed/raw JSON
    ├── YouTubePlaylistService.cs        # YouTube API: list playlists, get items, insert/delete
    ├── YouTubeVideoService.cs           # YouTube API: video details (batch fetch)
    ├── YouTubeSortService.cs            # Sort by translated title: LIS + budgeted moves (IsQuotaOrRateLimit)
    ├── YouTubeTranslationService.cs     # Translates titles via Azure TranslateService
    ├── YouTubeChangeDetector.cs         # Diff stored vs current: DetectChanges() → New/Changed/Deleted
    ├── YouTubeFetchState.cs             # Manifest state/youtube/manifest.json; PlaylistSnapshot + LastSortMoves/Attempted/Completed
    ├── YouTubeSyncProcessor.cs          # Batch orchestration: ProcessPlaylistsAsync, SortPlaylistsAsync (maxWritesPerRun 150), ArchiveDeletedPlaylists
    ├── DashboardService.cs              # Reads state/youtube/, builds dashboard model
    ├── YouTubeDuplicateMergePolicy.cs   # Policy: FindGroups (title-normalized), SelectWinner, GetTransferCandidates
    └── YouTubeDuplicateMerger.cs        # Exec merges: insert winners, delete losers, archive merge-manifests
</span></code></pre>
<h2>WHERE TO LOOK</h2>
<table>
<thead>
<tr>
<th>Task</th>
<th>File</th>
<th>Notes</th>
</tr>
</thead>
<tbody>
<tr>
<td>Change sync flow</td>
<td><code>YouTubePlaylistOrchestrator.cs</code></td>
<td><code>ExecuteCoreAsync()</code> ThenAsync chain → Merge → Process → Sort</td>
</tr>
<tr>
<td>Change per-playlist logic</td>
<td><code>YouTubePlaylistProcessor.cs</code></td>
<td><code>ProcessPlaylistAsync()</code> + <code>RefreshLocalStateAsync()</code></td>
</tr>
<tr>
<td>Add YouTube API call</td>
<td><code>YouTubePlaylistService.cs</code> / <code>YouTubeVideoService.cs</code></td>
<td>Wrap response; handle quota via <code>IsQuotaOrRateLimit</code></td>
</tr>
<tr>
<td>Change translation</td>
<td><code>YouTubeTranslationService.cs</code></td>
<td>Calls <code>TranslateService.TranslateBatchAsync()</code></td>
</tr>
<tr>
<td>Change state schema</td>
<td><code>YouTubeFetchState.cs</code></td>
<td><code>PlaylistSnapshot</code>, <code>LoadAsync</code>/<code>SaveAsync</code>, sort fields</td>
</tr>
<tr>
<td>Build dashboard data</td>
<td><code>DashboardService.cs</code></td>
<td>Reads <code>state/youtube/</code>, returns dashboard model</td>
</tr>
</tbody>
</table>
<h2>CONVENTIONS</h2>
<ul>
<li><strong>Orchestrator pattern:</strong> <code>YouTubePlaylistOrchestrator</code> owns pipeline. Thin CLI delegates to <code>ExecuteAsync</code>/<code>ExecuteWithSortAsync</code>.</li>
<li><strong>ErrorOr chain:</strong> <code>LoadAsync().ThenAsync(Fetch+Detect).ThenAsync(Merge).ThenAsync(Process).Then(Finalize)</code> — breaks on first error.</li>
<li><strong>State persistence:</strong> <code>YouTubeFetchState.LoadAsync</code>/<code>SaveAsync</code> JSON to <code>state/youtube/manifest.json</code> (HmsTimeSpanConverter, ArchiveDeleted).</li>
<li><strong>Change detection:</strong> <code>YouTubeChangeDetector.DetectChanges(current, stored)</code> → New/Changed/Deleted; deleted archived to <code>state/youtube/deleted/</code>.</li>
<li><strong>Cross-service:</strong> <code>YouTubeTranslationService</code> → <code>Services.Azure.TranslateService</code> directly (no CLI indirection).</li>
<li><strong>Quota handling:</strong> <code>YouTubeSyncProcessor</code> caps <code>maxWritesPerRun=150</code> (50 units/write); <code>YouTubeSortService.IsQuotaOrRateLimit</code> checks 403/429 + &quot;quota&quot; string; budget passed per-pass, stops on exhaustion.</li>
</ul>
<h2>ANTI-PATTERNS</h2>
<ul>
<li><strong>NEVER</strong> put sync logic in CLI. Orchestrator owns everything.</li>
<li><strong>NEVER</strong> bypass Orchestrator to call <code>YouTubePlaylistService</code> from CLI.</li>
</ul>
