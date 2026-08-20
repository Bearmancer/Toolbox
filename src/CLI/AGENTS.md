<h1>CLI Layer</h1>
<p>Spectre.Console.Cli thin wrappers — <code>Settings</code> → service → <code>result.Match</code> → 0/1. No business logic.</p>
<h2>STRUCTURE</h2>
<pre class="syntax-highlighting"><code><span class="text plain">CLI/
├── TypeRegistrar.cs              # Spectre ITypeRegistrar → IServiceProvider bridge
├── Azure/
│   ├── AzureCommandModule.cs     # &quot;azure&quot; branch: translate, docintel, vision, stt, tts, ner, phrases
│   ├── TranslateCommand.cs
│   ├── DocIntelCommand.cs
│   ├── VisionCommand.cs
│   ├── SpeechSttCommand.cs
│   ├── SpeechTtsCommand.cs
│   ├── NerCommand.cs
│   └── PhrasesCommand.cs
├── Audio/
│   ├── AudioCommandModule.cs     # &quot;audio&quot; branch: sacd-convert, dsd-convert
│   ├── SacdConvertCommand.cs
│   └── DsdConvertCommand.cs
├── Dashboard/
│   ├── DashboardCommandModule.cs # "dashboard" branch: generate
│   └── DashboardGenerateCommand.cs
└── Sync/
    ├── SyncCommandModule.cs      # &quot;sync&quot; branch: youtube, lastfm
    ├── YouTube/SyncYoutubeCommand.cs
    └── LastFm/SyncLastFmCommand.cs
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
<td>Add Azure subcommand</td>
<td><code>Azure/AzureCommandModule.cs</code></td>
<td><code>AddCommand&lt;T&gt;</code> in <code>AddBranch(&quot;azure&quot;)</code>, new <code>AsyncCommand&lt;Settings&gt;</code></td>
</tr>
<tr>
<td>Add Audio subcommand</td>
<td><code>Audio/AudioCommandModule.cs</code></td>
<td>Same pattern</td>
</tr>
<tr>
<td>Add Dashboard subcommand</td>
<td><code>Dashboard/DashboardCommandModule.cs</code></td>
<td>Same pattern</td>
</tr>
<tr>
<td>Command pattern</td>
<td>Any <code>*Command.cs</code></td>
<td><code>AsyncCommand&lt;Settings&gt;</code> → <code>service.CallAsync(ct)</code> → <code>result.Match</code> → exit code</td>
</tr>
</tbody>
</table>
<h2>CONVENTIONS</h2>
<ul>
<li><strong>Thin only.</strong> <code>ExecuteAsync(Settings, CancellationToken)</code> → service <code>ErrorOr&lt;T&gt;</code> → <code>Match(onSuccess→0, onError→1)</code>. No orchestration.</li>
<li><strong>No business logic.</strong> Merge/state/pagination/ETag → <code>Services/</code> orchestrator.</li>
<li><strong>Result matching.</strong> <code>result.Match(v =&gt; { Console.WriteLine(v); return 0; }, e =&gt; { Console.Error.WriteLine(e.Description); return 1; })</code></li>
<li><strong>Cancellation.</strong> <code>CancellationToken ct</code> from Spectre <code>ExecuteAsync</code> signature — pass to service.</li>
</ul>
<h2>ANTI-PATTERNS</h2>
<ul>
<li><strong>NEVER</strong> merge/state/pagination in command — extract to service layer.</li>
<li><strong>NEVER</strong> import <code>Core</code> for business logic — CLI uses <code>Core</code> only for <code>Telemetry</code>/<code>Errors</code>.</li>
</ul>
