<h1>AGENTS.md — Toolbox</h1>
<p><strong>Generated:</strong> 2026-07-31 | <strong>Commit:</strong> 70dd931 | <strong>Branch:</strong> main</p>
<p>Extends <code>C:\Users\Lance\.config\opencode\AGENTS.md</code>. All Sisyphus directives apply.</p>
<h2>OVERVIEW</h2>
<p>CLI toolbox wrapping Azure AI services, Google YouTube API, Last.fm, and SACD audio conversion. .NET 11.0, Spectre.Console.Cli, Serilog, ErrorOr.</p>
<h2>STRUCTURE</h2>
<pre class="syntax-highlighting"><code><span class="text plain">Toolbox/
├── Toolbox.slnx           # 8 projects: App, CLI, Core, Audio, Azure, Google, LastFm, Pristine
├── .editorconfig          # Code style — single source of truth
├── src/
│   ├── App/               # Exe. DI wiring only.
│   ├── CLI/               # Spectre.Console.Cli. No service logic.
│   │   ├── Azure/         # translate, docintel, vision, stt, ner, phrases
│   │   ├── Audio/         # sacd-convert, dsd-convert
│   │   ├── Dashboard/     # generate + OCI deploy
│   │   └── Sync/          # youtube, lastfm
│   ├── Core/              # Telemetry, errors, path resolution, text utils
│   └── Services/
│       ├── Audio/         # SACD ISO → DSD→FLAC (sacd_extract, saracon, sox)
│       ├── Azure/         # Vision, Translate, Speech, DocIntel, OpenAI, TextAnalytics
│       ├── Google/YouTube/# YouTube API + orchestration. Depends on Azure.TranslateService
│       ├── LastFm/        # Last.fm HTTP + sync orchestrator
│       └── Pristine/      # PASC downloader: direct-API + browser fallback, auto 16-bit transcode
├── state/                 # audio/, dashboard/, lastfm/, logs/, youtube/{deleted,merge-manifests,processed,raw}
├── state/logs/            # 11 JSONL per service (rolling: Infinite, 50 MB cap)
├── artifacts/             # bin+obj via UseArtifactsOutput (not bin/)
├── Directory.Build.props
├── Directory.Packages.props
└── .codegraph → C:\Users\Lance\.omo\codegraph\projects\Toolbox-6ccb6ce65a117481 (junction)
</span></code></pre>
<h2>DEPENDENCY GRAPH</h2>
<pre class="syntax-highlighting"><code><span class="text plain">App → CLI, Core, Services.Pristine
CLI → Core, Services.Azure, Services.Google, Services.LastFm, Services.Audio, Services.Pristine
Services.Google → Core, Services.Azure  (cross-service: YouTubeTranslationService → TranslateService)
Services.Audio → Core
Services.Azure → Core
Services.LastFm → Core
Services.Pristine → Core, Services.Audio
</span></code></pre>
<h2>WHERE TO LOOK</h2>
<table>
<thead>
<tr>
<th>Task</th>
<th>Location</th>
<th>Notes</th>
</tr>
</thead>
<tbody>
<tr>
<td>Add CLI command</td>
<td><code>src/CLI/{Domain}/</code></td>
<td>Follow Spectre pattern: thin command → service call → Result.Match</td>
</tr>
<tr>
<td>Add Azure service</td>
<td><code>src/Services/Azure/</code></td>
<td>Add credential to AzureCredentials.cs, register in AzureSetup.cs</td>
</tr>
<tr>
<td>Add Google/YouTube feature</td>
<td><code>src/Services/Google/YouTube/</code></td>
<td>Orchestrator handles state; processor handles per-playlist logic</td>
</tr>
<tr>
<td>Add Last.fm feature</td>
<td><code>src/Services/LastFm/</code></td>
<td>LastFmApiClient for HTTP, LastFmSyncOrchestrator for sync flow</td>
</tr>
<tr>
<td>Add audio conversion</td>
<td><code>src/Services/Audio/</code></td>
<td>DsdConvertService is facade; PipelineOrchestrator sequences</td>
</tr>
<tr>
<td>Add Pristine feature</td>
<td><code>src/Services/Pristine/</code></td>
<td>PristineOrchestrator sequences; PristineApiClient is the direct-API success path, PristineBrowser/PristineAlbumService/PristinePollService the per-album fallback</td>
</tr>
<tr>
<td>Dashboard generation</td>
<td><code>src/Services/Google/Dashboard/</code></td>
<td>DashboardDataBuilder → DashboardHtmlGenerator → OciDashboardDeployer</td>
</tr>
<tr>
<td>Modify telemetry</td>
<td><code>src/Core/Telemetry.cs</code></td>
<td>Per-service JSONL + optional Seq sink</td>
</tr>
<tr>
<td>Add error codes</td>
<td><code>src/Core/Errors.cs</code></td>
<td>Central taxonomy; add factory method per domain</td>
</tr>
<tr>
<td>Change build config</td>
<td><code>Directory.Build.props</code></td>
<td>Single source for TargetFramework, analyzers, warnings</td>
</tr>
<tr>
<td>Change code style</td>
<td><code>.editorconfig</code></td>
<td>Naming, var usage, patterns, diagnostics — all as errors</td>
</tr>
</tbody>
</table>
<h2>CONVENTIONS</h2>
<ul>
<li><strong>Auth:</strong> <code>.env</code> only. No hardcoded secrets. <code>AzureCredentials.Read()</code>, <code>GoogleCredentials.Read()</code>, env vars in LastFmSetup.</li>
<li><strong>DI registration:</strong> Extension methods using C# <code>extension(IServiceCollection)</code> syntax in each service's <code>*Setup.cs</code>.</li>
<li><strong>Error handling:</strong> <code>ErrorOr&lt;T&gt;</code> railway-oriented. <code>result.Match(onSuccess, onError)</code>. Error factories in <code>Errors.cs</code>.</li>
<li><strong>JSON:</strong> PascalCase properties. <code>JsonSerializerOptions { WriteIndented = true }</code> only. No <code>PropertyNamingPolicy</code>.</li>
<li><strong>Logging:</strong> <code>Telemetry.ForService(ServiceName.X)</code> scopes log entries. JSONL per service in <code>state/logs/</code>. <code>Info</code>/<code>Warn</code> are user-facing console output — plain sentence, no <code>Key=Value</code> field-soup; that style is Debug/Verbose-only (see <code>src/Core/AGENTS.md</code>).</li>
<li><strong>State:</strong> <code>state/youtube/manifest.json</code>, <code>state/lastfm/scrobbles.json</code>, <code>state/dashboard/</code>, <code>state/audio/</code>. No database.</li>
<li><strong>One class per file.</strong> No <code>Constants.cs</code>, no <code>Helpers.cs</code>. Extract to shared file only when 3+ consumers.</li>
<li><strong>Inline constants:</strong> <code>private static readonly string</code> at top of file.</li>
<li><strong>Code style:</strong> <code>.editorconfig</code> is the single source of truth. All rules enforced as <code>error</code> severity.</li>
</ul>
<h2>WORKING STYLE</h2>
<ul>
<li><strong>Research independently first.</strong> Verify assumptions and look for holes rather than presuming an existing plan is correct.</li>
<li><strong>Diagnose from actual runtime logs/config on failure.</strong> Don't speculate or retry the same operation blind.</li>
<li><strong>Prefer <code>rg</code>/<code>fd</code></strong> for file discovery and content search over PowerShell-native enumeration (<code>Get-ChildItem</code>/<code>Get-Content</code>).</li>
<li><strong>Deliverables (plans, specs) should be comprehensive and implementation-ready</strong>, not brief summaries.</li>
<li><strong>Once an approach is settled, execute immediately.</strong> Don't over-deliberate or seek repeated confirmation on a decision already made.</li>
<li><strong>Measure before cutting.</strong> For large exports/archives/deliverables, show where the size actually goes before deciding what to trim.</li>
</ul>
<h2>RULES</h2>
<ol>
<li><strong>Build-verify every edit.</strong> Change one file → <code>dotnet build</code> → verify clean.</li>
<li><strong>Commit after each phase.</strong> 1–3 files per commit. Atomic, revertable, descriptive message.</li>
<li><strong>Minimize file sprawl.</strong> One class per file. No <code>Constants.cs</code>, no <code>Helpers.cs</code>.</li>
<li><strong>No test NuGet packages.</strong> No xUnit, NUnit, MSTest. Standalone <code>.cs</code> files with <code>Main()</code> for manual verification.</li>
<li><strong><code>Directory.Build.props</code>/<code>.csproj</code> exclusively.</strong> No <code>Directory.Build.targets</code>, no extra props files.</li>
<li><strong>Never skip style warnings.</strong> No <code>#pragma warning disable</code>, no suppression attributes.</li>
<li><strong>PascalCase JSON — never set PropertyNamingPolicy.</strong></li>
<li><strong>Inline paths, keys, defaults.</strong> <code>private static readonly string</code> at top of file.</li>
<li><strong>Zero inline/explanatory comments.</strong> Code is self-documenting. XML docs only where required.</li>
<li><strong>Run <code>dprint fmt .</code> after any set of file edits</strong>, before reporting work as done or ready, and again before every <code>git commit</code>/<code>git push</code>. A pre-commit hook (<code>.git/hooks/pre-commit</code>) formats staged files and a pre-push hook (<code>.git/hooks/pre-push</code>) blocks pushing unformatted code, but don't rely on the hooks alone — run it yourself first so formatting diffs land in the commit you intend, not a surprise amend. <code>.git/hooks/</code> is untracked and won't survive a fresh clone.</li>
</ol>
<h2>ANTI-PATTERNS (THIS PROJECT)</h2>
<ul>
<li><strong>NEVER</strong> <code>global::</code> — use <code>using</code> aliases.</li>
<li><strong>NEVER</strong> fully-qualified invocations inline — import via <code>using</code>.</li>
<li><strong>NEVER</strong> <code>#pragma warning disable</code> or suppression attributes.</li>
<li><strong>NEVER</strong> <code>PropertyNamingPolicy</code> on <code>JsonSerializerOptions</code>.</li>
<li><strong>NEVER</strong> test NuGet packages (xUnit, NUnit, MSTest). Standalone <code>.cs</code> with <code>Main()</code> only.</li>
<li><strong>NEVER</strong> <code>Directory.Build.targets</code> or extra props files.</li>
<li><strong>NEVER</strong> inline/explanatory comments.</li>
<li><strong>NEVER</strong> destructive git operations (<code>filter-branch</code>, <code>reflog expire</code>, <code>gc --prune</code>, <code>reset --hard</code>, force-push, branch deletion, rebase rewrites, <code>checkout --force</code>) without explicit confirmation. Investigation stays read-only; history-rewriting experiments run on temp clones, never the real repo.</li>
</ul>
<h2>COMMANDS</h2>
<pre class="syntax-highlighting"><code><span class="source shell bash"><span class="meta function-call shell"><span class="variable function shell">dotnet</span></span><span class="meta function-call arguments shell"> build                          <span class="comment line number-sign shell"><span class="punctuation definition comment begin shell">#</span></span><span class="comment line number-sign shell"> Build all projects</span><span class="comment line number-sign shell">
</span></span><span class="meta function-call shell"><span class="variable function shell">dotnet</span></span><span class="meta function-call arguments shell"> run<span class="variable parameter option shell"><span class="punctuation definition parameter shell"> --</span>project</span> src<span class="constant character escape shell">\A</span>pp<span class="keyword operator end-of-options shell"> --</span></span><span class="meta function-call arguments shell"> <span class="keyword operator assignment redirection shell">&lt;</span>cmd<span class="keyword operator assignment redirection shell">&gt;</span> # Run CLI command</span>
<span class="meta function-call shell"><span class="variable function shell">dotnet</span></span><span class="meta function-call arguments shell"> run<span class="variable parameter option shell"><span class="punctuation definition parameter shell"> --</span>project</span> src<span class="constant character escape shell">\A</span>pp<span class="keyword operator end-of-options shell"> --</span></span><span class="meta function-call arguments shell"> sync youtube</span>
<span class="meta function-call shell"><span class="variable function shell">dotnet</span></span><span class="meta function-call arguments shell"> run<span class="variable parameter option shell"><span class="punctuation definition parameter shell"> --</span>project</span> src<span class="constant character escape shell">\A</span>pp<span class="keyword operator end-of-options shell"> --</span></span><span class="meta function-call arguments shell"> sync lastfm</span>
<span class="meta function-call shell"><span class="variable function shell">dotnet</span></span><span class="meta function-call arguments shell"> run<span class="variable parameter option shell"><span class="punctuation definition parameter shell"> --</span>project</span> src<span class="constant character escape shell">\A</span>pp<span class="keyword operator end-of-options shell"> --</span></span><span class="meta function-call arguments shell"> audio sacd-convert <span class="keyword operator assignment redirection shell">&lt;</span>iso<span class="keyword operator assignment redirection shell">&gt;</span>
</span><span class="meta function-call shell"><span class="variable function shell">dotnet</span></span><span class="meta function-call arguments shell"> run<span class="variable parameter option shell"><span class="punctuation definition parameter shell"> --</span>project</span> src<span class="constant character escape shell">\A</span>pp<span class="keyword operator end-of-options shell"> --</span></span><span class="meta function-call arguments shell"> azure translate</span>
<span class="meta function-call shell"><span class="variable function shell">dotnet</span></span><span class="meta function-call arguments shell"> run<span class="variable parameter option shell"><span class="punctuation definition parameter shell"> --</span>project</span> src<span class="constant character escape shell">\A</span>pp<span class="keyword operator end-of-options shell"> --</span></span><span class="meta function-call arguments shell"> dashboard generate</span>
</span></code></pre>
<h3>Saracon CLI</h3>
<p>Headless only — never GUI. Shape built in <code>src/Services/Audio/SaraconService.cs</code> (<code>saracon</code> from <code>PATH</code>):</p>
<pre class="syntax-highlighting"><code><span class="text plain">saracon -c d2p -r &lt;sample-rate&gt; -f wav -n &lt;bit-depth&gt;bit -d tpdf -g &lt;gain-db&gt; -T -V all -t &quot;&lt;output-directory&gt;&quot; &quot;&lt;input.dff&gt;&quot;
</span></code></pre>
<p><code>-c d2p</code> = DSD→PCM, <code>-t</code> = output dir, final arg = input .dff. Omit <code>--format</code> (default <code>Bit16</code>); <code>--format</code> accepts <code>16</code>, <code>24</code>, <code>Bit16</code>, or <code>Bit24</code> (case-insensitive).</p>
<h2>NOTES</h2>
<ul>
<li>.NET 11.0 preview SDK required. <code>SuppressNETCoreSdkPreviewMessage</code> is set.</li>
<li><code>&lt;UseArtifactsOutput&gt;true&lt;/UseArtifactsOutput&gt;</code> — outputs in <code>artifacts/</code>, not <code>bin/</code>.</li>
<li><code>.editorconfig</code> is source of truth. All rules enforced as <code>error</code>.</li>
<li><code>Toolbox.slnx</code> is SDK-style, 8 projects.</li>
<li>No CI/CD (no <code>.github/</code>). No <code>tools/</code> dir. Manual builds.</li>
<li>No test projects. Standalone <code>.cs</code> with <code>Main()</code> only.</li>
<li>Sub-AGENTS.md in <code>src/CLI/</code>, <code>src/Core/</code>, <code>src/Services/Audio/</code>, <code>src/Services/Azure/</code>, <code>src/Services/Google/</code>, <code>src/Services/LastFm/</code>, <code>src/Services/Pristine/</code>.</li>
</ul>
<h2>RESOLVED AUDITS</h2>
<p><code>erroror_migration_assessment.md</code> and <code>ponytail_audit_verified.md</code> (formerly <code>.omo/plans/</code>) closed out 2026-08-21 per the plans directory's own policy — outcome folded here, reasoning trail in git log.</p>
<ul>
<li><strong>ErrorOr migration:</strong> 7 of 9 candidate methods converted to <code>ErrorOr&lt;T&gt;</code> — <code>PristineDownloader.DownloadAsync</code>, <code>PristineBrowser.CreateAsync</code>, <code>PristineAlbumService.StartPlaybackAsync</code>/<code>DownloadArtworkAndPdfAsync</code>, <code>PristinePollService.DownloadSingleAlbumAsync</code>, <code>OciDashboardDeployer.DeployAsync</code>, <code>YouTubeSyncProcessor.SortPlaylistsAsync</code>. <code>LastFmApiException</code> deleted; <code>LastFmApiClient</code>'s throw/ErrorOr mixing fixed (HTTP failures now caught and returned as <code>ErrorOr</code> inside <code>ExecuteHttpRequestAsync</code> itself).</li>
<li><strong>Deliberately not converted:</strong> <code>PristinePollService.WaitForLoginAsync</code> and <code>PristineOrchestrator.WaitForLoginAsync</code> stay separate near-duplicates — consolidating requires forcing the real browser-fallback path, which means invalidating live Pristine session cookies against the paid account. Not worth it for a dedup. <code>ProcessRunnerCanceledException</code> stays a thrown exception (it's an <code>OperationCanceledException</code> subtype; the audit's own later "let cancellation propagate naturally" principle overrides its earlier smell-point table entry for this exact case).</li>
<li><strong>Ponytail cleanup landed:</strong> dead code removed (<code>PristineDownloadConfig</code>, <code>PRISTINE_HEADLESS</code> env var, unused <code>--from</code> flag on <code>TranslateCommand</code>, <code>old/Scripts/.git</code> nested repo + abandoned Postgres cluster); <code>YouTubeDuplicateMerger</code> and <code>DsdConvertCommand</code> god methods split (behavior-preserving); <code>DashboardHtmlGenerator</code>'s 364-line HTML string extracted to an embedded template asset; <code>SyncYoutubeCommand</code>'s duplicated dashboard-regen logic now routes through <code>DashboardOrchestrator</code>.</li>
<li><strong>Checked and rejected</strong> (claim didn't hold up): deleting <code>ErrorOr</code>/Serilog/<code>Core.Errors</code>, replacing <code>ProcessRunner</code> with bare <code>WaitForExitAsync</code>, removing Azure's manual <code>Telemetry.StartActivity</code> wrappers (nothing else subscribes to Azure SDK's native spans — would be a telemetry-visibility regression), consolidating <code>FlacCompletenessChecker</code>+<code>DiscOutputInspector</code>, native Spectre binding for <code>PristineDownloadCommand.NormalizeCodes</code> (handles a case Spectre can't do natively) or <code>SyncLastFmCommand --since</code> (would lose the custom error message + telemetry record), and collapsing the <code>*CommandModule</code> pattern or <code>SyncYoutubeCommand</code>'s 2×2 dispatch tree (both are consistent, documented conventions, not sprawl).</li>
</ul>
