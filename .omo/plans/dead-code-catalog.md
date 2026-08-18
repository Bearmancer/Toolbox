<h1>Dead Code Catalog — Unconsumed vs Unused vs Logical-Error</h1>
<p><strong>Source:</strong> <code>toolbox-consolidated-spec.md</code> §8 (18+5+5 symbols)
<strong>Verified:</strong> 2026-08-18 | Live: <code>src/Core/Errors.cs</code>, <code>src/Core/Text.cs</code>, <code>src/Services/Google/YouTube/*</code>, <code>src/Services/Audio/*</code>, <code>src/CLI/**</code>, <code>Directory.Packages.props</code></p>
<h2>Unconsumed (no code path reads it)</h2>
<table>
<thead>
<tr>
<th>#</th>
<th>Symbol</th>
<th>Location</th>
<th>Evidence</th>
</tr>
</thead>
<tbody>
<tr>
<td>1</td>
<td><code>Errors.PlaylistNotFound/VideoNotFound</code></td>
<td><code>Core/Errors.cs</code></td>
<td>0 producers, 0 consumers — see <code>error-taxonomy.md</code></td>
</tr>
<tr>
<td>2</td>
<td><code>Errors.General.Unexpected/Internal</code></td>
<td><code>Core/Errors.cs:9</code></td>
<td>0 callers</td>
</tr>
<tr>
<td>3</td>
<td><code>Errors.Validation.RequiredField</code></td>
<td><code>Core/Errors.cs:21</code></td>
<td>0 callers</td>
</tr>
<tr>
<td>4</td>
<td><code>Errors.Azure.ServiceUnavailable</code></td>
<td><code>Core/Errors.cs:50</code></td>
<td>0+0</td>
</tr>
<tr>
<td>5</td>
<td><code>Text.Has/StartsWith</code></td>
<td><code>Core/Text.cs</code></td>
<td>0 callers</td>
</tr>
<tr>
<td>6</td>
<td><code>SyncResult.UpdatedSnapshots</code></td>
<td><code>YouTubeSyncProcessor.cs:326</code></td>
<td>populated → never read</td>
</tr>
<tr>
<td>7</td>
<td><code>SyncOutcome.IdsWithNewVideos</code></td>
<td><code>YouTubePlaylistOrchestrator.cs:396</code></td>
<td>computed → discarded</td>
</tr>
<tr>
<td>8</td>
<td><code>DuplicateMergeOutcome.GroupsProcessed/Deferred</code></td>
<td><code>YouTubeDuplicateMerger.cs:14</code></td>
<td>logged → discarded</td>
</tr>
<tr>
<td>9</td>
<td><code>PathValidator.ValidateOutputDirectory</code></td>
<td><code>Services/Audio/PathValidator.cs:18</code></td>
<td>0 callers</td>
</tr>
<tr>
<td>10</td>
<td><code>SacdProbeService</code></td>
<td><code>Services/Audio/SacdProbeService.cs:3</code></td>
<td>pure delegation, 0 pipeline callers</td>
</tr>
<tr>
<td>11</td>
<td><code>DashboardService DI singleton</code></td>
<td><code>GoogleSetup.cs:69</code></td>
<td>all methods static — registration dead</td>
</tr>
<tr>
<td>12</td>
<td><code>TranslateCommand --from</code></td>
<td><code>CLI/Azure/TranslateCommand.cs:59</code></td>
<td>registered → ignored</td>
</tr>
<tr>
<td>13</td>
<td><code>SyncResult.SkippedVideos</code></td>
<td><code>SyncProcessor→Orchestrator</code></td>
<td>logged → never read</td>
</tr>
<tr>
<td>14</td>
<td><code>ChangeDetectionResult.UnchangedPlaylists</code></td>
<td><code>ChangeDetector→Orchestrator</code></td>
<td>never iterated</td>
</tr>
<tr>
<td>15</td>
<td><code>PlaylistSnapshot.LastChecked</code></td>
<td><code>PlaylistService:203</code></td>
<td>written → never read</td>
</tr>
<tr>
<td>16</td>
<td><code>YouTubeFetchState.LastChecked/LastUpdated</code> top-level</td>
<td><code>YouTubeFetchState:13</code></td>
<td>written 5 places → never read</td>
</tr>
<tr>
<td>17</td>
<td><code>DashboardData PlaylistCount/VideoCount</code></td>
<td><code>DashboardDataBuilder:20</code></td>
<td>scaffolding dead</td>
</tr>
<tr>
<td>18</td>
<td><code>ArchiveDeleted duplicate</code></td>
<td><code>YouTubeFetchState</code> vs <code>YouTubeSyncProcessor</code></td>
<td>duplicate path</td>
</tr>
</tbody>
</table>
<h2>Unused (code path never triggers)</h2>
<table>
<thead>
<tr>
<th>#</th>
<th>Symbol</th>
<th>Location</th>
<th>Evidence</th>
</tr>
</thead>
<tbody>
<tr>
<td>1</td>
<td><code>SSH.NET</code> in 5 non-CLI projects</td>
<td><code>Core/Azure/Audio/Google/LastFm .csproj</code></td>
<td>0 <code>Renci</code> usage outside CLI — leave if OCI <code>OciDashboardDeployer</code> needs it; verify first</td>
</tr>
<tr>
<td>2</td>
<td><code>SacdConvertCommand 24/both format</code></td>
<td><code>CLI/Audio/SacdConvertCommand.cs:18</code></td>
<td>advertised → validation rejects</td>
</tr>
<tr>
<td>3</td>
<td><code>ProcessResult.ShouldBreak=false</code></td>
<td><code>YouTubeSyncProcessor.cs:335</code></td>
<td>always true on error</td>
</tr>
<tr>
<td>4</td>
<td><code>HmsTimeSpanConverter</code> in Merger isolated <code>JsonOptions</code></td>
<td><code>YouTubeDuplicateMerger.cs</code></td>
<td>0 <code>TimeSpan</code> fields in manifest — reuse <code>YouTubeFetchState.JsonOptions</code></td>
</tr>
</tbody>
</table>
<h2>Consumed but Logical-Error (fix, don't delete)</h2>
<table>
<thead>
<tr>
<th>#</th>
<th>Symbol</th>
<th>Location</th>
<th>Bug</th>
</tr>
</thead>
<tbody>
<tr>
<td>1</td>
<td><code>YT.RateLimit</code></td>
<td><code>Errors.cs:27→SyncProcessor:79</code></td>
<td>consumed, never produced — producers emit <code>ApiError</code></td>
</tr>
<tr>
<td>2</td>
<td><code>YT.QuotaExceeded</code></td>
<td><code>SortService:312</code></td>
<td>produced, consumer never checks</td>
</tr>
<tr>
<td>3</td>
<td><code>Azure.AuthFailed/RateLimit</code></td>
<td><code>SyncProcessor:88</code></td>
<td>consumed, <code>TranslateService</code> emits generic</td>
</tr>
<tr>
<td>4</td>
<td><code>S-13 reverseLookup</code></td>
<td><code>DashboardService:74</code></td>
<td>sanitized title collision drops data</td>
</tr>
<tr>
<td>5</td>
<td><code>N-05 transliterate</code></td>
<td><code>TranslationService:213</code></td>
<td><code>hi</code> transliterated counts as translated <code>en</code></td>
</tr>
</tbody>
</table>
<h2>Notes</h2>
<ul>
<li><code>Serilog.Sinks.Console</code> <code>PackageVersion</code> entry in <code>Directory.Packages.props:19</code> — 0 <code>PackageReference</code> consumers. Safe to drop.</li>
<li><code>state/logs/*.jsonl</code> — 8 of 10 currently 0 B in dev; <code>audio.jsonl</code> + <code>youtube.jsonl</code> active. Empty ≠ dead architecture, but confirms missing <code>ForService</code> callers or level drop.</li>
</ul>
