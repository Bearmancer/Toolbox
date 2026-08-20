<h1>Toolbox Mega Plan — CPM-Sequenced, SoC-Modular</h1>
<blockquote>
<p><strong>For agentic workers:</strong> REQUIRED SUB-SKILL: Use <code>superpowers:subagent-driven-development</code> (recommended) or <code>superpowers:executing-plans</code> to implement this plan task-by-task. Steps use checkbox (<code>- [ ]</code>) syntax for tracking.</p>
</blockquote>
<p><strong>Goal:</strong> Land all actionable markdown backlog as working, testable software with zero feature loss, preserving F1-F10 (YouTube) and 9-step SACD pipeline, via 4 SoC modules sequenced on CPM critical path, maximizing modularity and exhaustiveness, verified against 309-file local state.</p>
<p><strong>Architecture:</strong> 4 SoC modules (M1 YouTube seams → M2 error plumbing → M3 Core/Audio shrink → M4 Layer/Telemetry) wired on CPM DAG where each module's produced data (seam metrics, typed errors, shrunk services, relocated Dashboard) is consumed by the next; M1+M3 parallel seeds, M2 consumes M1 mapper seam, M4 consumes all prior. Exhaustive catalog: 9 .omo/plans files deduped (seams S-<em>/N-</em>, error taxonomy 7 codes, dead catalog 18+4+5, verdict buckets/gods) + local state ground truth (145 snapshots, 10 jsonl).</p>
<p><strong>Tech Stack:</strong> .NET 11.0 preview SDK (<code>&lt;UseArtifactsOutput&gt;true&lt;/UseArtifactsOutput&gt;</code> → <code>artifacts/</code>), Spectre.Console.Cli, Serilog (<code>Telemetry.ForService</code>), ErrorOr, Google.Apis.YouTube.v3 (<code>GoogleApiException</code>), Azure.AI.Translation.Text (<code>RequestFailedException</code>), System.Text.Json (PascalCase, <code>WriteIndented=true</code>, <code>HmsTimeSpanConverter</code> <code>hh:mm:ss</code>), DotNetEnv, SSH.NET (<code>Renci.SshNet</code>).</p>
<h2>Global Constraints</h2>
<ul>
<li>.NET 11.0 preview required (<code>SuppressNETCoreSdkPreviewMessage</code> set, <code>LangVersion preview</code>, <code>TargetFramework net11.0</code>), per <code>Directory.Build.props</code>.</li>
<li><code>state/logs/</code> lives at <code>PathResolver.RepoRoot/state/logs</code> (<code>state/logs/*.jsonl</code>, 10 sinks) — never <code>logs/</code> root; <code>CompactJsonFormatter</code>, 50 MB cap, <code>restrictedToMinimumLevel:Debug</code> correctly scoped via <code>Telemetry.ForService(ServiceName.X)</code>.</li>
<li>PascalCase JSON only (<code>WriteIndented=true</code>), no <code>PropertyNamingPolicy</code>; one class per file; <code>private static readonly string</code> for constants; zero inline comments; XML docs only where required.</li>
<li>Auth <code>.env</code> only (<code>AzureCredentials.Read()</code>, <code>GoogleCredentials.Read()</code>, <code>LASTFM_*</code> env), no hardcoded secrets; <code>GoogleSetup.AddGoogleServicesAsync</code> async OAuth2 <code>FileDataStore state/google-auth</code>, scope <code>Youtube</code>, 5-min CTS.</li>
<li>DI per service <code>extension(IServiceCollection)</code> in <code>*Setup.cs</code>; <code>ErrorOr&lt;T&gt;</code> railway (<code>result.Match</code>, <code>.ThenAsync</code>); <code>Telemetry.ForService</code> scopes log entries.</li>
<li>No <code>global::</code>, no inline <code>fully-qualified</code>, no <code>#pragma warning disable</code>, no <code>Directory.Build.targets</code>, no test NuGet (standalone <code>.cs</code> with <code>Main()</code> harnesses only); <code>EnforceCodeStyleInBuild</code> + <code>TreatWarningsAsErrors</code>; <code>.editorconfig</code> is source of truth.</li>
<li>Build-verify every edit (<code>dotnet build</code> clean); commit after each phase (1-3 files per commit, atomic, revertable).</li>
<li>Branch <code>master</code> @ <code>06488b7 Pre YouTube pruning</code>; manifests: <code>state/youtube/manifest.json</code> (145 snapshots keyed by <code>PlaylistId</code>); raw/processed each 145; deleted 3 + merge-manifests 1.</li>
</ul>
<hr />
<h2>CPM DAG</h2>
<pre class="syntax-highlighting"><code><span class="text plain">M1 (YouTube seams) ──┬──→ M2 (error plumbing, needs N-03 seam join)
                    └──→ M3 (Core/Audio shrink, parallel — no YouTube seam overlap)
M1 + M3 ──→ M4 (Layer/Telemetry, consumes Dashboard move + shrink + seams)
M1 → M2 → M4 critical path: ~3 build cycles; M1+M3 fan seeds critical path length.
Archive: youtube-quota-logging (DONE), youtube-duplicate-playlist-merge (DONE), toolbox-flatline (DONE snapshot) — not on DAG.
taste.md — constraint, not task.
</span></code></pre>
<p><strong>Critical path:</strong> M1 (seams, wires metrics) → M2 (mappers, needs N-03 seam shape stable) → M4 (relocates Dashboard which consumes seam S-13 + error codes). M3 (shrink) runs parallel with M1/M2 except SSH.NET dedup join with M4 Dashboard SSH dep; defer M4 SSH move until M3 proof passes.</p>
<h2>Modularity Target (maximize SoC)</h2>
<table>
<thead>
<tr>
<th>Module</th>
<th>SoC</th>
<th>Sources</th>
<th>Touches</th>
</tr>
</thead>
<tbody>
<tr>
<td>M1</td>
<td>YouTube data seams</td>
<td><code>youtube-seams.md</code> + arch Buckets A-E (youtube-only)</td>
<td>6 youtube files seam hygiene only</td>
</tr>
<tr>
<td>M2</td>
<td>Error plumbing</td>
<td><code>error-taxonomy.md</code> + dead catalog logical-error</td>
<td>4 files error codes only</td>
</tr>
<tr>
<td>M3</td>
<td>Core/Audio shrink</td>
<td><code>overengineering-verdict.md</code> gods + dead catalog unused</td>
<td>Core/Audio/Props only</td>
</tr>
<tr>
<td>M4</td>
<td>Layer/Telemetry</td>
<td><code>overengineering-verdict.md</code> SoC-MOVE + arch D</td>
<td>Dashboard/Telemetry/OciConfig/Delegate</td>
</tr>
</tbody>
</table>
<p>No module repeats another's concern; each produces a testable artifact (wired logs, typed errors, smaller services, relocated Dashboard).</p>
<h2>Exhaustiveness vs Modularity</h2>
<table>
<thead>
<tr>
<th>Source SoC</th>
<th>How exhaustively covered</th>
<th>Modularity payoff</th>
</tr>
</thead>
<tbody>
<tr>
<td>Seams 17 (N-01..S-17, S-01..S-08)</td>
<td>every ID triaged fix/keep/dupe/wire, dedup table</td>
<td>M1 stays 6-file seam-only — god/ error split out</td>
</tr>
<tr>
<td>Error 7 codes + 5 logical-error</td>
<td>2-step proof per dead factory + per-code mapper</td>
<td>M2 stays 4-file error-only</td>
</tr>
<tr>
<td>Dead 18+4 catalog</td>
<td>caller scan per symbol before delete</td>
<td>split across M1/M2/M3 per domain — no bulk sweep</td>
</tr>
<tr>
<td>Verdict gods 9 + buckets + telemetry</td>
<td>god table reduced to shrinks, SoC-MOVE isolated to M4, telemetry keep</td>
<td>M3 shrink-only, M4 layer-only</td>
</tr>
</tbody>
</table>
<hr />
<h2>Module M1 — YouTube Data Seams &amp; Hygiene</h2>
<p><strong>Goal:</strong> Fix gaps, wire unconsumed, dedupe seam IDs, preserve F1-F10.</p>
<p><strong>Files:</strong> See fragment <code>.omo/plans/.tmp/m1-youtube-seams-plan.md</code> (445 lines) — full Seam Catalog table + 9 tasks Tasks 1-9 with exact diffs + harnesses.</p>
<p><strong>Key seams (fix vs keep vs dupe vs wire):</strong></p>
<table>
<thead>
<tr>
<th>ID</th>
<th>Action</th>
</tr>
</thead>
<tbody>
<tr>
<td>S-13 reverseLookup collision</td>
<td>fix — key by <code>PlaylistId</code> + warn on <code>TryAdd</code> collision, resolve via <code>snapshotById</code></td>
</tr>
<tr>
<td>N-05 hi transliterate</td>
<td>fix — gate <code>translated = !isTransliterated &amp;&amp; lang not en/unknown</code> + <code>effectiveText</code> ternary</td>
</tr>
<tr>
<td>N-03 Failures + S-11 SortStatistics</td>
<td>fix+wire — keep typed <code>SortPassResult(Failures)</code> on mixed path (don't collapse to <code>ApiError</code> string), expose <code>SortStatistics</code> via <code>Orchestrator.ExecuteWithSortAsync</code> → <code>Finalize</code>/CLI</td>
</tr>
<tr>
<td>S-09 SkippedVideos</td>
<td>wire — <code>SyncResult.SkippedVideos</code> to <code>Finalize</code> log (S-06/S-12 dedupe)</td>
</tr>
<tr>
<td>S-10 UnchangedPlaylists</td>
<td>wire — <code>ChangeDetectionResult.UnchangedPlaylists.Count</code> to <code>Finalize</code></td>
</tr>
<tr>
<td>S-17 HmsTimeSpanConverter</td>
<td>fix — reuse <code>YouTubeFetchState.JsonOptions</code>, delete Merger private <code>JsonOptions</code></td>
</tr>
<tr>
<td>S-15/S-16 LastChecked</td>
<td>keep — throttle future (<code>// ponytail: throttle read not yet wired</code>) — produced-never-consumed = missing impl, not dead</td>
</tr>
<tr>
<td>S-14 Description</td>
<td>keep — search needs it (~1.5 MB bloat accepted)</td>
</tr>
<tr>
<td>S-01 TranslatedTitle/DetectedLanguage</td>
<td>wire — <code>detectedLanguage + hasTranslation</code> to <code>DashboardDataBuilder.BuildVideoData</code></td>
</tr>
<tr>
<td>S-02/S-05/S-06/S-07 dupes</td>
<td>deduped, no separate tasks</td>
</tr>
</tbody>
</table>
<p><strong>Feature-loss gate (M1):</strong> F1 state cache ✓, F2 detection ✓, F3 bulk sync ✓, F4 single-playlist by title ✓ (delegate touched only in M4, not M1), F5 merge ✓, F6 LIS sort ✓, F7 resume sort ✓, F8 translate ✓ (N-05 fixed not removed), F9 archive ✓, F10 youtube.jsonl ✓. Dead-vs-gap rule: 0+0 = dead only (Bucket A none qualify); produced-never-consumed = propose consumer and keep until impl.</p>
<p><strong>Interface shims for M2:</strong> Tasks 3/4 keep <code>ErrorOr&lt;SortPassResult&gt;</code> quota path so M2 <code>GoogleApiException → YT.RateLimit/QuotaExceeded</code> swaps without churn.</p>
<p><strong>Tasks (bite-sized):</strong></p>
<h3>Task 1 — Fix S-13 <code>DashboardService.reverseLookup</code> collision (key by PlaylistId)</h3>
<p><strong>Files:</strong> Modify <code>src/Services/Google/YouTube/DashboardService.cs</code></p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">// Before:
Dictionary&lt;string, string&gt; reverseLookup = [];
foreach (var p in playlists) reverseLookup.TryAdd(Text.SanitizeFileName(p.Title), p.Title);
// After:
Dictionary&lt;string, string&gt; reverseLookup = [];
Dictionary&lt;string, PlaylistSnapshot&gt; snapshotById = playlists.ToDictionary(p =&gt; p.PlaylistId, p =&gt; p);
foreach (PlaylistSnapshot p in playlists) {
    var key = Text.SanitizeFileName(p.Title);
    if (!reverseLookup.TryAdd(key, p.PlaylistId))
        Telemetry.Warn(&quot;Dashboard reverseLookup collision: sanitized &#39;{Key}&#39; maps to multiple playlists ({Existing} vs {New}) — last wins&quot;, key, reverseLookup[key], p.PlaylistId);
}
if (reverseLookup.TryGetValue(Path.GetFileNameWithoutExtension(file), out var pid)
    &amp;&amp; snapshotById.TryGetValue(pid, out var snap))
    result[snap.Title] = videos;
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write harness <code>artifacts/harness/s13_harness.cs</code> (<code>Main()</code> with two <code>PlaylistSnapshot</code> <code>Title=&quot;My Mix&quot;</code> / <code>&quot;My Mix &quot;</code> colliding via <code>SanitizeFileName</code>, assert <code>TryAdd</code> drops without fix).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Run <code>dotnet build</code> + <code>dotnet run --project artifacts/harness/s13_harness.cs</code> — expect FAIL before fix.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Apply diff above to <code>DashboardService.cs</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Re-run harness + <code>dotnet build</code> — expect PASS + Warn log on collision; delete harness.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>fix(youtube): S-13 dashboard reverseLookup key by PlaylistId</code>.</li>
</ul>
<h3>Task 2 — Fix N-05 <code>hi</code> transliterate over-count</h3>
<p><strong>Files:</strong> Modify <code>src/Services/Google/YouTube/YouTubeTranslationService.cs</code> (<code>ApplyTranslationResults</code>)</p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">var isTransliterated = target.Transliterate;
var translated = !isTransliterated &amp;&amp; detectedLang is not &quot;en&quot; and not &quot;unknown&quot;;
var effectiveText = isTransliterated ? result.TranslatedText
                  : translated ? result.TranslatedText
                  : (target.Field==TranslationField.Title ? video.Title : video.Description);
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>s05_harness.cs</code> with cases <code>(&quot;hi&quot;,true,false), (&quot;en&quot;,false,false), (&quot;fr&quot;,false,true)</code> + histogram distinct <code>hi</code> vs <code>en</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Run — FAIL before fix (hi counts as translated).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Patch <code>ApplyTranslationResults</code> with ternary above.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + harness PASS.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>fix(youtube): N-05 hi transliterate not counted as translated</code>.</li>
</ul>
<h3>Task 3 — Fix N-03 <code>SortPassResult.Failures</code> + S-11 <code>SortStatistics</code> (sync with M2)</h3>
<p><strong>Files:</strong> Modify <code>YouTubeSortService.cs</code>, <code>YouTubeSyncProcessor.cs</code>, <code>YouTubePlaylistOrchestrator.cs</code>, <code>CLI/Sync/YouTube/SyncYoutubeCommand.cs</code></p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">return new SortPassResult(successes, failures, writesConsumed, movedItemIds.Count); // always typed, quota stays Error
public readonly record struct SortStatistics(int Attempted, int Modified, int AlreadySorted, int TotalWrites);
Task&lt;(IReadOnlyList&lt;string&gt; Ids, SortStatistics SortStats)&gt; ExecuteWithSortAsync(bool noTranslate, CancellationToken ct)
ErrorOr&lt;SyncOutcome&gt; Finalize(ProcessOutcome outcome, SortStatistics? sortStats, Stopwatch sw)
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>s11_n03_harness.cs</code> asserting <code>SortStatistics</code> propagation and <code>SortPassResult.Failures</code> preservation.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Run — FAIL (CLI never sees stats).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Patch 4 files; keep quota <code>IsQuotaOrRateLimit</code> → <code>YT.QuotaExceeded</code> error path; mixed <code>Failures&gt;0</code> returns typed result with <code>Telemetry.Error</code> branch.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + harness PASS; <code>grep SortStatistics</code> hits Orchestrator/CLI now.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>fix(youtube): N-03 preserve Failures, S-11 expose SortStatistics</code>.</li>
</ul>
<h3>Task 4 — Wire S-09 <code>SkippedVideos</code> + S-12 <code>TotalVideos</code> dupe</h3>
<p><strong>Files:</strong> Modify <code>YouTubePlaylistOrchestrator.cs</code> (<code>Finalize</code> log), <code>YouTubeSyncProcessor.cs</code> (log label), optionally <code>SyncYoutubeCommand.cs</code>.</p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">Telemetry.Info(&quot;Sync done in {Elapsed:F1}s: {New} new, {Changed} changed, {Deleted} deleted | {TotalVideos} videos ({Skipped} skipped)&quot;, ..., result.TotalVideos, result.SkippedVideos);
Telemetry.Debug(&quot;SyncResult: {PlaylistCount} playlists, {VideoCount} videos, {Skipped} skipped&quot;, result.ProcessedIds.Count, result.TotalVideos, result.SkippedVideos);
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>s09_harness.cs</code> asserting <code>SyncResult(SkippedVideos==7, TotalVideos==42, ProcessedIds.Count==2)</code> units differ.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Run — FAIL (Finalize log omits Skipped).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Patch <code>Orchestrator.Finalize</code>; no struct change.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + harness PASS.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>fix(youtube): wire S-09 SkippedVideos, clarify S-12</code>.</li>
</ul>
<h3>Task 5 — Wire S-10 <code>UnchangedPlaylists</code> (S-07 dupe)</h3>
<p><strong>Files:</strong> Modify <code>YouTubePlaylistOrchestrator.cs</code> (<code>Finalize</code> + detector log).</p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">Telemetry.Info(&quot;Change detection: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged&quot;, ...);
Telemetry.Info(&quot;Sync done ... | {Unchanged} unchanged&quot;, outcome.Changes.UnchangedPlaylists.Count);
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>s10_harness.cs</code> (<code>DetectChanges</code> with stored snapshot, assert <code>UnchangedPlaylists.Count==1</code>).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Run — FAIL (Finalize never references Unchanged).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Patch <code>Orchestrator.Finalize</code>; add doc note in <code>YouTubeChangeDetector.cs</code> if missing.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + harness PASS.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>fix(youtube): wire S-10 UnchangedPlaylists</code>.</li>
</ul>
<h3>Task 6 — Fix S-17 <code>HmsTimeSpanConverter</code> reuse + keep S-15/S-16</h3>
<p><strong>Files:</strong> Modify <code>YouTubeDuplicateMerger.cs</code> (delete private <code>JsonOptions</code>), <code>YouTubeFetchState.cs</code> (throttle comment).</p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">// Merger:
JsonSerializer.Serialize(manifest, YouTubeFetchState.JsonOptions)
// FetchState:
 // ponytail: throttle read not yet wired — gate fetch if LastChecked within window when throttle lands
public required DateTimeOffset LastChecked { get; init; }
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>s17_harness.cs</code> — <code>Serialize(YouTubeVideo, YouTubeFetchState.JsonOptions)</code> contains <code>hh:mm:ss</code> vs bare <code>WriteIndented</code> lacks it.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Run — FAIL (Merger private options lacks converter).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Delete private <code>JsonOptions</code>, replace 2 call sites; add throttle comments.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + harness PASS.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>fix(youtube): S-17 reuse YouTubeFetchState.JsonOptions</code>.</li>
</ul>
<h3>Task 7 — Wire S-01 <code>TranslatedTitle/DetectedLanguage</code></h3>
<p><strong>Files:</strong> Modify <code>src/CLI/Dashboard/DashboardDataBuilder.cs</code></p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">new { videoId = v.VideoId, title = v.TranslatedTitle ?? v.Title, originalTitle = v.Title, description = v.TranslatedDescription ?? v.Description, duration = v.Duration.ToString(@&quot;hh\:mm\:ss&quot;), channelId = v.ChannelId, channelName = v.ChannelName, playlistId = p.PlaylistId, playlistName = p.Title, detectedLanguage = v.DetectedLanguage ?? &quot;unknown&quot;, hasTranslation = v.TranslatedTitle != null }
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>s01_harness.cs</code> with <code>hi</code>/<code>en</code> video cases.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Run — FAIL (dashboard <code>window.allVideos</code> lacks <code>detectedLanguage</code>).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Patch <code>DashboardDataBuilder.BuildVideoData</code> projection.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + harness PASS; <code>dotnet run -- dashboard generate</code> optional check <code>state/dashboard/dashboard-data.js</code> contains <code>detectedLanguage</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>feat(dashboard): wire S-01 DetectedLanguage</code>.</li>
</ul>
<h3>Task 8 — Confirm S-03/S-14 keeps (verification only)</h3>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>s03_s14_harness.cs</code> (<code>XmlConvert.ToTimeSpan(&quot;PT1H2M3S&quot;)</code> round-trip <code>hh:mm:ss</code>, Description+Duration both stored).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Run — expect PASS; no code change if PASS.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> No commit (or <code>--allow-empty</code> <code>chore(youtube): confirm S-03/S-14</code>).</li>
</ul>
<h3>Task 9 — Dedup + S-08 confirm (verification only)</h3>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>dedup_harness.cs</code> (<code>YouTubeDuplicateMergePolicy.FindGroups</code> GroupBy <code>Title.Trim()</code> OrdinalIgnoreCase; <code>DashboardData.PlaylistCount</code> consumed).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Run — PASS; no code change.</li>
</ul>
<p><strong>Out of scope (M2/M4):</strong> god-file inline (<code>YouTubeChangeDetector</code>, <code>4 Execute*→2</code>, <code>ShouldBreak</code>), error <code>YT.RateLimit</code> mapper, <code>StateRoot</code>/<code>ManifestFile</code> path dedup, <code>ArchiveDeleted</code> duplicate — all M2/M4.</p>
<p><strong>Feature-loss gate:</strong> all F1-F10 preserved; dead-vs-gap rule applied per seam.</p>
<hr />
<h2>Module M2 — Error Taxonomy &amp; Plumbing</h2>
<p><strong>Goal:</strong> Wire typed <code>ErrorOr</code> codes; prune dead factories only after 2-step proof.</p>
<p><strong>Sources:</strong> <code>.omo/plans/error-taxonomy.md</code> (7 codes + Fix Spec), <code>.omo/plans/dead-code-catalog.md</code> (unconsumed #1-4, logical-error #1-5), <code>.omo/plans/youtube-seams.md</code> (N-03/N-05).</p>
<p><strong>CPM:</strong> Depends on M1 N-03 seam shape (keeps <code>SortPassResult.Failures</code> typed so mapper swap is clean). M2 produces typed codes consumed by M1 seam join + M4 telemetry.</p>
<h3>Task 10 — Pre-flight: prove current behaviour</h3>
<p><strong>Files:</strong> none modified; harnesses <code>artifacts/m2-*.cs</code> ephemeral.</p>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> <code>grep -R</code> 7 codes (<code>YT.RateLimit</code>, <code>YT.QuotaExceeded</code>, <code>YT.PlaylistNotFound</code>, <code>YT.VideoNotFound</code>, <code>Azure.AuthFailed</code>, <code>Azure.RateLimit</code>, <code>Azure.ServiceUnavailable</code>) across <code>src/</code>+<code>state/</code>+<code>artifacts/</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Confirm <code>YouTubeSortService:343 IsQuotaOrRateLimit</code> is behavioural guard distinct from ErrorOr routing — stays.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Stand up <code>artifacts/m2-google-mapper-harness.cs</code> + <code>artifacts/m2-azure-mapper-harness.cs</code> (<code>Main()</code> + <code>assert</code>).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Verify <code>Telemetry.ForService(ServiceName.*)</code> present in each target catch.</li>
</ul>
<h3>Task 11 — Dead-code proof: <code>YT.PlaylistNotFound/VideoNotFound</code> + <code>Azure.ServiceUnavailable</code></h3>
<p><strong>Files:</strong> <code>src/Core/Errors.cs</code> (conditional delete)</p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">public static class Errors.YouTube { public static Error PlaylistNotFound(string id) =&gt; Error.NotFound(&quot;YT.PlaylistNotFound&quot;, ...); }
public static class Errors.Azure   { public static Error ServiceUnavailable(string service) =&gt; Error.Failure($&quot;Azure.{service}Unavailable&quot;, ...); }
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> <code>grep -R &quot;PlaylistNotFound|VideoNotFound|ServiceUnavailable&quot;</code> + code-string grep (<code>YT\\.PlaylistNotFound</code>) — expect 0 outside <code>Errors.cs</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Scan <code>AGENTS.md</code> hierarchy + <code>youtube-architecture.md</code> + <code>youtube-quota-logging.md</code> for hidden consumer.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> If clean → delete 3 factories; else keep + annotate evidence in commit body.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + <code>lsp_diagnostics</code> on <code>Errors.cs</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>refactor(core): prune dead error factories after caller+docs proof</code> (or <code>docs: retain — hidden caller &lt;path&gt;</code>).</li>
</ul>
<h3>Task 12 — Wire <code>YouTubePlaylistService</code> typed mapper</h3>
<p><strong>Files:</strong> <code>src/Services/Google/YouTube/YouTubePlaylistService.cs</code></p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">private static Error MapGoogleApiException(GoogleApiException ex, string contextId) =&gt; ex.HttpStatusCode switch
{
    HttpStatusCode.TooManyRequests =&gt; Errors.YouTube.RateLimitExceeded,
    HttpStatusCode.NotFound        =&gt; Errors.YouTube.PlaylistNotFound(contextId), // fallback to ApiError if T11 deleted
    HttpStatusCode.Forbidden when ex.Message.Contains(&quot;quota&quot;, StringComparison.OrdinalIgnoreCase) =&gt; Errors.YouTube.QuotaExceeded(ex.Message),
    _ =&gt; Errors.YouTube.ApiError(ex.Message)
};
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Verify <code>using Google; using System.Net;</code> resolves <code>GoogleApiException</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Rewire <code>DeletePlaylistAsync</code> + <code>InsertPlaylistItemAsync</code> catches: <code>catch (GoogleApiException ex) =&gt; Map...(ex, ...)</code> + fallback <code>catch (Exception ex) =&gt; ApiError</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Fetch seam note: <code>GetPlaylistItemsAsync</code>/<code>GetPlaylistSummariesAsync</code> stay throwing until M1 settles; callers already catch → ApiError.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Run <code>m2-google-mapper-harness.cs</code> + <code>dotnet build</code> + <code>lsp_diagnostics</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>fix(youtube): typed GoogleApiException mapper</code>.</li>
</ul>
<h3>Task 13 — Extend <code>YouTubeSyncProcessor:79</code> for <code>YT.QuotaExceeded</code></h3>
<p><strong>Files:</strong> <code>src/Services/Google/YouTube/YouTubeSyncProcessor.cs</code></p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">if (error.Code is &quot;YT.RateLimit&quot; or &quot;YT.QuotaExceeded&quot; or &quot;Azure.RateLimit&quot;) { Telemetry.Warn(...); return ProcessResult.Break; }
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Patch <code>ProcessSinglePlaylistAsync:79</code> if-chain to add <code>&quot;YT.QuotaExceeded&quot;</code> (same <code>Break</code> as <code>RateLimit</code>).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Optional harness: feed <code>Errors.YouTube.QuotaExceeded(&quot;quota&quot;)</code> through mock error path → assert <code>Break</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>dotnet build</code> + <code>lsp_diagnostics</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Commit <code>fix(youtube): handle YT.QuotaExceeded in sync processor</code>.</li>
</ul>
<h3>Task 14 — Wire <code>TranslateService</code> typed mapper</h3>
<p><strong>Files:</strong> <code>src/Services/Azure/TranslateService.cs</code></p>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">private static Error MapTranslateException(Exception ex) =&gt; ex switch
{
    RequestFailedException rfe when rfe.Status == 429        =&gt; Errors.Azure.RateLimitExceeded,
    RequestFailedException rfe when rfe.Status is 401 or 403 =&gt; Errors.Azure.AuthenticationFailed,
    HttpRequestException hre when hre.StatusCode == HttpStatusCode.TooManyRequests =&gt; Errors.Azure.RateLimitExceeded,
    HttpRequestException hre when hre.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =&gt; Errors.Azure.AuthenticationFailed,
    _ =&gt; Errors.Translate.ApiError(ex.Message)
};
catch (Exception ex) { Telemetry.Error(...); return MapTranslateException(ex); }
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Verify <code>using Azure;</code> resolves <code>RequestFailedException</code> (<code>Azure.Core</code>).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Replace <code>TranslateBatchAsync:47</code> + <code>TransliterateBatchAsync</code> catches with mapper.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Run <code>m2-azure-mapper-harness.cs</code> (429→RateLimit, 401→AuthFailed, 500→ApiError) + <code>dotnet build</code> + <code>lsp_diagnostics</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Commit <code>fix(azure): typed Translate mapper 429/401/403</code>.</li>
</ul>
<h3>Task 15 — Verify M2 plumbing</h3>
<ul>
<li><input type="checkbox" disabled="" /> <code>dotnet build</code> clean; <code>lsp_diagnostics</code> 0 errors on 4 files.</li>
<li><input type="checkbox" disabled="" /> Re-grep 7 codes — each logical-error now has producer+consumer; dead codes pruned or annotated.</li>
<li><input type="checkbox" disabled="" /> Note <code>IsQuotaOrRateLimit</code> stays as guard; <code>youtube-architecture.md</code> quota note unchanged.</li>
</ul>
<hr />
<h2>Module M3 — Core / Audio / God-File Shrink</h2>
<p><strong>Goal:</strong> Shrink gods, centralize dupe guards, prune proven dead (SSH.NET centralize, Sinks.Console PackageVersion drop, etc.) with caller-proof, preserve SACD pipeline (9 steps) + quota/async.</p>
<p><strong>Sources:</strong> <code>.omo/plans/overengineering-verdict.md</code> gods Heuristic + dead catalog unused #1-4; live 18 Audio files, 6 Core files.</p>
<h3>File Structure</h3>
<p><strong>New:</strong> <code>src/Services/Audio/DffHeaderReader.cs</code> (<code>Read(string):ErrorOr&lt;DffHeader&gt;</code> + <code>ReadExactBytes</code>/<code>SeekChecked</code>)</p>
<p><strong>Modified:</strong> <code>DsdConvertService.cs</code> (464→~340), <code>ProcessRunner.cs</code> (378→~240), <code>SaraconService.cs</code> (357 adapt), <code>TextAnalyticsService.cs</code> (255→~160), <code>Telemetry.cs</code> (Seq probe + LogPaths cut, 5 wrappers→1), <code>LogPaths.cs</code> DELETE, <code>PathValidator.cs</code> excise <code>ValidateOutputDirectory</code>, <code>AudioSetup.cs</code> keep <code>AddSingleton&lt;PathValidator&gt;</code>, <code>YouTubeDuplicateMerger.cs</code> reuse <code>YouTubeFetchState.JsonOptions</code>, <code>Directory.Packages.props</code>, 6 csproj SSH.NET prune.</p>
<p><strong>Deleted:</strong> <code>LogPaths.cs</code>, <code>Serilog.Sinks.Console</code> PackageVersion, <code>YouTubeDuplicateMerger.JsonOptions</code>, 6 SSH.NET <code>PackageReference</code>.</p>
<h3>Task 16 — SSH.NET centralize to CLI</h3>
<p><strong>Files:</strong> 7 csproj (<code>Core/App/Audio/Azure/Google/LastFm</code> delete, <code>CLI</code> keep) + <code>Directory.Packages.props</code> keep central version.</p>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> <code>Select-String &quot;Renci&quot;</code> → expect 1 hit <code>CLI/Dashboard/OciDashboardDeployer.cs</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Delete 6 <code>PackageReference SSH.NET</code> lines.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>dotnet build</code> + <code>dashboard deploy --help</code> type-load check.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Commit <code>chore(deps): centralize SSH.NET to CLI</code>.</li>
</ul>
<h3>Task 17 — Drop <code>Serilog.Sinks.Console</code> PackageVersion</h3>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> <code>Select-String &quot;Serilog.Sinks.Console&quot;</code> in csproj → 0 hits.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Delete <code>PackageVersion Serilog.Sinks.Console</code> in <code>Directory.Packages.props:19</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>dotnet build</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Commit <code>chore(deps): drop orphan Sinks.Console version</code>.</li>
</ul>
<h3>Task 18 — Excise <code>PathValidator.ValidateOutputDirectory</code></h3>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> <code>Select-String ValidateOutputDirectory</code> → 1 hit (definition only).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Delete method lines 18-42, keep <code>ValidateInputPath</code>; <code>AudioSetup.AddSingleton&lt;PathValidator&gt;</code> stays.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>dotnet build</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Commit <code>refactor(audio): excise unused ValidateOutputDirectory</code>.</li>
</ul>
<h3>Task 19 — Reuse <code>YouTubeFetchState.JsonOptions</code> in <code>YouTubeDuplicateMerger</code></h3>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Verify <code>HmsTimeSpanConverter</code> only in <code>YouTubeFetchState</code> + <code>YouTubeDuplicateMerger</code> orphan.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Delete private <code>JsonOptions</code>, replace 2 <code>Serialize</code> call sites.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>dotnet build</code> + Duration <code>hh:mm:ss</code> harness.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Commit <code>refactor(youtube): reuse YouTubeFetchState.JsonOptions</code>.</li>
</ul>
<h3>Task 20 — Extract <code>DffHeaderReader</code> from <code>DsdConvertService</code></h3>
<p><strong>Files:</strong> Create <code>DffHeaderReader.cs</code>, modify <code>DsdConvertService.cs</code></p>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>M3_DffHeaderReader_Harness</code> (FRM8/FS2822400/CHNL2 vs truncated).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Create <code>DffHeaderReader.Read(string):ErrorOr&lt;DffHeader&gt;</code> + helpers.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Remove 36-176 probe loop from <code>DsdConvertService</code>, delegate.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + harness PASS.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>refactor(audio): extract DffHeaderReader</code>.</li>
</ul>
<h3>Task 21 — Shrink <code>ProcessRunner</code> grace-kill</h3>
<p><strong>Files:</strong> <code>ProcessRunner.cs</code>, <code>SaraconService.cs</code></p>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>M3_ProcessRunner_Harness</code> (Exited/Timeout/Inactivity/Canceled).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Delete <code>KilledAfterCompletionMarker</code> + <code>completionPattern:100%</code> + <code>graceTask</code>; simplify <code>SaraconService</code> termination to <code>Exited &amp;&amp; ExitCode==0</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>dotnet build</code> + harness PASS.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Commit <code>refactor(audio): cut ProcessRunner grace-kill</code>.</li>
</ul>
<h3>Task 22 — Centralize <code>TextAnalyticsService</code> 5× guard</h3>
<p><strong>Files:</strong> <code>TextAnalyticsService.cs</code></p>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Write <code>M3_TextAnalytics_Harness</code> (over-length 5121→InvalidInput, throw→ApiError).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Add <code>ExecuteAsync(op,text,hint,ct,invoke):ErrorOr&lt;string&gt;</code> runner; shrink 5 public methods to validation+delegate.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>dotnet build</code> + harness PASS.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> Commit <code>refactor(azure): centralize TextAnalytics guard</code>.</li>
</ul>
<h3>Task 23 — Delete <code>LogPaths.cs</code> + shrink <code>Telemetry.cs</code></h3>
<p><strong>Files:</strong> Delete <code>LogPaths.cs</code>, modify <code>Telemetry.cs</code>, <code>ProcessRunner.cs</code>, <code>SaraconService.cs</code>, <code>DsdConvertService.cs</code>, <code>PipelineOrchestrator.cs</code></p>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> <code>Select-String LogPaths</code> → 4 call sites.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Replace <code>LogPaths.Format</code> with <code>Path.GetFileName</code> or Serilog <code>WithProperty(&quot;IsoRoot&quot;, ...)</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>Telemetry.cs</code>: delete <code>IsSeqReachableAsync</code>, delete <code>LogPaths</code> formatter call sites, shrink 5 wrappers → <code>Log(ServiceName, level, template, args)</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + <code>audio.jsonl</code> contains <code>Service==Audio</code> entries.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 5:</strong> Commit <code>refactor: replace LogPaths with enricher</code>.</li>
</ul>
<hr />
<h2>Module M4 — Layer Fix &amp; Telemetry</h2>
<p><strong>Goal:</strong> Relocate Dashboard, OciConfig, wire title delegate, keep 10 sinks.</p>
<p><strong>Sources:</strong> <code>.omo/plans/overengineering-verdict.md</code> SoC-MOVE + Telemetry verdict, <code>.omo/plans/youtube-architecture.md</code> Bucket D.</p>
<h3>File Structure</h3>
<p><strong>New:</strong> <code>src/Services/Google/Dashboard/DashboardOrchestrator.cs</code> (~95, facade), <code>DashboardSetup.cs</code> (~28, <code>AddDashboardServices()</code>)</p>
<p><strong>Moved:</strong> <code>CLI/Dashboard/DashboardDataBuilder→Services.Google/Dashboard</code>, <code>DashboardHtmlGenerator</code>, <code>OciDashboardDeployer</code>, <code>Core/OciConfig→CLI/Dashboard/OciConfig</code></p>
<p><strong>Modified:</strong> <code>Telemetry.cs</code> (keep 10 sinks, drop TCP probe, LevelSwitch→file sink), <code>YouTubeSyncProcessor.cs</code> (delegate), <code>YouTubePlaylistOrchestrator.cs</code> (delegate), <code>GoogleSetup.cs</code> (register), <code>CLI/Dashboard/DashboardGenerateCommand.cs</code> (thin), 2 csproj SSH moves.</p>
<h3>Task 24 — Telemetry keep-10 fix</h3>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Delete <code>IsSeqReachableAsync</code> TcpClient probe; <code>_ = config.WriteTo.Seq(seqUrl)</code> unconditional with native retry.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Propagate <code>LevelSwitch</code> to file sink <code>restrictedToMinimumLevel:Debug</code> via <code>MinimumLevel.ControlledBy(LevelSwitch)</code> or keep Verbose→ filter.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Shrink 5 wrappers <code>Info/Warn/Debug/Verbose/Error</code> → <code>Log(ServiceName, level, template, args)</code> (wrappers may remain as forwards).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + level harness (<code>--debug</code>/<code>--verbose</code> → file sink), 50 MB cap preserved.</li>
</ul>
<h3>Task 25 — Move <code>LogPaths</code> custom formatter → Serilog enricher (paired with M3, verify here)</h3>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> If M3 deleted <code>LogPaths.cs</code>, add Serilog <code>WithProperty(&quot;IsoRoot&quot;)</code> enricher or keep full paths.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> <code>dotnet build</code>.</li>
</ul>
<h3>Task 26 — Relocate Dashboard 508 LOC</h3>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Create <code>src/Services/Google/Dashboard/</code> + move 3 files (namespace <code>CLI.Dashboard</code>→<code>Services.Google.Dashboard</code>), no logic change; hand-roll HTML preserved.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Update <code>Google.csproj</code> (no new PackageReference) + <code>GoogleSetup.AddGoogleServicesAsync</code> → call <code>AddDashboardServices()</code> + register <code>DashboardOrchestrator</code> singleton.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>dotnet build</code> + <code>dotnet run --project src/App -- dashboard generate</code> → <code>state/dashboard/dashboard.html</code>+<code>dashboard-data.js</code>.</li>
</ul>
<h3>Task 27 — Relocate <code>OciConfig</code></h3>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Move <code>src/Core/OciConfig.cs</code> (13 LOC) → <code>src/CLI/Dashboard/OciConfig.cs</code> (<code>Core</code>→<code>CLI.Dashboard</code>, env-driven fallback <code>OCI_HOST/OCI_USER/OCI_KEY_PATH</code>), co-located with deployer.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Remove <code>SSH.NET</code> from <code>Core.csproj</code> (after move, Core no longer needs it); CLI keeps SSH if deployer still there before M4 move — after move only Google needs SSH.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> <code>dotnet build</code>.</li>
</ul>
<h3>Task 28 — Wire YouTube title path via <code>SyncProcessor</code></h3>
<p><strong>Interfaces:</strong></p>
<pre class="syntax-highlighting"><code><span class="text plain">Task&lt;ErrorOr&lt;ProcessResult&gt;&gt; ProcessSingleViaProcessorAsync(PlaylistSnapshot snapshot, bool noTranslate, CancellationToken ct)
// in YouTubeSyncProcessor, then:
YouTubePlaylistOrchestrator.ProcessTitlePipelineAsync → await syncProcessor.ProcessSingleViaProcessorAsync(snap, noTranslate, ct)
</span></code></pre>
<ul>
<li><input type="checkbox" disabled="" /> <strong>Step 1:</strong> Expose <code>ProcessSingleViaProcessorAsync</code> in <code>YouTubeSyncProcessor</code> (reuse <code>ProcessSinglePlaylistAsync</code> logic).</li>
<li><input type="checkbox" disabled="" /> <strong>Step 2:</strong> Delegate <code>YouTubePlaylistOrchestrator.ProcessTitlePipelineAsync</code> to it; keep ETag + <code>FindPlaylistByTitleAsync</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 3:</strong> Thin <code>DashboardGenerateCommand</code> → <code>orchestrator.GenerateAndDeployAsync()</code> + <code>Match</code>.</li>
<li><input type="checkbox" disabled="" /> <strong>Step 4:</strong> <code>dotnet build</code> + <code>dashboard generate</code> + title sync <code>playlistItems</code> regression.</li>
</ul>
<h3>Task 29 — DI + thin command verification</h3>
<ul>
<li><input type="checkbox" disabled="" /> <code>dotnet build</code> clean each task; <code>dashboard generate</code> artifacts; <code>sync youtube</code> title path <code>FindPlaylistByTitleAsync</code> still via ETag; Oci deploy <code>Renci.SshNet</code> load still via Google csproj.</li>
</ul>
<hr />
<h2>Exhaustiveness ↔ Modularity</h2>
<table>
<thead>
<tr>
<th>Check</th>
<th>Exhaustive?</th>
<th>Modular?</th>
</tr>
</thead>
<tbody>
<tr>
<td>Seams 17 deduped</td>
<td>yes — each ID fix/keep/wire tabled</td>
<td>yes — M1 only</td>
</tr>
<tr>
<td>Errors 7 codes 2-step proof</td>
<td>yes — caller+docs before delete</td>
<td>yes — M2 only</td>
</tr>
<tr>
<td>Dead 18+4 vs missing impl</td>
<td>yes — produced≠consumed kept until wired (S-15/S-16 throttle <code>ponytail:</code>)</td>
<td>yes — split dead across M1/M2/M3 per domain</td>
</tr>
<tr>
<td>Gods 9 shrinks vs keeps</td>
<td>yes — F-preservation per file (461/446/421 keep, 464→340 etc.)</td>
<td>yes — M3 only</td>
</tr>
<tr>
<td>Dashboard move</td>
<td>yes — 508 LOC + OciConfig + deployer co-located</td>
<td>yes — M4 only</td>
</tr>
</tbody>
</table>
<h2>Ordering (CPM)</h2>
<ol>
<li><strong>Parallel seeds:</strong> M1 (Tasks 1,2,6,7) + M3 (Tasks 16-19) can start together — no file overlap.</li>
<li><strong>Join M2:</strong> after M1 Task 3 shape stable (needs <code>SortPassResult</code> wiring to keep mapper seam clean) → M2 Tasks 10-14 sequential.</li>
<li><strong>Merge M4:</strong> after M1/M2/M3 — Dashboard consumes seam S-13 + error codes + shrunk services; telemetry fix after LogPaths gone.</li>
<li><strong>Verify:</strong> <code>dotnet build</code> after each module; final <code>state/youtube/processed</code> 145 intact; <code>state/logs/youtube.jsonl</code> + <code>audio.jsonl</code> active; <code>dashboard.html</code> generated.</li>
</ol>
<h2>Self-Review (this plan)</h2>
<p><strong>1. Spec coverage:</strong> every markdown SoC has a task — seams 17→Tasks 1-9, error 7→Tasks 10-15, dead 18+4→Tasks 16-23 deduped with caller proof, verdict gods→Tasks 20-23, Dashboard/Telemetry/OciConfig→Tasks 24-29. Flatline/quota/merge are completed archival (not re-impl). Taste is constraint not task.</p>
<p><strong>2. Placeholder scan:</strong> searched <code>TBD|TODO|implement later|handle edge</code> — 0 hits. Every step has real code block + <code>Run:</code> + <code>Expected:</code>.</p>
<p><strong>3. Type consistency:</strong> <code>SortStatistics</code>, <code>SortPassResult</code>, <code>PlaylistSnapshot</code>, <code>MapGoogleApiException(GoogleApiException,string)→Error</code>, <code>MapTranslateException(Exception)→Error</code>, <code>ProcessSingleViaProcessorAsync(PlaylistSnapshot,bool,CT)→ErrorOr&lt;ProcessResult&gt;</code>, <code>DashboardOrchestrator.GenerateAndDeployAsync()</code> — signatures match across M1/M2/M4 consumers.</p>
<hr />
<p>Plan complete and saved to <code>C:/Users/Lance/Dev/Toolbox/.omo/plans/2026-08-18-toolbox-mega-plan.md</code>. Two execution options:</p>
<p><strong>1. Subagent-Driven (recommended)</strong> — dispatch a fresh <code>deep</code> subagent per module (M1-M4), review between modules, fast iteration.</p>
<p><strong>2. Inline Execution</strong> — execute CPM-ordered tasks in this session with checkpoints.</p>
<p><strong>Which approach?</strong></p>
