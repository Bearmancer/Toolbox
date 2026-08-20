<h1>Azure Services</h1>
<p>Thin SDK wrappers — one client + service per Azure AI capability. Cross-service consumer: <code>Services.Google</code> → <code>TranslateService</code>.</p>
<h2>STRUCTURE</h2>
<pre class="syntax-highlighting"><code><span class="text plain">Azure/
├── AzureSetup.cs               # extension AddAzureServices() — 4 SDK clients + SpeechService
├── AzureCredentials.cs         # 15 env vars: Read() + Env() — endpoints/keys/region/deployment
├── VisionService.cs            # ImageAnalysisClient → AnalyzeAsync()
├── TranslateService.cs         # TextTranslationClient → TranslateBatchAsync() / TransliterateBatchAsync()
├── SpeechService.cs            # SpeechConfig+ffmpeg → TranscribeAsync() / SynthesizeAsync() (chunked)
├── DocIntelService.cs          # DocumentIntelligenceClient → AnalyzeAsync()
├── TextAnalyticsService.cs     # TextAnalyticsClient → Entities/KeyPhrases
├── AzureSdkEventListener.cs    # AzureEventSourceListener → Serilog (Azure-Core/Identity)
├── ClientModelEventListener.cs # ClientModel EventSource → Serilog
├── SpeechSdkEventListener.cs   # Speech SDK EventSource → Serilog
└── EventLevelMapper.cs         # EventLevel → LogEventLevel
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
<td>Add Azure service</td>
<td><code>XxxService.cs</code> + <code>AzureCredentials.cs</code> + <code>AzureSetup.cs</code></td>
<td>Add props + <code>Env()</code>, register <code>new XxxClient()</code> + <code>AddSingleton&lt;XxxService&gt;()</code></td>
</tr>
<tr>
<td>Add env var</td>
<td><code>AzureCredentials.cs</code></td>
<td>Add <code>required string</code> prop + <code>Env(&quot;KEY_NAME&quot;)</code> in <code>Read()</code></td>
</tr>
<tr>
<td>Change DI</td>
<td><code>AzureSetup.cs</code></td>
<td><code>extension(IServiceCollection services)</code> block</td>
</tr>
</tbody>
</table>
<h2>CONVENTIONS</h2>
<ul>
<li><strong>One SDK client per service</strong> (except <code>SpeechService</code> — builds <code>SpeechConfig</code> from <code>AzureCredentials</code>).</li>
<li><strong>Credentials:</strong> <code>.env</code> only via <code>AzureCredentials.Read()</code> — 15 vars, <code>Env()</code> throws on missing.</li>
<li><strong>DI:</strong> <code>extension(IServiceCollection)</code> → <code>AddSingleton(new XxxClient(...))</code> + <code>AddSingleton&lt;XxxService&gt;()</code>.</li>
<li><strong>Errors:</strong> <code>ErrorOr&lt;T&gt;</code> for fallible ops, <code>Errors.*.ApiError</code> on catch, throw only for missing config.</li>
</ul>
<h2>ANTI-PATTERNS</h2>
<ul>
<li><strong>NEVER</strong> hardcode keys/endpoints — always <code>AzureCredentials.Read()</code>.</li>
<li><strong>NEVER</strong> add CLI logic here — thin service layer, CLI lives in <code>src/CLI/Azure/</code>.</li>
</ul>
