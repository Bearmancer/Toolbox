<h1>Audio — SACD ISO → DSD → FLAC</h1>
<p>External-bin pipeline: sacd_extract → DFF → saracon d2p → sox split → ATL.NET tag.</p>
<h2>STRUCTURE — 18 files</h2>
<pre class="syntax-highlighting"><code><span class="text plain">Audio/
├── AudioSetup.cs              # DI extension AddAudioServices(), eager PATH check saracon/sox/sacd_extract
├── PipelineOrchestrator.cs    # Pure orchestration: enumerate ISOs (natural sort), probe, route, cleanup. 6 deps
├── DsdConvertService.cs       # Facade: ProbeDsdAsync, PrepareDffAsync, CalculateGainAsync, ConvertAndSplitAsync, Derive. Owns Saracon/Sox/Metadata
├── SaraconService.cs          # Internal of DsdConvertService. saracon -c d2p wrapper. 1h timeout, 100% marker, Validates WAV/FLAC output
├── SoxService.cs              # Internal of DsdConvertService. trim split, stats (Pk lev dB), duration --i -D, derive rate -v
├── SacdExtractService.cs      # sacd_extract: -P probe, -2/-m -e -c -C extract (Edit Master + CUE)
├── ProcessRunner.cs           # Shared: ArgumentList, concurrent stdout/stderr drain, CancellationToken, timeout/inactivity/completionPattern
├── LogPaths.cs                # Path redaction: Setup/Reset IsoRoot+OutputRoot, Format → «ISO»/«OUT»/«TMP»
├── PathValidator.cs           # Traversal/containment validation
├── DiskSpaceChecker.cs        # Pre-flight: 4x extraction, 8x conversion + 500MB margin
├── DiscOutputInspector.cs     # Disc assessment: CUE/FLAC/DFF probe → DiscState
├── FlacCompletenessChecker.cs # Duration checks, FLAC-by-track map, DFF dir resolution
├── DiscState.cs               # Complete | NeedsPrimaryConversion | NeedsExtraction | InvalidArtifacts | Failed
├── ReprocessGuard.cs          # state/audio/sacd-guard.json — 3 consecutive non-Complete → Failed
├── DffMetadataStripper.cs     # ID3 chunk strip → _clean.dff, FRM8 size rewrite, odd-pad handling
├── AudioMetadataService.cs    # ATL.NET: new Track(path), set props, Save()
├── CueParser.cs               # Custom CUE: BOM + UTF-8 heuristic + Windows-1252 fallback, no external dep
└── AudioModels.cs             # SacdDisc/Track, DsdProbeResult, CueSheet/Track, DsdConversionSettings.ForDsdRate, ConversionResult, PipelineResult
</span></code></pre>
<p>Facade: <code>PipelineOrchestrator</code> → <code>DsdConvertService</code> only. Never <code>SaraconService</code>/<code>SoxService</code> directly.</p>
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
<td>Add conversion step</td>
<td><code>DsdConvertService.cs</code></td>
<td>Add method to facade, call from PipelineOrchestrator</td>
</tr>
<tr>
<td>Change DSD→PCM</td>
<td><code>SaraconService.cs</code></td>
<td>Internal dep. d2p: gain/sample-rate/bit-depth/tpdf</td>
</tr>
<tr>
<td>Change sox op</td>
<td><code>SoxService.cs</code></td>
<td>Internal dep. Split/stats/duration/derive</td>
</tr>
<tr>
<td>Change gain</td>
<td><code>DsdConvertService.cs</code></td>
<td>DFF header + saracon 0dB→sox stats → gain = -0.5 - peak, cap 6.0</td>
</tr>
<tr>
<td>Add CUE field</td>
<td><code>CueParser.cs</code></td>
<td>Parse() method</td>
</tr>
<tr>
<td>Add metadata</td>
<td><code>DsdConvertService.cs</code></td>
<td>ATL tag inside ConvertAndSplitAsync</td>
</tr>
<tr>
<td>Change binary path</td>
<td><code>AudioSetup.cs</code></td>
<td>PATH only, no env vars</td>
</tr>
<tr>
<td>Pipeline logic</td>
<td><code>PipelineOrchestrator.cs</code></td>
<td>Enumeration, routing, cleanup</td>
</tr>
<tr>
<td>Resume/assessment</td>
<td><code>DiscOutputInspector.cs</code></td>
<td>CUE/FLAC/DFF probe, resume state</td>
</tr>
<tr>
<td>Pre-flight check</td>
<td><code>PathValidator.cs</code> / <code>DiskSpaceChecker.cs</code></td>
<td>Before pipeline start</td>
</tr>
</tbody>
</table>
<h2>CONVENTIONS</h2>
<ul>
<li>ProcessRunner: ArgumentList only, concurrent stdout/stderr collectors, CancellationToken always, TerminationReason (Exited/Timeout/Inactivity/KilledAfterCompletionMarker/Canceled/StartFailed), completionPattern &quot;100%&quot; + 10s grace.</li>
<li>PipelineOrchestrator pure orchestration: natural-sort ISO enumeration, sacd_extract probe, DiscOutputInspector routing, delegates ONLY to DsdConvertService.</li>
<li>CUE: custom parser, no lib. BOM + UTF-8 heuristic + Windows-1252 fallback.</li>
<li>DsdConversionSettings.ForDsdRate(): single sample-rate mapping. DSD64→44100/16,88200/24; DSD128→88200/16,176400/24. No inline switches.</li>
<li>ATL.NET metadata: new Track(path), set props, Save().</li>
<li>ErrorOr<!-- raw HTML omitted --> on all fallible ops. Telemetry.ForService(ServiceName.Audio) scope.</li>
<li>Output dirs: sibling <code>../Name (Stereo)/</code> not <code>Name/[Stereo]/</code>. Single disc per subdir assessment.</li>
<li>DiskSpace: 4x ISO extraction, 8x conversion, +500MB margin via DriveInfo.AvailableFreeSpace.</li>
</ul>
<h2>ENVIRONMENT</h2>
<p>Binaries <code>saracon</code>, <code>sox</code>, <code>sacd_extract</code> from PATH only. Validated eagerly in AudioSetup.AddAudioServices() via ProcessRunner.IsOnPath() — throws InvalidOperationException if missing. No env vars.</p>
<h2>PIPELINE — 9 steps</h2>
<ol>
<li>sacd_extract -P -i <!-- raw HTML omitted --> → stereo/mch probe</li>
<li>sacd_extract -2/-m -e -c -C -i <!-- raw HTML omitted --> → DSDIFF Edit Master DFF + CUE (in channelDir sibling)</li>
<li>DFF FRM8/DSD header parse (PROP/SND/FS/CHNL) → sample rate + channels</li>
<li>Prepare: DffMetadataStripper ID3 check → _clean.dff if needed</li>
<li>Gain: saracon d2p 0dB → temp WAV → sox stats → gain = -0.5 - Pk lev dB, cap 6.0</li>
<li>saracon -c d2p -r <!-- raw HTML omitted --> -f wav -n <!-- raw HTML omitted -->bit -d tpdf -g <!-- raw HTML omitted --> -T -V all -t <!-- raw HTML omitted --> <!-- raw HTML omitted --> → master WAV</li>
<li>sox trim per CueTrack → FLACs (inside ConvertAndSplitAsync), ATL.NET tag per track</li>
<li>Delete master WAV in finally; best-effort (never masks primary error)</li>
<li>Optional: DeriveDirectoryAsync → 16-bit FLAC via sox rate -v</li>
</ol>
<h2>SARACON</h2>
<p>Headless only, never GUI. SaraconService builds: <code>saracon -c d2p -r &lt;rate&gt; -f wav -n &lt;bit&gt;bit -d tpdf -g &lt;gain&gt; -T -V all -t &quot;&lt;outDir&gt;&quot; &quot;&lt;input.dff&gt;&quot;</code>. -c d2p required, final arg = input DFF, -t = output dir. Default Bit16 at app layer (omit --format, parser rejects --format 16). Validates RIFF/WAVE/fmt/data chunks, checks -d2p variant filename, warns if output &lt;50% expected PCM bytes. 1h timeout.</p>
<h2>STATE / RECOVERY</h2>
<p>DiscOutputInspector → Complete / NeedsPrimaryConversion / NeedsExtraction / InvalidArtifacts. ReprocessGuard in state/audio/sacd-guard.json, 3 consecutive non-Complete → Failed, Complete clears entry, Warn log on transition. Reset: <code>dotnet run --project src\App -- audio sacd-convert --reset-guard</code>. Don't edit JSON manually.</p>
<h2>ARTIFACT OWNERSHIP</h2>
<table>
<thead>
<tr>
<th>Artifact</th>
<th>Success</th>
<th>Failure</th>
</tr>
</thead>
<tbody>
<tr>
<td>ISO</td>
<td>delete iff --keep-iso absent AND outputs validate (FLAC count==CUE tracks, non-zero)</td>
<td>retain</td>
</tr>
<tr>
<td>CUE</td>
<td>retain — never deleted</td>
<td>retain</td>
</tr>
<tr>
<td>DFF/_clean.dff</td>
<td>delete after full validation (even with --keep-iso)</td>
<td>retain/quarantine</td>
</tr>
<tr>
<td>FLAC</td>
<td>retain</td>
<td>delete only deliberate re-split, logged</td>
</tr>
<tr>
<td>Master WAV</td>
<td>finally best-effort delete</td>
<td>never masks error</td>
</tr>
</tbody>
</table>
