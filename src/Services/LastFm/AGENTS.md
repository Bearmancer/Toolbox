<h1>AGENTS.md — Services/LastFm</h1>
<p>Last.fm API client + sync orchestrator. Persists scrobbles to <code>state/lastfm/scrobbles.json</code>.</p>
<h2>STRUCTURE</h2>
<pre class="syntax-highlighting"><code><span class="text plain">LastFm/
├── LastFmSetup.cs            # DI: reads LASTFM_API_KEY + LASTFM_USERNAME from env, registers singletons
├── LastFmApiClient.cs        # HTTP layer: BuildFetchUrl, request execution, JSON parsing, ClassifyError
├── LastFmService.cs          # Business logic: FetchRecentTracksAsync pagination, 3x retry, 200ms rate limit
├── LastFmSyncOrchestrator.cs # Sync flow: load state → filter → fetch → merge → save (SyncResult)
└── LastFmState.cs            # Persistence: LoadScrobblesAsync, SaveScrobblesAsync, MergeScrobbles (PlayedAt dedup)
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
<td>Add API endpoint</td>
<td><code>LastFmApiClient.cs</code></td>
<td>Add to <code>BuildFetchUrl</code>, chain in <code>FetchPageCoreAsync</code></td>
</tr>
<tr>
<td>Change fetch</td>
<td><code>LastFmService.cs</code></td>
<td><code>FetchRecentTracksAsync</code> controls pagination + stop condition</td>
</tr>
<tr>
<td>Modify sync</td>
<td><code>LastFmSyncOrchestrator.cs</code></td>
<td>Load, filter, merge, save. Returns <code>SyncResult</code> record</td>
</tr>
<tr>
<td>Persistence</td>
<td><code>LastFmState.cs</code></td>
<td><code>scrobbles.json</code> only. <code>JsonSerializerOptions { WriteIndented = true }</code></td>
</tr>
<tr>
<td>Error codes</td>
<td><code>LastFmApiClient.cs</code></td>
<td><code>ClassifyError</code> maps codes → <code>Retryable/Fatal/Permanent</code></td>
</tr>
<tr>
<td>Env var</td>
<td><code>LastFmSetup.cs</code></td>
<td>Read in <code>AddLastFmServices()</code>, throw <code>InvalidOperationException</code> if missing</td>
</tr>
</tbody>
</table>
<h2>CONVENTIONS</h2>
<ul>
<li><strong>Auth:</strong> <code>LASTFM_API_KEY</code> + <code>LASTFM_USERNAME</code> via env. Never hardcode.</li>
<li><strong>Error flow:</strong> <code>ClassifyError</code> maps Last.fm numeric error codes to <code>LastFmErrorType</code>; <code>ParseJsonResponse</code> returns <code>Errors.LastFm.Retryable</code> or <code>Errors.LastFm.ApiError</code> (both <code>ErrorOr</code>). HTTP 429 returns <code>Errors.LastFm.RateLimited</code> from <code>ExecuteHttpRequestAsync</code>. <code>FetchPageAsync</code> in <code>LastFmService.cs</code> branches on 429/503 to decide retry vs. propagate.</li>
<li><strong>Rate limit:</strong> 200ms between requests. HTTP 429 → <code>Retry-After</code> header, fallback 5s.</li>
<li><strong>Retry:</strong> 3 attempts, exponential backoff. Only on <code>Retryable</code> + <code>HttpRequestException</code>.</li>
<li><strong>Merge:</strong> <code>MergeScrobbles</code> dedups by <code>PlayedAt</code> (GroupBy, take First), sorted descending.</li>
<li><strong>JSON:</strong> PascalCase properties. No <code>PropertyNamingPolicy</code>.</li>
<li><strong>Display:</strong> <code>LastFmScrobble.Date</code> returns IST (UTC+5:30) <code>yyyy-MM-dd HH:mm</code>.</li>
<li><strong>Telemetry:</strong> <code>Telemetry.ForService(ServiceName.LastFm)</code> per operation.</li>
</ul>
<h2>ANTI-PATTERNS</h2>
<ul>
<li><strong>NEVER</strong> hardcode API keys. Env vars only.</li>
<li><strong>NEVER</strong> bypass <code>WaitForRateLimit</code> (200ms) before each request.</li>
<li><strong>NEVER</strong> write <code>state/lastfm/scrobbles.json</code> directly. Use <code>LastFmState.SaveScrobblesAsync</code>.</li>
<li><strong>NEVER</strong> discard an <code>ErrorOr</code> result without logging. <code>FetchPageAsync</code> logs retries via <code>Telemetry.Warn</code>; terminal failures are logged by <code>FetchRecentTracksAsync</code> via <code>Telemetry.Error</code> when it receives the errored result — maintain that expectation.</li>
<li><strong>NEVER</strong> assume single track shape. Response <code>track</code> can be array or single object.</li>
</ul>
