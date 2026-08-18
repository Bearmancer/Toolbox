<h1>Error Taxonomy — Speculative vs Dead vs Logical-Error</h1>
<p><strong>Source:</strong> <code>toolbox-consolidated-spec.md</code> §2 (error producer→consumer map)
<strong>Verified:</strong> 2026-08-18 | Live: <code>src/Core/Errors.cs</code> (18 factories) + <code>src/Services/Google/YouTube/YouTubeSortService.cs</code> + <code>src/Services/Google/YouTube/YouTubeSyncProcessor.cs</code></p>
<h2>Producer → Consumer Map</h2>
<table>
<thead>
<tr>
<th>Code</th>
<th>Consumer</th>
<th>Producer</th>
<th>Status</th>
</tr>
</thead>
<tbody>
<tr>
<td><code>YT.RateLimit</code></td>
<td><code>SyncProcessor:79</code></td>
<td><strong>0 producers</strong></td>
<td><strong>logical-error</strong> — fix producer mappers</td>
</tr>
<tr>
<td><code>YT.QuotaExceeded</code></td>
<td><code>SyncProcessor:79</code> not checked</td>
<td><code>SortService:312</code></td>
<td><strong>logical-error</strong> — fix consumer check</td>
</tr>
<tr>
<td><code>YT.PlaylistNotFound</code></td>
<td><strong>0 consumers</strong></td>
<td><strong>0 producers</strong></td>
<td><strong>dead</strong> — delete</td>
</tr>
<tr>
<td><code>YT.VideoNotFound</code></td>
<td><strong>0 consumers</strong></td>
<td><strong>0 producers</strong></td>
<td><strong>dead</strong> — delete</td>
</tr>
<tr>
<td><code>Azure.AuthFailed</code></td>
<td><code>SyncProcessor:88</code></td>
<td><strong>0 producers</strong></td>
<td><strong>logical-error</strong> — map at Translate boundary</td>
</tr>
<tr>
<td><code>Azure.RateLimit</code></td>
<td><code>SyncProcessor:79</code></td>
<td><strong>0 producers</strong></td>
<td><strong>logical-error</strong> — map at Translate boundary</td>
</tr>
<tr>
<td><code>Azure.ServiceUnavailable</code></td>
<td><strong>0 consumers</strong></td>
<td><strong>0 producers</strong></td>
<td><strong>dead</strong> — delete</td>
</tr>
</tbody>
</table>
<p>Live quota handling exists (<code>IsQuotaOrRateLimit</code> in <code>YouTubeSortService</code>, <code>maxWritesPerRun=150</code> budget in <code>YouTubeSyncProcessor</code>, <code>remainingBudget</code> param) — this table tracks <em>typed ErrorOr codes</em>, not exception-guard behavior. <code>YT.RateLimit</code> vs generic <code>ApiError</code> is a code-routing bug, not a missing guard.</p>
<h2>Fix Spec</h2>
<ul>
<li><code>YouTubePlaylistService.Delete/Insert/Fetch</code>: catch <code>GoogleApiException</code> → typed mapper (429→<code>RateLimit</code>, 403+quota→<code>QuotaExceeded</code>, 404→<code>PlaylistNotFound</code>, else→<code>ApiError</code>).</li>
<li><code>SyncProcessor:79</code>: add <code>&quot;YT.QuotaExceeded&quot;</code> to check.</li>
<li><code>TranslateService</code>: catch <code>HttpRequestException</code> 429→<code>Azure.RateLimit</code>, 401/403→<code>Azure.AuthFailed</code>.</li>
</ul>
<h2>Related Dead Codes</h2>
<p><code>Errors.General.Unexpected/Internal</code>, <code>Errors.Validation.RequiredField</code>, <code>Text.Has/StartsWith</code>, <code>TranslateCommand --from</code>, <code>Serilog.Sinks.Console</code> PackageVersion — see <code>dead-code-catalog.md</code>.</p>
