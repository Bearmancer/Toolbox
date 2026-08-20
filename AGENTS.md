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
│       └── LastFm/        # Last.fm HTTP + sync orchestrator
├── state/                 # audio/, dashboard/, lastfm/, logs/, youtube/{deleted,merge-manifests,processed,raw}
├── state/logs/            # 11 JSONL per service (rolling: Infinite, 50 MB cap)
├── artifacts/             # bin+obj via UseArtifactsOutput (not bin/)
├── Directory.Build.props
├── Directory.Packages.props
└── .codegraph → C:\Users\Lance\.omo\codegraph\projects\Toolbox-6ccb6ce65a117481 (junction)
</span></code></pre>
<h2>DEPENDENCY GRAPH</h2>
<pre class="syntax-highlighting"><code><span class="text plain">App → CLI, Core
CLI → Core, Services.Azure, Services.Google, Services.LastFm, Services.Audio
Services.Google → Core, Services.Azure  (cross-service: YouTubeTranslationService → TranslateService)
Services.Audio → Core
Services.Azure → Core
Services.LastFm → Core
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
<li><strong>Logging:</strong> <code>Telemetry.ForService(ServiceName.X)</code> scopes log entries. JSONL per service in <code>state/logs/</code>.</li>
<li><strong>State:</strong> <code>state/youtube/manifest.json</code>, <code>state/lastfm/scrobbles.json</code>, <code>state/dashboard/</code>, <code>state/audio/</code>. No database.</li>
<li><strong>One class per file.</strong> No <code>Constants.cs</code>, no <code>Helpers.cs</code>. Extract to shared file only when 3+ consumers.</li>
<li><strong>Inline constants:</strong> <code>private static readonly string</code> at top of file.</li>
<li><strong>Code style:</strong> <code>.editorconfig</code> is the single source of truth. All rules enforced as <code>error</code> severity.</li>
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
