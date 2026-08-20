<h1>Core — Zero-Dep Utility Layer</h1>
<p>Leaf. All <code>Services.*</code> → Core. Never reverse. No service knowledge here.</p>
<h2>Structure</h2>
<pre class="syntax-highlighting"><code><span class="text plain">Core/
├── Core.csproj     # ErrorOr, Serilog+Compact/File/Seq/Spectre/Tracing, Spectre.Console
├── Telemetry.cs    # Per-service JSONL (CompactJsonFormatter, 50 MB) + Spectre console + Seq probe
├── Errors.cs       # Domain factories: Validation/YouTube/Azure/LastFm/DocIntel/Speech/Vision/Translate/TextAnalytics/Audio/Pristine
├── PathResolver.cs # RepoRoot, GetStatePath(), ResolveInput(), ReadChecked()
└── Text.cs         # SanitizeFileName + string extensions (IsEqualTo)
</span></code></pre>
<p><code>Telemetry.cs</code>: <code>state/logs</code> = <code>Path.Combine(RepoRoot,&quot;state&quot;,&quot;logs&quot;)</code>; one JSONL per <code>ServiceName</code> via <code>Enum.GetValues</code>→<code>AddServiceLogger</code> filtering on <code>Service==service.ToString()</code>; <code>LevelSwitch</code> controls Spectre sink; Seq at <code>SEQ_URL</code> gated by SEQ_URL env var presence (no TCP probe).
<code>ServiceName.cs</code>: <code>LastFm, YouTube, Vision, Translate, TextAnalytics, Speech, DocIntel, Audio, Pristine, SdkDiagnostics</code> → <code>ToFileSlug()</code> maps <code>TextAnalytics→textanalytics</code>, <code>Pristine→pristine</code>, <code>SdkDiagnostics→sdk</code>.
<code>PathResolver.cs</code>: <code>RepoRoot</code> lazy walks ≤10 parents for <code>.git</code>/<code>.env</code>, fallback <code>GetCurrentDirectory()</code>; <code>ResolveInput()</code> resolves relative via <code>resources/</code>; <code>ReadChecked(path,maxBytes,service)</code>.</p>
<h2>Where to Look</h2>
<table>
<thead>
<tr>
<th>Task</th>
<th>File</th>
<th>Note</th>
</tr>
</thead>
<tbody>
<tr>
<td>Add error category</td>
<td><code>Errors.cs</code></td>
<td>Add <code>static class Errors.{Domain}</code> with <code>Error.*</code> factories</td>
</tr>
<tr>
<td>Add service to telemetry</td>
<td><code>ServiceName.cs</code> + <code>Telemetry.cs</code></td>
<td>Add enum value + <code>ToFileSlug</code> case; <code>Configure()</code> auto-creates logger via <code>Enum.GetValues</code></td>
</tr>
<tr>
<td>Change log format/sink</td>
<td><code>Telemetry.cs</code></td>
<td><code>AddServiceLogger()</code> owns file sink; <code>Configure()</code> owns Spectre+Seq</td>
</tr>
<tr>
<td>Resolve paths/state</td>
<td><code>PathResolver.cs</code></td>
<td><code>RepoRoot</code>, <code>GetStatePath(subdir)</code>, <code>ResolveInput()</code>, <code>ReadChecked()</code></td>
</tr>
<tr>
<td>OCI deploy target</td>
<td><code>src/Services/Google/Dashboard/OciConfig.cs</code></td>
<td><code>Host</code>/<code>User</code>/<code>KeyPath</code> — consumed by dashboard deploy</td>
</tr>
</tbody>
</table>
<h2>Conventions</h2>
<ul>
<li>Fallible → <code>ErrorOr&lt;T&gt;</code> via <code>Errors.{Domain}</code> factories (<code>Errors.cs</code> taxonomy, not ad-hoc <code>Error.Failure</code>).</li>
<li>Telemetry scope: <code>using var _ = Telemetry.ForService(ServiceName.X);</code> pushes <code>Service</code> property for <code>AddServiceLogger</code> filter.</li>
<li>Paths via <code>PathResolver.RepoRoot</code>/<code>GetStatePath()</code> — never hardcode <code>state/</code>.</li>
<li>No <code>Services.*</code> references. No domain logic. Pure utilities.</li>
</ul>
<h2>Anti-Patterns</h2>
<ul>
<li><strong>NEVER</strong> add service-specific logic to Core.</li>
<li><strong>NEVER</strong> add <code>ServiceName</code> value without <code>ToFileSlug()</code> case — <code>Configure()</code> will throw <code>ArgumentOutOfRangeException</code>.</li>
<li><strong>NEVER</strong> bypass <code>PathResolver</code> with <code>Directory.GetCurrentDirectory()</code> — breaks when <code>AppContext.BaseDirectory</code> ≠ CWD.</li>
</ul>
