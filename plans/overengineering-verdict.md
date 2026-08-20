<h1>Overengineering vs SRP vs SoC — Master Verdict</h1>
<p><strong>Source:</strong> <code>toolbox-consolidated-spec.md</code> §7
<strong>Constraint:</strong> solo dev, not enterprise. Single interface + single impl with no test seam = overhead. Clean code + architectural clarity paramount.
<strong>Verified:</strong> 2026-08-18 | Live: <code>src/Services/Google/YouTube</code> (12 files), <code>src/Services/Audio</code> (18 files), <code>src/Core</code> (6 files), <code>src/CLI</code> (~22 files)</p>
<table>
<thead>
<tr>
<th>Bucket</th>
<th>Verdict</th>
<th>Examples</th>
<th>Rationale</th>
</tr>
</thead>
<tbody>
<tr>
<td>True overengineering — CUT</td>
<td>delete/shrink</td>
<td>dead harness, 13 dead factories, 5 dead result fields, 84 LOC repeated <code>Result.Match</code>, 10 sinks for 8 empty files</td>
<td>dead, duped, or speculative. No second consumer/caller/behavior</td>
</tr>
<tr>
<td>Legit SRP — KEEP</td>
<td>keep split</td>
<td><code>PipelineOrch</code> vs <code>DsdConvert</code> (merging→900 LOC god); <code>Saracon/Sox/SacdExtract</code> (distinct binaries); LIS sort (quota); <code>MergePolicy</code> (destructive rules)</td>
<td>merging creates gods or collapses distinct external contracts</td>
</tr>
<tr>
<td>Correct SoC, wrong layer — MOVE</td>
<td>relocate</td>
<td>Dashboard 508 LOC in CLI → Services; <code>OciConfig</code> in Core → CLI/Dashboard env</td>
<td>separation correct, location violates layering</td>
</tr>
<tr>
<td>Adapter necessity — KEEP</td>
<td>keep</td>
<td>3 <code>EventListener</code>s (incompatible SDKs); <code>Google→Azure</code> direct dep</td>
<td>generic façade hides, not reduces</td>
</tr>
<tr>
<td>Gray — lean keep</td>
<td>keep until 2nd consumer</td>
<td><code>YouTubeChangeDetector</code> (62 LOC 1 caller), 4 command modules, <code>FlacChecker</code> statics</td>
<td>YAGNI says inline; SRP says testable. Inline only if &lt;60 LOC AND untested</td>
</tr>
</tbody>
</table>
<h2>When Is It Overengineering? (Solo-Dev Heuristic)</h2>
<ol>
<li>Single interface, single impl, no test seam → delete interface.</li>
<li>Wrapper adding 0 behavior (<code>Text.IsEqualTo</code> over <code>string.Equals</code>) → inline.</li>
<li>File-per-trivial-thing (<code>DiscState</code> 10 LOC enum, <code>PathValidator</code> 25 LOC 0 callers) → merge/delete.</li>
<li>Speculative generality (<code>ProcessRunner</code> <code>TerminationReason</code> branches for 1 path) → cut to used.</li>
<li>Copy-paste abstraction (5× identical <code>TextAnalytics</code> guard) → <em>overengineering by duplication</em>; extract shared runner.</li>
<li><strong>NOT</strong> overengineering: distinct binary wrappers (<code>Saracon ≠ Sox ≠ SacdExtract</code>), quota-critical algos (LIS sort), destructive policy separation (<code>MergePolicy</code>), SDK adapter necessity (3 <code>EventListener</code>s).</li>
</ol>
<h2>Telemetry Verdict</h2>
<table>
<thead>
<tr>
<th>Pattern</th>
<th>Verdict</th>
<th>Rationale</th>
</tr>
</thead>
<tbody>
<tr>
<td>10 per-service JSONL loggers</td>
<td><strong>KEEP</strong> (fix, not cut)</td>
<td>routing correct; empty files = missing callers not bad architecture</td>
</tr>
<tr>
<td><code>Seq</code> TCP probe</td>
<td><strong>CUT</strong></td>
<td>native Serilog retry sufficient</td>
</tr>
<tr>
<td><code>LogPaths</code> custom formatter</td>
<td><strong>CUT</strong></td>
<td>Serilog Enricher does it</td>
</tr>
<tr>
<td>5 one-line <code>Telemetry</code> wrappers</td>
<td><strong>SHRINK</strong></td>
<td>one <code>Telemetry.Log(ServiceName, level, template)</code></td>
</tr>
</tbody>
</table>
<h2>God Files (Reconciled to Live LOC)</h2>
<table>
<thead>
<tr>
<th>File</th>
<th>LOC</th>
<th>Verdict</th>
</tr>
</thead>
<tbody>
<tr>
<td><code>PipelineOrchestrator.cs</code></td>
<td>~461</td>
<td>keep — 1 job, borderline not true god</td>
</tr>
<tr>
<td><code>YouTubeDuplicateMerger.cs</code></td>
<td>~386</td>
<td>keep — destructive workflow density</td>
</tr>
<tr>
<td><code>DsdConvertService.cs</code></td>
<td>~410</td>
<td>shrink — extract <code>DffHeaderReader</code> → ~340</td>
</tr>
<tr>
<td><code>YouTubeSortService.cs</code></td>
<td>~369</td>
<td>keep — LIS algorithmic density</td>
</tr>
<tr>
<td><code>YouTubePlaylistOrchestrator.cs</code></td>
<td>~363</td>
<td>split — 4 entry points → 2</td>
</tr>
<tr>
<td><code>YouTubeSyncProcessor.cs</code></td>
<td>~332</td>
<td>split — completes layer god with orchestrator</td>
</tr>
<tr>
<td><code>SaraconService.cs</code></td>
<td>~329</td>
<td>shrink — drop retry branches</td>
</tr>
<tr>
<td><code>ProcessRunner.cs</code></td>
<td>~336</td>
<td>shrink — drop grace-kill branches → ~240</td>
</tr>
<tr>
<td><code>DashboardHtmlGenerator.cs</code></td>
<td>~364</td>
<td>move — keep hand-roll, relocate to Services</td>
</tr>
<tr>
<td><code>TextAnalyticsService.cs</code></td>
<td>~255</td>
<td>shrink — 5× cloned guard/catch → central runner → ~160</td>
</tr>
</tbody>
</table>
<p>God modules: <code>Services/Audio</code> file-count god (18 files); <code>Services/Google</code> layer god (dedup to 1 layer); <code>CLI</code> layer violation — move 508 LOC to Services; <code>Core</code> <code>Telemetry</code> god (10 sinks, shrink).</p>
<h2>Ceilings</h2>
<p>Mark deliberate ceiling shortcuts with <code>ponytail:</code> comment (<code>global lock, per-account locks if throughput matters</code>).</p>
