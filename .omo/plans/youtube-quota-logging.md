<h1>Plan: YouTube Quota + Logging Fixes — COMPLETED</h1>
<p><strong>Status:</strong> completed (2026-08-18)<br />
<strong>Intent:</strong> CLEAR<br />
<strong>Date:</strong> 2026-08-13</p>
<blockquote>
<p><strong>Implemented.</strong> <code>Telemetry.cs:30</code> now uses <code>Path.Combine(PathResolver.RepoRoot, &quot;state&quot;, &quot;logs&quot;)</code> (verified live). <code>YouTubeFetchState</code> has <code>LastSortMoves/Attempted/Completed</code>. <code>YouTubeSyncProcessor</code> caps at <code>maxWritesPerRun=150</code> with <code>remainingBudget</code> + <code>IsQuotaOrRateLimit</code> early-exit. This doc retained as build spec reference; active source of truth is <code>youtube-architecture.md</code> + live <code>YouTubeFetchState.cs</code>/<code>YouTubeSyncProcessor.cs</code>/<code>Telemetry.cs</code>.</p>
</blockquote>
<hr />
<h2>summary</h2>
<p>4 bugs in YouTube sync: (1) misleading &quot;changed&quot; count vs sort-modified count, (2) quota exhaustion from 250+ writes/run with no early-exit, (3) empty repo logs due to CWD-relative path, (4) no diagnostic detail persisted to file.</p>
<p>All fixes use TDD-style verification: document failing state (current bug), implement fix, verify passing state (bug gone).</p>
<hr />
<h2>task 1: fix log path (always inside Toolbox dir)</h2>
<h3>failing state (current bug)</h3>
<pre class="syntax-highlighting"><code><span class="text plain">cd C:\Users\Lance
.\Dev\Toolbox\artifacts\bin\App\debug\App.exe sync youtube
# Observe: C:\Users\Lance\Dev\Toolbox\logs\youtube.jsonl is EMPTY
# Observe: C:\Users\Lance\logs\youtube.jsonl has content (wrong location)
</span></code></pre>
<h3>passing state (after fix)</h3>
<pre class="syntax-highlighting"><code><span class="text plain">cd C:\Users\Lance
.\Dev\Toolbox\artifacts\bin\App\debug\App.exe sync youtube
# Observe: C:\Users\Lance\Dev\Toolbox\logs\youtube.jsonl has content (PASS)
# Observe: C:\Users\Lance\logs\youtube.jsonl doesn&#39;t exist or is stale
</span></code></pre>
<h3>implementation</h3>
<p><strong>File:</strong> <code>src/Core/Telemetry.cs:26</code></p>
<pre class="syntax-highlighting"><code><span class="text plain">// BEFORE:
AddServiceLogger(config, service, $&quot;logs/{service.ToFileSlug()}.jsonl&quot;);

// AFTER:
var logDir = Path.Combine(PathResolver.RepoRoot, &quot;logs&quot;);
Directory.CreateDirectory(logDir);
AddServiceLogger(config, service, Path.Combine(logDir, $&quot;{service.ToFileSlug()}.jsonl&quot;));
</span></code></pre>
<p><code>PathResolver.RepoRoot</code> walks up from <code>AppContext.BaseDirectory</code> to find <code>.git</code> or <code>.env</code> → resolves to <code>C:\Users\Lance\Dev\Toolbox\</code>. Logs always inside Toolbox dir, regardless of CWD.</p>
<h3>acceptance criteria</h3>
<ul>
<li><input type="checkbox" checked="" disabled="" /> <code>logs/youtube.jsonl</code> has content after sync run from any directory</li>
<li><input type="checkbox" checked="" disabled="" /> No logs created in CWD or .exe directory</li>
</ul>
<hr />
<h2>task 2: decouple file log level (always capture Debug+)</h2>
<h3>failing state (current bug)</h3>
<pre class="syntax-highlighting"><code><span class="text plain">cd C:\Users\Lance\Dev\Toolbox
.\artifacts\bin\App\debug\App.exe sync youtube
# Run WITHOUT --verbose
# Observe: logs/youtube.jsonl has only Info/Warning/Error entries
# Observe: no Debug entries (SortPlaylist pass details missing)
</span></code></pre>
<h3>passing state (after fix)</h3>
<pre class="syntax-highlighting"><code><span class="text plain">cd C:\Users\Lance\Dev\Toolbox
.\artifacts\bin\App\debug\App.exe sync youtube
# Run WITHOUT --verbose
# Observe: logs/youtube.jsonl has Debug entries (PASS)
# Example: &quot;YouTube.SortPlaylist pass 1: X updated, Y failed&quot;
</span></code></pre>
<h3>implementation</h3>
<p><strong>File:</strong> <code>src/Core/Telemetry.cs:16-56</code></p>
<pre class="syntax-highlighting"><code><span class="text plain">// BEFORE: file sink shares LevelSwitch with console
// AFTER: file sink has its own fixed Debug level

private static async Task AddServiceLogger(
    LoggerConfiguration config,
    ServiceName service,
    string path
)
{
    _ = config.WriteTo.Logger(lc =&gt;
        lc.Filter.ByIncludingOnly(e =&gt;
                e.Properties.TryGetValue(&quot;Service&quot;, out LogEventPropertyValue? propValue)
                &amp;&amp; propValue is ScalarValue sv
                &amp;&amp; sv.Value is string serviceName
                &amp;&amp; serviceName == service.ToString()
            )
            .WriteTo.File(
                new CompactJsonFormatter(),
                path,
                rollingInterval: RollingInterval.Infinite,
                retainedFileCountLimit: null,
                fileSizeLimitBytes: 50 * 1024 * 1024,
                restrictedToMinimumLevel: LogEventLevel.Debug  // &lt;-- ADD: always capture Debug+
            )
    );
}
</span></code></pre>
<p>Console still respects <code>--verbose</code>/<code>--debug</code>/default. File always captures Debug+ for diagnostics.</p>
<h3>acceptance criteria</h3>
<ul>
<li><input type="checkbox" checked="" disabled="" /> JSONL files contain Debug entries after default (non-verbose) run</li>
<li><input type="checkbox" checked="" disabled="" /> Console output unchanged (Info by default, Debug with --verbose)</li>
</ul>
<hr />
<h2>task 3: early-exit on quota/rate-limit errors</h2>
<h3>failing state (current bug)</h3>
<pre class="syntax-highlighting"><code><span class="text plain"># Run sync that hits quota
# Observe console: 109+ &quot;Failed to update ... quota&quot; errors
# Observe: each 403 costs 50 units (wasted), hammering continues
# Observe: total errors ~109 (lines 16-124 in youtube.jsonl)
</span></code></pre>
<h3>passing state (after fix)</h3>
<pre class="syntax-highlighting"><code><span class="text plain"># Run sync that hits quota
# Observe console: &quot;Quota exhausted, stopping sort&quot; after FIRST quota error
# Observe: no hammering, no 100+ failed calls
# Observe: remaining playlists skipped gracefully
</span></code></pre>
<h3>implementation</h3>
<p><strong>File:</strong> <code>src/Services/Google/YouTube/YouTubeSortService.cs:196-266</code> (<code>ExecuteSortPlanAsync</code>)</p>
<pre class="syntax-highlighting"><code><span class="text plain">// BEFORE: catches exception, increments failures, CONTINUES loop
// AFTER: detect quota/rate-limit, BREAK immediately

for (var i = 0; i &lt; plan.Updates.Count; i++)
{
    // ... existing code ...
    try
    {
        await yt.PlaylistItems.Update(item, &quot;snippet&quot;).ExecuteAsync(ct);
        successes++;
    }
    catch (GoogleApiException ex) when (IsQuotaOrRateLimit(ex))
    {
        Telemetry.Error(&quot;Quota/rate-limit exhausted at item {Index}/{Total}. Stopping sort.&quot;,
            i + 1, plan.Updates.Count);
        return Errors.YouTube.QuotaExceeded($&quot;Quota exhausted after {successes} updates&quot;);
    }
    catch (Exception ex)
    {
        failures++;
        Telemetry.Error(&quot;Failed to update ItemId={ItemId} to position {Position}: {Error}&quot;,
            itemId, newPosition, ex.Message);
    }
    // ...
}

private static bool IsQuotaOrRateLimit(GoogleApiException ex)
    =&gt; ex.HttpStatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
       &amp;&amp; ex.Message.Contains(&quot;quota&quot;, StringComparison.OrdinalIgnoreCase);
</span></code></pre>
<p>Return <code>ErrorOr</code> error → caller breaks pass loop → caller breaks playlist loop → graceful stop.</p>
<h3>acceptance criteria</h3>
<ul>
<li><input type="checkbox" checked="" disabled="" /> First quota error triggers immediate stop (no hammering)</li>
<li><input type="checkbox" checked="" disabled="" /> Console shows &quot;Quota exhausted&quot; message</li>
<li><input type="checkbox" checked="" disabled="" /> Remaining playlists skipped without attempting updates</li>
</ul>
<hr />
<h2>task 4: quota budget per run (cap at 150 writes)</h2>
<h3>failing state (current bug)</h3>
<pre class="syntax-highlighting"><code><span class="text plain"># Run sync with large backlog
# Observe: 250+ write attempts
# Observe: quota exhausted mid-batch (positions 78-241+)
# Observe: 12,800+ units consumed (&gt; 10,000/day limit)
</span></code></pre>
<h3>passing state (after fix)</h3>
<pre class="syntax-highlighting"><code><span class="text plain"># Run sync with large backlog
# Observe: max ~150 write attempts
# Observe: &quot;Quota budget reached (150/150 writes). Stopping sort.&quot; message
# Observe: graceful stop before quota exhaustion
# Observe: next run continues with remaining playlists
</span></code></pre>
<h3>implementation</h3>
<p><strong>Files:</strong></p>
<ul>
<li><code>src/Services/Google/YouTube/YouTubeSyncProcessor.cs:147-173</code> (<code>SortPlaylistsAsync</code>)</li>
<li><code>src/Services/Google/YouTube/YouTubeSortService.cs:11-114</code> (<code>SortPlaylistAsync</code>)</li>
</ul>
<pre class="syntax-highlighting"><code><span class="text plain">// YouTubeSyncProcessor.cs - pass budget to sort

public async Task SortPlaylistsAsync(
    IReadOnlyList&lt;string&gt; playlistIds,
    YouTubeFetchState state,
    CancellationToken ct
)
{
    const int maxWritesPerRun = 150;  // 150 * 50 = 7,500 units, leaves headroom
    var writesConsumed = 0;

    foreach (var playlistId in playlistIds)
    {
        if (writesConsumed &gt;= maxWritesPerRun)
        {
            Telemetry.Info(&quot;Quota budget reached ({Writes}/{Max} writes). Stopping sort.&quot;,
                writesConsumed, maxWritesPerRun);
            break;
        }

        var result = await SortSinglePlaylistAsync(playlistId, snapshot, state, ct,
            remainingBudget: maxWritesPerRun - writesConsumed);
        
        if (result.IsError)
            break;  // quota error or other failure
        
        writesConsumed += result.WritesConsumed;
    }
}
</span></code></pre>
<pre class="syntax-highlighting"><code><span class="text plain">// YouTubeSortService.cs - enforce budget within sort

public async Task&lt;ErrorOr&lt;SortResult&gt;&gt; SortPlaylistAsync(
    string playlistId,
    IReadOnlyDictionary&lt;string, string&gt; translatedTitles,
    int remainingBudget,  // &lt;-- ADD parameter
    CancellationToken ct
)
{
    // ... existing code ...
    var passResult = await ExecuteSortPlanAsync(plan, remainingBudget, ct);
    // ...
}

private async Task&lt;ErrorOr&lt;SortPassResult&gt;&gt; ExecuteSortPlanAsync(
    SortPlan plan,
    int remainingBudget,  // &lt;-- ADD parameter
    CancellationToken ct
)
{
    var maxUpdatesThisPass = Math.Min(plan.Updates.Count, remainingBudget);
    
    for (var i = 0; i &lt; maxUpdatesThisPass; i++)
    {
        // ... existing update logic ...
        if (writesConsumed &gt;= remainingBudget)
        {
            Telemetry.Warn(&quot;Quota budget exhausted mid-playlist. Stopping after {Count} writes.&quot;,
                writesConsumed);
            return new SortPassResult(successes, failures, writesConsumed);
        }
    }
}
</span></code></pre>
<h3>acceptance criteria</h3>
<ul>
<li><input type="checkbox" checked="" disabled="" /> Max 150 writes per run (configurable constant)</li>
<li><input type="checkbox" checked="" disabled="" /> &quot;Quota budget reached&quot; message when limit hit</li>
<li><input type="checkbox" checked="" disabled="" /> Graceful stop, no quota exhaustion errors</li>
<li><input type="checkbox" checked="" disabled="" /> Next run continues with remaining playlists</li>
</ul>
<hr />
<h2>task 5: break churn cycle (track sort state, prioritize intelligently)</h2>
<h3>failing state (current bug)</h3>
<pre class="syntax-highlighting"><code><span class="text plain"># Run sync twice
# Observe: same 20 playlists sorted both times (alphabetical by ID)
# Observe: partially-sorted playlists re-attempted every run
# Observe: churn cycle never resolves
</span></code></pre>
<h3>passing state (after fix)</h3>
<pre class="syntax-highlighting"><code><span class="text plain"># Run sync twice
# Observe: first run sorts changed + interrupted playlists
# Observe: second run skips already-sorted playlists (0 moves)
# Observe: churn cycle broken, progress made each run
</span></code></pre>
<h3>implementation</h3>
<p><strong>Files:</strong></p>
<ul>
<li><code>src/Services/Google/YouTube/YouTubeFetchState.cs</code> - add sort state tracking</li>
<li><code>src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs:187-216</code> (<code>ExecuteWithSortAsync</code>)</li>
</ul>
<pre class="syntax-highlighting"><code><span class="text plain">// YouTubeFetchState.cs - add sort state to PlaylistSnapshot

public record PlaylistSnapshot
{
    // ... existing fields ...
    public int? LastSortMoves { get; init; }  // moves needed last sort (null = never sorted)
    public DateTimeOffset? LastSortAttempted { get; init; }
    public bool LastSortCompleted { get; init; }  // true if 0 moves or fully sorted
}
</span></code></pre>
<pre class="syntax-highlighting"><code><span class="text plain">// YouTubePlaylistOrchestrator.cs - prioritize changed + interrupted

var prioritizedIds = allPlaylistIds
    .OrderByDescending(id =&gt; processedIds.Contains(id))  // changed first
    .ThenByDescending(id =&gt; state.PlaylistSnapshots.GetValueOrDefault(id)?.LastSortMoves ?? 0)  // most-unsorted first
    .ThenBy(id =&gt; state.PlaylistSnapshots.GetValueOrDefault(id)?.LastSortAttempted ?? DateTimeOffset.MinValue)  // oldest attempt first
    .Where(id =&gt; !state.PlaylistSnapshots.GetValueOrDefault(id)?.LastSortCompleted ?? true)  // skip already-sorted
    .Take(20)
    .ToList();
</span></code></pre>
<pre class="syntax-highlighting"><code><span class="text plain">// YouTubeSortService.cs - update sort state after sort

var sortState = new SortState
{
    LastSortMoves = totalRepositioned,
    LastSortAttempted = DateTimeOffset.UtcNow,
    LastSortCompleted = totalRepositioned == 0 || allSorted
};
</span></code></pre>
<h3>acceptance criteria</h3>
<ul>
<li><input type="checkbox" checked="" disabled="" /> Sort state tracked per playlist in manifest</li>
<li><input type="checkbox" checked="" disabled="" /> Prioritization: changed &gt; most-unsorted &gt; oldest-attempt</li>
<li><input type="checkbox" checked="" disabled="" /> Already-sorted playlists skipped</li>
<li><input type="checkbox" checked="" disabled="" /> Churn cycle broken (progress each run)</li>
</ul>
<hr />
<h2>task 6: accurate sort reporting (separate counts)</h2>
<h3>failing state (current bug)</h3>
<pre class="syntax-highlighting"><code><span class="text plain"># Run sync
# Observe: &quot;1 changed&quot; but 9+ playlists repositioned
# Observe: misleading report (changed != modified)
</span></code></pre>
<h3>passing state (after fix)</h3>
<pre class="syntax-highlighting"><code><span class="text plain"># Run sync
# Observe: &quot;Sync: 1 changed (YouTube)&quot; (accurate)
# Observe: &quot;Sort: 9 modified, 11 already-sorted, 20 attempted&quot; (accurate)
# Observe: clear separation of sync vs sort work
</span></code></pre>
<h3>implementation</h3>
<p><strong>File:</strong> <code>src/Services/Google/YouTube/YouTubePlaylistOrchestrator.cs:168-185</code> (<code>Finalize</code>)</p>
<pre class="syntax-highlighting"><code><span class="text plain">// BEFORE:
Telemetry.Info(&quot;Sync done ... {New} new, {Changed} changed ...&quot;, ...);

// AFTER:
Telemetry.Info(
    &quot;Sync done in {Elapsed:F1}s: {New} new, {Changed} changed (YouTube) | {TotalVideos} videos&quot;,
    syncStopwatch.Elapsed.TotalSeconds,
    outcome.Changes.NewPlaylists.Count,
    outcome.Changes.ChangedPlaylists.Count,
    result.TotalVideos
);

// Add sort summary after sort completes
if (sortResult is { })
{
    Telemetry.Info(
        &quot;Sort complete: {Attempted} attempted, {Modified} modified, {AlreadySorted} already-sorted | {TotalWrites} writes ({WritesUnits} units)&quot;,
        sortResult.Attempted,
        sortResult.Modified,
        sortResult.AlreadySorted,
        sortResult.TotalWrites,
        sortResult.TotalWrites * 50
    );
}
</span></code></pre>
<p><strong>File:</strong> <code>src/Services/Google/YouTube/YouTubeSortService.cs:56-62</code> (per-playlist log)</p>
<pre class="syntax-highlighting"><code><span class="text plain">// BEFORE: &quot;X repositioned&quot; (ambiguous across passes)
// AFTER: track distinct item IDs

var distinctItemsMoved = updates
    .Where(u =&gt; u.Success)
    .Select(u =&gt; u.Item.Id)
    .Distinct()
    .Count();

Telemetry.Info(&quot;{PlaylistName} — {Distinct}/{Total} items sorted ({ApiCalls} API calls)&quot;,
    playlistName, distinctItemsMoved, itemCount, totalRepositioned);
</span></code></pre>
<h3>acceptance criteria</h3>
<ul>
<li><input type="checkbox" checked="" disabled="" /> Sync summary: &quot;X changed (YouTube)&quot; (accurate)</li>
<li><input type="checkbox" checked="" disabled="" /> Sort summary: &quot;X modified, Y already-sorted, Z attempted&quot; (accurate)</li>
<li><input type="checkbox" checked="" disabled="" /> Per-playlist: distinct items sorted vs API calls (no inflation)</li>
<li><input type="checkbox" checked="" disabled="" /> Write count + units logged</li>
</ul>
<hr />
<h2>verification summary</h2>
<table>
<thead>
<tr>
<th>task</th>
<th>failing command</th>
<th>passing command</th>
</tr>
</thead>
<tbody>
<tr>
<td>1 (log path)</td>
<td>sync from <code>C:\Users\Lance\</code>, repo <code>logs/</code> empty</td>
<td>sync from <code>C:\Users\Lance\</code>, repo <code>logs/</code> has content</td>
</tr>
<tr>
<td>2 (file level)</td>
<td>sync without --verbose, JSONL has no Debug</td>
<td>sync without --verbose, JSONL has Debug</td>
</tr>
<tr>
<td>3 (early-exit)</td>
<td>sync hits quota, 109+ failed 403s</td>
<td>sync hits quota, stops after first error</td>
</tr>
<tr>
<td>4 (quota budget)</td>
<td>sync attempts 250+ writes, quota dies</td>
<td>sync caps at 150 writes, stops gracefully</td>
</tr>
<tr>
<td>5 (churn cycle)</td>
<td>sync twice, same 20 playlists both times</td>
<td>sync twice, second run skips sorted</td>
</tr>
<tr>
<td>6 (reporting)</td>
<td>&quot;1 changed&quot; but 9+ modified</td>
<td>&quot;1 changed (YouTube)&quot; + &quot;9 modified (sort)&quot;</td>
</tr>
</tbody>
</table>
<hr />
<h2>implementation order</h2>
<ol>
<li><strong>Task 1</strong> (log path) — foundational, enables verification of other tasks</li>
<li><strong>Task 2</strong> (file level) — enables diagnostic logging for debugging</li>
<li><strong>Task 3</strong> (early-exit) — immediate safety improvement</li>
<li><strong>Task 4</strong> (quota budget) — prevents quota exhaustion</li>
<li><strong>Task 5</strong> (churn cycle) — breaks the perpetual re-sort loop</li>
<li><strong>Task 6</strong> (reporting) — accurate counts, depends on tasks 3-5</li>
</ol>
<p>Tasks 1-2 are logging infrastructure. Tasks 3-5 are quota fixes. Task 6 is reporting polish.</p>
<hr />
<h2>dependencies</h2>
<ul>
<li>Task 5 depends on Task 3 (need sort state to track interrupted playlists)</li>
<li>Task 6 depends on Tasks 3-5 (need accurate counts from quota budgeting)</li>
<li>Tasks 1-2 independent, can be done first</li>
</ul>
<hr />
<h2>must-not-have</h2>
<ul>
<li>no test NuGet packages (xUnit, NUnit, MSTest) — per AGENTS.md</li>
<li>no new dependencies</li>
<li>no concurrent sort (would worsen quota)</li>
<li>no batch size &lt; 20 (quota cap handles safety)</li>
<li>no reducing write limit below 150 (leaves no headroom for reads)</li>
</ul>
