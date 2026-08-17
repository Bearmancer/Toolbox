More YT seams hunt — max30 full graph pull across every YT type.
⚙codegraph_codegraph_explore [query=YouTubeSortService SortResult SortPlan SortPassResult PlaylistUpdate LongestIncreasingSubsequence ComputeSortPlan ExecuteSortPlanAsync, maxFiles=30]
YouTube — more not-produced / not-consumed
Max30 sweep pulled YouTubeSortService 421 + SortResult/SortPlan/SortPassResult/PlaylistUpdate/LIS/ComputeSortPlan/ExecuteSortPlanAsync verbatim. Prior gaps YT.RateLimit/UpdatedSnapshots/Unchanged/LastChecked already bucketed — 5 new below.
New seams
#	Data	Producer	Consumer	Logged?	p→c	Verdict
N-01	SortResult.LastSortCompleted SortService:405 bool + LastSortAttempted DateTimeOffset?	SortService:139-145 new SortResult(repositioned,etag,writes,distinct,UtcNow, repositioned==0) produced per SortPlaylistAsync	SyncProcessor.SortSinglePlaylistAsync:267-271 stored[playlistId]=snapshot with {LastSortMoves=DistinctItemsMoved, LastSortAttempted=…, LastSortCompleted=…} → Orchestrator.ExecuteWithSortAsync:201 OrderBy(Contains).ThenBy(LastSortMoves).ThenBy(LastSortAttempted).Where(!LastSortCompleted) Where(!completed) resumes	snapshot resume consumed Orchestrator:212	produced↔consumed via SaveAsync:206 snapshot — but top-level YouTubeFetchState.LastChecked/Updated still unconsumed (prior S-15). Gap is Repositioned vs DistinctItemsMoved — both produced SortService:140-141 distinctMoved vs repositioned — SortProcessor:279 result.DistinctItemsMoved uses distinct, result.Repositioned used for RefreshLocalStateAsync:263 if Repositioned>0 — two counts for same event, both consumed but diverge if duplicate moves — dupe metric.	 
N-02	SortPlan TotalItems / LisSize SortPlan:409	ComputeSortPlan:231 new SortPlan(items.Count, keptIds.Count, updates) produced	ExecuteSortPlanAsync:248 plan.Updates.Count only — TotalItems and LisSize never read	Debug ComputeSortPlan: {Total} items, LIS={LisSize}, {Delta} need repositioning:224 logged inside producer	produced-logged-never-consumed outside	dead fields outside — keep log, drop from SortPlan or keep for dashboard; TotalItems = items.Count already known to caller SortService:149 fetch deduped count.
N-03	SortPassResult Failures SortPassResult:415 Failures	ExecuteSortPlan:338 return Failures>0 ? ApiError : new(Successes,Failures,Writes,Distinct) produced, 316 Failures++ on catch(Exception)	SortService:84 if result.Failures>0 break consumed inside same method 30 lines away, SortProcessor:251 if sortResult.IsError return(0,0,0) — Failures collapsed to ApiError YT.ApiError not Failures count	logged Error {Failures} updates failed:87	produced-consumed-inside then erased — SortPassResult.Failures survives only one hop then ApiError string — caller SyncProcessor loses count Failures → SortStatistics never aggregates failures per-playlist. Logical missing aggregate	 
N-04	PlaylistUpdate Item + NewPosition PlaylistUpdate:398	ComputeSortPlan:222 updates.Add(new(sorted[i], i)) produced	consumed ExecuteSortPlan:268 PlaylistUpdate update=plan.Updates[i] → item.Snippet.Position=newPosition → yt.PlaylistItems.Update(item):298	Verbose Updating ItemId={ItemId} NewPos={NewPos}:275	consumed — no gap	 
N-05	YouTubeTranslationService MaxTextsPerCall 70 / MaxCharsPerCall 30000	TranslationService:9 constants → BuildTranslationBatches:98-105 batching produced	consumed entirely by ExecuteTranslationBatchesAsync:120 batch split. But caller Processor checkpoint checkpointAsync saves processed/*.json per batch inside translation — if translate fails transliterateResult.IsError:155 → return FirstError → Processor:44 TranslateVideosAsync error → SyncProcessor:99 ProcessorResult.Value unbalanced? actually ProcessSingle returns ErrorOrwithYT.ApiError`**	Debug Translate [{Batch}/{Total}] -> Azure Transliterate/Translate:139,172 per batch	consumed, but fallback TranslateService.MaxChars 50000 vs YouTube 30000 drift flagged prior — not dead, stdlib ladder drift. Transliterate hi Deva→Latn hardcoded :149  hi/Deva/Latn produced never varied — if only hi videos need it, DetectedLanguage hi still produced later Processor never uses transliterated en? Actually ApplyTranslationResults:213 translated = detectedLang not en/unknown — hi→Latn DetectedLanguage hi stays "hi" not "unknown" so translatedCount++ — produced but hi transliterated still counted translated != en — logic seam: transliterate success should not count as translated if target is en.	 
Why not just TranslationService dupe — detail
- TranslateService:?? TranslateBatchAsync(texts,"en") + TransliterateBatchAsync(texts,"hi","Deva","Latn") — YouTube decides which target gets which call based on ContainsDevanagari:84. That decision shouldTransliterate produced CollectTranslationTargets:64 never consumed outside ExecuteTranslationBatches:134 Where Transliterate — internal fan-out consumed inside same file no seam, but DetectedLanguage "hi" for transliterated vs translated path breaks ApplyTranslationResults:213 translated = not en/unknown — hi transliterated passes not en so counted — logical over-count.
Cumulative YouTube produced↔consumed map after max30 sweep
Produced never consumed (dead): YT.PlaylistNotFound/VideoNotFound factories,
  SyncResult.UpdatedSnapshots rebuilt-dict, SyncOutcome.IdsWithNewVideos,
  DuplicateMerge GroupsProcessed/Deferred, SortPlan TotalItems/LisSize,
  UnchangedPlaylists 4th tuple, PlaylistSnapshot.LastChecked, Top-level LastChecked/Updated,
  ProcessResult.ShouldBreak bool
Produced logged never surfaced: TotalVideos/SkippedVideos final aggregate,
  SortStatistics Attempted/Modified/AlreadySorted/TotalWrites surfaced only as Telemetry.Info not CLI exit,
  SortPassResult Failures collapsed to ApiError string
Consumed never produced (missing mapper): YT.RateLimit (429), Azure.AuthFailed/RateLimit (Translate 429/401),
  YT.QuotaExceeded consumed at SyncProcessor but SortService is sole producer — consumer gap at batch path
Produced double-consumed dupe: Repositioned vs DistinctItemsMoved both for same write batch,
  ChannelName/Id double-serialized Hms vs builder hh:mm:ss, StateRoot constants ×5
YouTube incremental net this sweep: -4 fields (SortPlan TotalItems/LisSize, Unchanged, Policy dupe metric)
                                     + 1 logical fix Transliterator counted-as-translated over-count
                                     0 feature loss — LIS, merge, translate, dashboard all preserved

More seams — new codegraph sweep Dashboard/DashboardService/Sort/Video/Merge
Last sweep proved ChannelName/Id consumed BuildVideoData:49-50 → channelFmt — corrected. New sweep DashboardHtmlGenerator+SyncProcessor+Sort+Video pulls 7 more produced⇄consumed gaps beyond YT.RateLimit/UpdatedSnapshots already bucketed.
New produced→consumed gaps
#	Data	Producer	Consumer	Logged?	p→c	Verdict
S-09	SyncResult.SkippedVideos aggregated SyncCounters:347 skipped+=result.Skipped → ToResult:378 SkippedVideos	Processor.BuildVideoList:175 TryGetValue duration fail → skipped++ → MergeResult Skipped → ProcessResult Skipped	Orchestrator.Finalize:172 Sync done {TotalVideos} videos reads TotalVideos only — SkippedVideos never reads, CLI ExecuteAsync:25 ids only	per-video Debug Skipping video {VideoId} yes, final SkippedVideos no	produced-never-consmued outside	dead aggregate delete SkippedVideos or log Finalize {Skipped} skipped
S-10	ChangeDetectionResult.UnchangedPlaylists 4th tuple	ChangeDetector:45 unchangedPlaylists.Add(snapshot)	Orchestrator.FetchSummaries:53 DetectChanges only reads New/Changed/Deleted — Unchanged never iterated	only inside Detector:52 Info {Unchanged} unchanged	produced-never-consumed	delete 4th list unless dashboard overviewFilter wants count
S-11	SortStatistics Attempted/Modified/AlreadySorted/TotalWrites	SyncProcessor.SortPlaylists:208 stats new(Attempted,Modified,AlreadySorted,TotalWrites)	Orchestrator.ExecuteWithSort:226 await SortPlaylistsAsync discards return return Ids	Info Sort complete {Attempted}…{TotalWrites} writes:215 yes but CLI SyncYoutubeCommand never prints — verbose off invisible	produced-logged-not-surfaced	logical missing output return stats to CLI ToolResult
S-12	SyncResult.TotalVideos + TotalVideos vs ProcessedIds.Count	SyncCounters:359 TotalVideos+=Videos → ToResult	Finalize:178 result.TotalVideos logged, ProcessedIds.Count also equals — duplicate metric	logged	dupe	keep one TotalVideos == sum Videos already — drop aggregate or compute from Ids.Count
S-13	DashboardService.reverseLookup TryAdd(SanitizeFileName(Title), Title) DashboardService:74	produced processed/*.json file Günter Wand.json sanitized Gunter_Wand.json vs Gunter Wand.json collision	TryAdd keeps first, second duplicate title silently drops result[title]=videos never loads 2nd — LoadVideosByPlaylist:88 TryGetValue misses	no — silently dropped	produced-never-consumed (collision)	logical error key by PlaylistId not sanitized title: result[PlaylistId]=videos + videosByPlaylist Title dict bug
S-14	YouTubeVideo.Description / TranslatedDescription payload 10KB/video×150	Processor:184 Description=Snippet.Description ?? "" → Translation:72 Description → stored processed/*.json → BuildVideoData:47 description = TranslatedDescription ?? Description CompactJson 8.4MB dashboard-data.js	generator Tabulator columns Title/Channel/Duration/Playlist only — description column hidden by default toggleCol(...'description',false):75 + VIDEO_FIELDS search includes description:143 so consumed for search but not visible	search yes, display off	produced-transported-not-rendered	keep for search, drop from BuildVideoData if search excludes description → -1.5MB
S-15	YouTubeFetchState top-level LastChecked/LastUpdated FetchState:30	SyncProcessor.SaveIncrementalState:123 LastChecked=UtcNow LastUpdated=UtcNow ×2 callers	LoadAsync never compares, FetchSummaries:50 fetches unconditionally	Info YouTube sync starting no timestamp	produced-never-consumed	dead required make optional or add throttle if UtcNow-LastChecked<5m skip fetch else delete
S-16	PlaylistSnapshot.LastChecked FetchState:13	PlaylistService:203 LastChecked=UtcNow per GetPlaylistSummaries/Summary	Orchestrator+Detector never read — etagChanged/countChanged only	no	produced-never-consumed	delete field keep LastUpdated publishedAt
S-17	HmsTimeSpanConverter FetchState:113 Read TimeSpan.Parse / Write hh:mm:ss + Merger private JsonOptions WriteIndented only	FetchState.JsonOptions includes Hms → used for SaveAsync/LoadAsync manifests + Processor.WriteJsonAsync/LoadExistingVideosAsync videos	Merger.ArchiveMergeManifest:395 Serialize(manifest, Merger.JsonOptions) manifest MergeManifestRecord has 0 TimeSpan — Hms never hit	no	unnecessary converter scope	reuse FetchState.JsonOptions or keep Merger option but remove converter
Why S-13 not dead but logical
S-13 looks like produced→dropped not dead. DashboardDataBuilder.BuildVideoData:38 TryGetValue(p.Title) keys by original Title; DashboardService:74 reverseLookup keys by SanitizeFileName(Title). Live FS shows Günter Wand.json + Gunter Wand.json + Gunter Wand-raw 3 files — 2 distinct playlists whose sanitized names collide Gunter_Wand. Second TryAdd fails, second file never result[title]=videos — data loss. Consumer exists but filtered by collision — logical not dead.
Fix S-13: Dictionary<string, IReadOnlyList<YouTubeVideo>> result key PlaylistId not Title — result[PlaylistSnapshot.PlaylistId] + BuildVideoData looks up by Id. 1-line.
Re-bucketed after new sweep
A dead:  S-09 SkippedVideos, S-10 Unchanged, S-16 Snapshot.LastChecked, S-15 top-level LastChecked/Updated required
B dupe:  S-12 TotalVideos vs Count, S-17 Hms scope
C logical (fix not delete): S-13 sanitized collision → Id key, S-11 SortStatistics → CLI, S-14 description search vs display keep/search choice
YouTube incremental net: -4 fields/lists + 1 collision fix + 1 log add
Total repo net still -820→-960 + S-13 correctness fix (not counted in -lines)

YouTube — still more unconsumed seams
Last sweep claimed ChannelName/Id unconsumed — wrong. DashboardDataBuilder:49-50 channelId/channelName proves consumed. Revised inventory below: only seams where produced count ≠ consumed count or 0 consumed.
New seams found
#	Field / data	Produced	Consumed	Gap
S-01	YouTubeVideo.TranslatedTitle / TranslatedDescription	TranslationService:218 ApplyTranslationResults per VideoIndex → Processor.MergeCacheAsync:267 preserves existing translated if Title/Description IsEqualTo	Consumed DashboardDataBuilder:47 BuildVideoData title = TranslatedTitle ?? Title, 48 description = TranslatedDescription ?? Description — fallback means translated sometimes null but counted as complete	partially unconsumed: DetectedLanguage produced ApplyTranslationResults:212 languages[detectedLang]++, stored Processor:271 DetectedLanguage = existing ?? …, but DashboardDataBuilder:56 never includes detectedLanguage in videos json — loaded processed/*.json has it, window.allVideos strips it. If dashboard never shows language, produced-not-surfaced.
S-02	YouTubeVideo.Description + TranslatedDescription	Processor:184 Description = item.Snippet.Description ?? "" → translate → stored processed/*.json → DashboardService:93 result[title]=videos loads	BuildVideoData:47 description = TranslatedDescription ?? Description serialized to window.allVideos but DashboardHtmlGenerator? check — grep description in DashboardHtmlGenerator — need but not pulled; if generator never renders description column, produced-transported-not-rendered	audit generator: if Tabulator columns [title, duration, channel] only, description is produced→json→js variable never DOM — bloat BuildVideoData:42-53 payload + description per video × avg 150 videos ×10KB description = 1.5MB dashboard-data.js waste.
S-03	YouTubeVideo.Duration hh:mm:ss vs HmsTimeSpanConverter	VideoService:62 ParseIso8601Duration XmlConvert PT duration → TimeSpan → Processor stores Duration via FetchState.JsonOptions Hms hh:mm:ss → BuildVideoData:48 duration = Duration.ToString(@"hh\:mm\:ss") second formatting	double-format: stored hh:mm:ss string, then reformatted same hh:mm:ss in builder — duplicate serialization	shrink: builder should use stored string directly or store Duration already as formatted string — not error, waste. Hms read TimeSpan.Parse must match write hh:mm:ss — consistent today after fix noted prior.
S-04	PlaylistSnapshot.ReportedVideoCount	PlaylistService:205 GetPlaylistSummaries ItemCount produced	consumed ChangeDetector:40 countChanged = ReportedVideoCount != stored.ReportedVideoCount → changedPlaylists → but SyncProcessor.ProcessPlaylistsAsync:58 UpdatedSnapshots[snapshot]=snapshot stores new snapshot only after ProcessSinglePlaylistAsync success — if sync fails, top-level FetchState LastUpdated still updated → count sticks? Actually Orchestrator.FetchSummariesAndDetectAsync:83 stored.Where(!deletedIds).ToDictionary removes deleted before merge, count stays.	produced-consumed-logged: Telemetry.Info("Changed: {Title} ({Delta} videos)") delta computed from count — consumed. Not dead.
S-05	SortStatistics Attempted/Modified/AlreadySorted/TotalWrites	SyncProcessor.SortPlaylistsAsync:208 stats = new(Attempted,Modified,AlreadySorted,TotalWrites) produced	consumed Telemetry.Info("Sort complete: {Attempted} … {TotalWrites} writes"):215 logged, returned Task<SortStatistics>, caller Orchestrator.ExecuteWithSortAsync:226 await SortPlaylistsAsync discards return return outcome.Ids — stats not propagated	produced-logged-never-surfaced — SyncYoutubeCommand could show exit code 0 but CLI never prints sort stats to terminal beyond log. If verbose off, Info not seen. Logical missing output: SortStatistics should be Telemetry.Info already with singular gate → hits youtube.jsonl+Seq, but no CLI summary unless verbose. Not dead, incomplete.
S-06	SyncResult TotalVideos/SkippedVideos	SyncCounters.UpdateFrom:360 TotalVideos+=Videos → ToResult:377 totalVideos	Orchestrator.Finalize:178 result.TotalVideos logged Sync done … {TotalVideos} videos — only via Telemetry.Info, not returned to CLI as exit message beyond ids	same as S-05 — produced-logged-not-surfaced to user unless --debug.
S-07	ChangeDetectionResult UnchangedPlaylists 4th list	ChangeDetector:45 unchangedPlaylists.Add(snapshot) produced	Orchestrator.FetchSummaries:52 ChangeDetectionResult changes = DetectChanges → only New/Changed/Deleted read — Unchanged never iterated, only Telemetry.Info inside detector	produced-never-consumed outside — dead return field but live log inside. Delete 4th tuple unless dashboard wants unchanged count.
S-08	DashboardData.PlaylistCount / VideoCount	DashboardDataBuilder:20 Build → new DashboardData(sorted.Count, videos.Count, ...)	consumed where? DashboardHtmlGenerator? need source — if generator renders PlaylistCount as header, check. If not, produced-never-consumed scaffolding. GenerateDashboardDataAsync returns DashboardResult with playlists+videos, builder counts derived — duplicate of playlists.Count.	audit generator
Prior seams reconfirmed not re-flagged
Kept as not-seam after builder proof	Why
ChannelName/ChannelId	BuildVideoData:49-50 proves consumed → dashboard filter by channel exists. Not dead.
YouTubeFetchState.LastChecked/LastUpdated top-level required	still produced-never-consumed — prior P1 stands.
PlaylistSnapshot.LastChecked	still dead — prior.
Immediate next scans to finish
Scan	Query to codegraph
DashboardHtmlGenerator render seams	DashboardHtmlGenerator file — does it read PlaylistCount/VideoCount/channel/playlists?
YouTubeSortService SortResult NewETag/LastSort* 	produced SortResult 138-145 → SyncProcessor:267 stored[playlistId]=snapshot with {ETag,LastSort*} — is NewETag ever "" fallback consumed?
YouTubeTranslationService transliterate branch	Transliterate Deva→Latn result "hi" hardcoded — is hi→Latn ever consumed as separate code?
HmsTimeSpanConverter scope	FetchState.JsonOptions vs DashboardDataBuilder CompactJson no Hms — builder serializes hh:mm:ss string manually, consistent.
Want me to pull DashboardHtmlGenerator source next? It decides S-02/S-08 fate — description + counts rendered or dead payload.

YouTube — fresh unconsumed / unproduced seams
Codegraph sweep YouTubeFetchState+JsonOptions+HmsConverter+Video+Detect+Merger+Sort+Sync verbatim above. New seams beyond already bucketed YT.RateLimit/QuotaExceeded/SyncResult — 4 new.
1. YouTubeFetchState.LastChecked / LastUpdated top-level
record YouTubeFetchState { required LastChecked, LastUpdated? }
SaveAsync 5 callers: SyncProcessor.SaveIncrementalStateAsync:123 LastChecked=UtcNow, LastUpdated=UtcNow
                     Orchestrator ProcessTitlePipelineAsync:311 same
                     SyncProcessor.SortPlaylistsAsync:206 state saved mutual with sort
LoadAsync: no read — LastChecked/LastUpdated never compared, never guards fetch, never logged.
Direction	Verdict
produced never consumed	written on every save, 0 if (state.LastChecked...) branches. Orchestrator.FetchSummariesAndDetectAsync:50 fetches summaries unconditionally — LastChecked could gate fetch if within throttle window but doesn't.
Lost?	Telemetry.Info("YouTube sync starting") no timestamp — window invisible
Logical vs dead	dead fields today, missing throttle feature — keep only if add if UtcNow - LastChecked < 5min skip fetch. Else delete required prop or make them debug-only ETag path already gates per-playlist via DetectChanges.
Propose	Delete top-level LastChecked/LastUpdated required constraint — per-PlaylistSnapshot.LastChecked/LastUpdated already covers cache. Or keep 1 field LastChecked as throttle gate.
2. PlaylistSnapshot.LastChecked per-playlist
Snapshot { required LastChecked, LastUpdated, LastSort* }
Producer: PlaylistService.GetPlaylistSummariesAsync:203 LastChecked=UtcNow per fetch
          PlaylistService.GetPlaylistSummaryAsync:247 same
Consumer: Orchestrator never reads LastChecked; ChangeDetector etagChanged/countChanged only; Sort resume reads LastSort* but not LastChecked
Direction	produced never consumed
Logged?	no
Logical vs dead	dead — thought-through ETag+count obsoletes it. No consumer filters LastChecked age. If keep, DashboardService 112 could sort ORDER BY LastChecked but not.
Propose	Delete PlaylistSnapshot.LastChecked — LastUpdated (publishedAt) sufficient for display/sort. Save 1 Date field ×145 snapshots ~2KB json + confusion with FetchState.LastChecked. LastChecked=UtcNow on every fetch also overwrites meaningful value — misleading.
3. YouTubeFetchState.JsonOptions.HmsTimeSpanConverter
JsonOptions:22 Converters = { new HmsTimeSpanConverter() } handles YouTubeVideo.Duration hh:mm:ss
Producer: YouTubeFetchState.SaveAsync 5 callers + Processor.WriteJsonAsync → FetchState.JsonOptions
Consumer: DashboardService.LoadVideosByPlaylistAsync:83 Deserialize with same JsonOptions
          Processor.LoadExistingVideosAsync same
Merger: private static JsonOptions = new() { WriteIndented=true } 0 converters
       ArchiveMergeManifestAsync:395 Serialize(manifest, Merger.JsonOptions) — manifests contain Duration? check: MergeManifestRecord { WinnerTitle, LoserRecord Title, SourceVideoIds HashSet<string>, WinnerVideoCount, SourceVideoCount, MergedAt } — 0 TimeSpan fields
Direction
Merger JsonOptions unused converter — Hms never hit, but harmless 1 line
Larger seam: YouTubeVideo.Duration produced VideoService 62 ParseIso8601Duration XmlConvert → Processor 185 ChannelName/ChannelId/Title/Description + Duration → stored processed/*.json → consumed DashboardService 93 result[title]=videos → consumed DashboardDataBuilder? check ChannelName/ChannelId — do dashboards show channel? Grep Dashboard shows no ChannelName hit — TranslatedTitle/Description used, ChannelName/ChannelId stored but not displayed.
→ New seam:
| Field | Produced | Consumed | Output? | Verdict |
|---|---|---|---|
| YouTubeVideo.ChannelName, ChannelId | Processor:187 ChannelName=VideoOwnerChannelTitle ?? ChannelTitle seeded | DashboardService loads videos but DashboardDataBuilder never reads ChannelName/Id — grep 0 | not logged, not dashboard filtered | produced never consumed — logical missing feature or dead |
| Same Description, TranslatedDescription | produced TranslatedTitle shown, Description stored | DashboardService loads but Dashboard HTML shows title+duration? check — if description not rendered | storage bloat | if dashboard never renders description, produced-not-displayed = dead or missing search index |
Propose YouTube audit: if ChannelName/Description not rendered, drop from YouTubeVideo store or add dashboard filter BY Channel.
4. HmsTimeSpanConverter shape mismatch
Hms: Read(TimeSpan.Parse(reader.GetString()!)) Write(hh\:mm\:ss)
YouTube API Duration XmlConvert.ToTimeSpan is ISO8601 PT1H2M3S — not hh:mm:ss
Round-trip: PT3661S → TimeSpan 01:01:01 → hh:mm:ss "01:01:01" → TimeSpan.Parse "01:01:01" = 01:01:01 — works only because TimeSpan.Parse accepts hh:mm:ss. Logically converter should read ISO8601 not hh:mm:ss. Not unconsumed but wrong contract — reading processed/*.json written as hh:mm:ss via FetchState.JsonOptions then DashboardService deserializes with same hh:mm:ss reader → consistent, but external PT... never hits converter.
Not delete — rename to DurationHmsConverter and document.
5. Already bucketed but confirm via new source
- ChangeDetectionResult.UnchangedPlaylists: DetectChanges returns unchanged list 4th tuple — Orchestrator FetchSummaries:52 computes it but **never read** — only New/Changed/Deleted used. → produced never consumed dead 4th list — delete or keep only if dashboard stats unchanged but today only Info {Unchanged} unchanged` log inside detector.
- SyncCounters.TotalVideos/SkippedVideos → SyncResult TotalVideos/SkippedVideos → Orchestrator Finalize:178 result.TotalVideos logged Sync done {TotalVideos} videos consumed only via log, not returned to CLI ExecuteAsync:25 ids only. Produced, logged inside, not surfaced — logical missing telemetry vs dead: keep.
Net new YouTube deletions
Prio	Seam	Action	Loses feature?
P1	PlaylistSnapshot.LastChecked produced PlaylistService:203 never consumed	Delete field required → optional or drop	No — ETag+count gates cache
P1	YouTubeFetchState top-level LastChecked/LastUpdated required	Make optional / delete or add throttle if (UtcNow - LastChecked < 5min) return stored	No today — would add throttle feature if kept
P1	ChangeDetectionResult.UnchangedPlaylists	Delete 4th list if no dashboard consumer	No
P2	YouTubeVideo.ChannelName/ChannelId if dashboard grep 0	Audit dashboard — delete from store or add GROUP BY Channel filter	Missing feature vs dead — choose one
P2	Merger JsonOptions isolated	Reuse FetchState.JsonOptions	No
YouTube incremental net: -3 fields (LastChecked×2 + UnchangedList) + 1 dead allocation
                         0 feature loss — F1 cache keeps ETag+LastUpdated (publishedAt)
                         Logged: add Top-level LastChecked to Info("sync starting last checked {LastChecked}")

                         YouTube — deletion reassessment via codegraph (one-by-one)
Method: codegraph_explore per candidate → blast radius (callers) + source. Every deletion below re-queried. Found: callers N from tool = consumers; producers = catch → Errors.* sites. Flow: producer → ErrorOr<Error> → consumer error.Code branch → log/sink. Prior 46 callers Errors aggregate broken down per factory.
1. Speculative taxonomy — why YT.RateLimit flagged
Errors.cs:27  RateLimitExceeded => Failure("YT.RateLimit", "...Retrying...")
Codegraph: codegraph_explore Errors.YouTube RateLimitExceeded → blast radius Errors 46 callers aggregate, 0 direct producer hits for YT.RateLimit factory (grep YT.RateLimit 0). Consumer:
YouTubeSyncProcessor.cs:79  if (error.Code is "YT.RateLimit" or "Azure.RateLimit") // Warn rate-limit, Break batch
YouTubeSyncProcessor.cs:88  if (error.Code is "Azure.AuthFailed")                  // Error auth, Break
Producers that should emit it:
Producer	codegraph_explore YouTubePlaylistService GetPlaylistSummaries/Paginate/Delete/Insert	Emits	Hits consumer?
PaginateAsync<T>:11 3 callers GetPlaylists/Items/ItemPagesRaw	no try/catch — request.ExecuteAsync exception propagates	GoogleApiException propagates	No — not Error
DeletePlaylistAsync:266 InsertPlaylistItemAsync:292 FetchItemsAsync Processor:138	catch(Exception ex) => ApiError($"{ex.Message}") YouTubeSortService.cs:339 ApiError	YT.ApiError YT.ApiError YT.ApiError	No — 429/403 mapped to ApiError never YT.RateLimit
YouTubeSortService.ExecuteSortPlanAsync:305	catch(GoogleApiException ex) when IsQuotaOrRateLimit(ex) => QuotaExceeded IsQuotaOrRateLimit:343 ex.HttpStatusCode is 403/429 && Message.Contains("quota")	YT.QuotaExceeded only	Consumer never checks QuotaExceeded — inverse gap
TranslateService → YouTubeTranslationService:155,186	if(IsError) return FirstError propagates Translate.ApiError	Translate.ApiError	Azure.RateLimit/AuthFailed never emitted
Result: YT.RateLimit consumed but never produced → unreachable branch. Not dead code — logical error missing mapper. Batch still breaks via Processor:94 Unexpected error → Break with wrong level Error not Warn. Blast radius proves consumer exists, producers collapse 429 to generic.
Fix — typed mapper at every catch(GoogleApiException gex):
429 or 403+Reason=="rateLimitExceeded" → RateLimitExceeded
403+Reason is quotaExceeded/dailyLimitExceeded or Message "quota" → QuotaExceeded(g ex.Message)
404 → PlaylistNotFound(id)
else → ApiError
SyncProcessor:79 add: is "YT.RateLimit" or "YT.QuotaExceeded" or "Azure.RateLimit"
TranslateService: 429 → Azure.RateLimitExceeded, 401/403 → Azure.AuthenticationFailed
After fix branch reachable → Warn rate-limit hits youtube.jsonl+Seq, metrics triage correct.
2. Per-deletion reassessment — Flow: producer → consumer → output
#	Deletion candidate	Codegraph blast radius	Producer → Consumer flow	Output?	consumed-not-produced vs produced-not-consumed	Verdict
1	Errors.YouTube.RateLimitExceeded:27 YT.RateLimit	0 producers emit, 1 consumer SyncProcessor:79	Paginate/Delete/Insert/FetchItems catch→ApiError → SyncProcessor Code=="YT.RateLimit" never hit → falls to Unexpected Error break	Telemetry.Warn("Rate limit...") never hit, Error("Unexpected...") hits instead — wrong log level	consumed-never-produced = logical error not dead	Keep factory, fix producers per §1
2	Errors.YouTube.PlaylistNotFound:31 YT.PlaylistNotFound	codegraph Errors.YouTube PlaylistNotFound → Errors 46 callers but 0 factory callers, 0 error.Code=="YT.PlaylistNotFound" consumers	no producer, no consumer	no log	never-produced never-consumed	True dead — delete until delete-by-id wants 404 vs 401 distinction
3	Errors.YouTube.VideoNotFound:33	same — 0+0	—	—	never/never	True dead — delete
4	Errors.YouTube.QuotaExceeded:38	codegraph QuotaExceeded → 1 producer YouTubeSortService:312 QuotaExceeded($"Quota exhausted after {successes}"), 0 consumers checking YT.QuotaExceeded (SyncProcessor:79 only RateLimit)	SortService→QuotaExceeded → SyncProcessor:79 misses it → 94 Unexpected	SortService logs Error Quota exhausted inside sort, but batch ProcessSinglePlaylistAsync path not checked	produced-never-consumed = logical error mirror	Keep, fix consumer add or "YT.QuotaExceeded" at :79
5	Errors.Azure.AuthenticationFailed + RateLimitExceeded:44 + ServiceUnavailable:50 Azure.*	codegraph Errors.Azure → Errors 46, 0 producers emitting Azure.AuthFailed/RateLimit; consumer SyncProcessor:79,88 checks Azure.RateLimit/AuthFailed	TranslateService catch→Translate.ApiError → SyncProcessor never hit → Unexpected	Telemetry.Error("Azure translation key invalid") never hit	consumed-never-produced = logical error	Keep if Translate maps 429/401, else delete consumer branch and keep generic. Fix at TranslateService boundary. ServiceUnavailable 0+0 → true dead delete
6	Errors.General Unexpected/Internal:9 General.*	0+0 ever	no producer, no consumer YouTube	—	never/never	True dead for YouTube — delete. Not YouTube-specific but not needed until catch→Unexpected pattern appears
7	Errors.Validation RequiredField:21	0+0	—	—	never/never	True dead YouTube — delete
8	YouTubeSyncProcessor.SyncResult.UpdatedSnapshots:326 Dictionary<string,PlaylistSnapshot>	codegraph SyncResult UpdatedSnapshots → 3 producers SyncProcessor:58 UpdatedSnapshots[id]=snapshot + ToResult:370 updated[id]=s , 0 consumers outside SyncProcessor — Orchestrator.Finalize:182 reads only ProcessedIds/PlaylistsWithNewVideos/TotalVideos	ToResult builds new updated from processedSnapshots anyway — counters.UpdatedSnapshots written but rebuilt and discarded	SaveIncrementalStateAsync:108 already persists per-playlist, so dict redundant; not logged	produced-never-consumed = dead field not missing consumption	Delete field SyncResult(ProcessedIds, PlaylistsWithNewVideos, TotalVideos, SkippedVideos)
9	YouTubePlaylistOrchestrator.SyncOutcome.IdsWithNewVideos:396	codegraph SyncOutcome → producers Finalize:183 result?.PlaylistsWithNewVideos ?? [], 0 consumers ExecuteAsync:25 reads only Ids	computed → SyncOutcome → discarded	Finalize:172 Sync done {New}/{Changed}... logs counts not ids, ids never logged	produced-never-consumed = dead field	Delete field
10	YouTubeDuplicateMerger.DuplicateMergeOutcome.GroupsProcessed/Deferred:14 + Survivors	codegraph DuplicateMergeOutcome → producer Merger:324 Survivors/RemovedLosers/Winners/GroupsProcessed/GroupsDeferred, consumers Orchestrator:106 reads only RemovedLosers, WinnersRequiringProcessing	Groups* only inside Merger:314 Info merge complete	logged inside producer, outside discarded	produced-never-consumed outside = dead API, keep log	Delete fields or keep if orchestrator branches on Deferred>0 today just SaveAsync regardless
11	YouTubeSyncProcessor.ProcessResult.ShouldBreak:335 bool	codegraph ProcessResult ShouldBreak → producers ProcessResult.Break at :85,91,99 on every IsError, 1 consumer :45 if ShouldBreak break	always Break on error → immediate break	Warn/Error inside ProcessSinglePlaylistAsync	unnecessary abstraction Break never false on error → return ErrorOr<ProcessResult>	Shrink not delete data flow — error propagation same
12	YouTubePlaylistOrchestrator.CombineNewAndChanged:375 [..New,..Changed]	1 caller FetchSummariesAndDetectAsync:89	inline at 89	—	produced-consumed 1:1	yagni inline — feature kept, indirection removed
13	YouTubeChangeDetector.cs:12 62 DetectChanges(current,stored)	codegraph YouTubeChangeDetector → 1 caller Orchestrator:53	pure ETag+count	not separate log	1 pure func 1 caller	yagni inline unless isolated tests — MergePolicy 65 kept (destructive policy)
14	YouTubeFetchState.ArchiveDeleted:94 vs YouTubeSyncProcessor.ArchiveDeletedPlaylists:128	codegraph YouTubeFetchState DashboardService StateRoot → StateRoot ×5 constants, ArchiveDeleted ×2	2 owners MoveFileIfExists SanitizedTitle.json → deleted	no log	duplicate owners	copy/paste duplicate — keep one owner SyncProcessor
15	StateRoot = Path.Combine(RepoRoot,"state","youtube") ×5 + ManifestFile + ProcessedDir	codegraph StateRoot → 5 constants DashboardService:9 Orchestrator:14 SyncProcessor:12 Merger:20 Processor:15	same literal	—	dupe	copy/paste dupe → PathResolver.YouTubeManifest/Processed/Raw/Deleted
16	DashboardService singleton GoogleSetup:69	codegraph GoogleSetup → AddSingleton<DashboardService> but DashboardService:19 GenerateDashboardDataAsync all static, 0 ctor consumers	none	—	never-consumed registration	dead version — delete registration not feature
Rule from your prompt consumed but never produced elsewhere:
Pattern	Example	Logged?	Logical vs dead	Missing consumption?
consumed never produced	YT.RateLimit at :79	no — Warn never hit	logical error missing mapper	fix producer emits it
produced never consumed outside	UpdatedSnapshots	no — redundant with per-playlist save	dead field not bug	delete
produced consumed only inside	Merger.Groups* logged :314	yes inside	dead API outside	drop fields keep log
never both	PlaylistNotFound	no	true dead	delete
Wrappers closely checked: DeletePlaylistAsync/InsertPlaylistItemAsync/FetchItemsAsync 3× catch(Exception)→ApiError + Activity.Complete(Debug)+Error log identical — copy/paste typed catch missing. Only SortService:305 when IsQuotaOrRateLimit got it right — pattern known not applied. dict-filter Where(!ids).ToDictionary ×2 Orchestrator:82+110 identical → WithoutIds helper.
Copy/paste verified: StateRoot ×5, dict-filter ×2, ArchiveDeleted ×2, catch→ApiError ×3 — all shrinks keep feature.
Dead version (YouTube): Sinks.Console PackageVersion:19 0 refs YouTube-unrelated; YouTube dead version = DashboardService singleton + YT.Playlist/VideoNotFound factories. Delete lines, not behavior.
2. YouTube spec sheet — from beginning (YouTube only)
File	LOC	Keep?	Finding bucket
YouTubeFetchState 126	F1 cache	keep	B StateRoot dupe → constant, D ArchiveDeleted duplicate
ChangeDetector 62	F2	keep file if tested else inline C	yagni
PlaylistService 339	F3,F5	keep — PaginateAsync correct	fix typed GoogleApiException mapper
Orchestrator 418	F1-F5,F7	keep — god by dispatch fix	C inline Combine+Detector, B dict-filter, A dead fields, god 4 Execute*→2 opts{Sort,NoTranslate} delegate title to SyncProcessor → CLI→Orch→SyncProcessor→Processor 418→~260
SyncProcessor 383	F3,F6,F7,F9	keep	A UpdatedSnapshots delete, ShouldBreak → ErrorOr, B archive owner
Processor 351	F3,F8	keep	ApiError catch stays but mapper added upstream
VideoService 82	F3	keep	—
SortService 421	F6,F7 LIS	keep LIS OrderBy not equivalent — quota 150 writes	E
Merger 446+Policy 65	F5	keep	A Groups* dead fields keep log
Translation 267	F8 70/30000+Deva	keep	mapper for Azure.*
DashboardService 112	read	keep	A registration dead
GoogleSetup 74	wire	keep	—
YouTube net: -90 to -150 LOC + 3 factories (Playlist/VideoNotFound, Azure.ServiceUnavailable) + 4 fields
             13 files →12 if Detector inline, 418 →~260, 3 sinks always after Telemetry gate fix,
             0 feature loss (F1 state cache, F2 ETag, F3 bulk+title, F4 single, F5 bigger-wins missing-merge+insertCap100+ContainsAll verify→delete, F6 LIS 150 quota, F7 resume LastSort*, F8 translate, F9 archive/incremental save all preserved)

             YouTube — full spec from scratch
13 files Services/Google/YouTube/* 3146 LOC, 1 GoogleSetup. All source pulled verbatim via codegraph. Prior 418 dive + telemetry + buckets re-scoped to YouTube only. Speculative taxonomy explained line-by-line.
1. Scope + feature keep map
File	LOC	Job	Speech
YouTubeFetchState.cs	126	manifest.json model PlaylistSnapshots{ETag,ReportedVideoCount,LastChecked,LastUpdated,LastSort*} + Load/Save/ArchiveDeleted + JsonOptions/HmsConverter	F1 state cache — keep
YouTubeChangeDetector.cs	62	DetectChanges(current,stored)→New/Changed/Deleted ETag+count	F2 change detect — keep
YouTubePlaylistService.cs	339	PaginateAsync + GetPlaylists/PlaylistItems/ItemPagesRaw/Summaries/Summary + Delete/Insert	F3 bulk fetch + F5 insert/delete — keep
YouTubePlaylistOrchestrator.cs	418	ExecuteCoreAsync Load→Detect→Merge→Process→Finalize + ExecuteWithSortAsync OrderBy+ThenBy+Where(!LastSortCompleted) + ExecuteForTitle* FindByTitle→ETag→ProcessPlaylistAsync	F1 cache read, F2-F5 orchestration, F7 resume — keep
YouTubeSyncProcessor.cs	383	ProcessPlaylistsAsync batch loop + ProcessSinglePlaylistAsync RateLimit/Auth branch + SaveIncrementalState + SortPlaylistsAsync maxWritesPerRun 150 + SortSinglePlaylistAsync→RefreshLocalState + ArchiveDeletedPlaylists	F3 batch, F6 sort quota, F7 resume, F9 archive — keep
YouTubePlaylistProcessor.cs	351	ProcessPlaylistAsync FetchItems→BuildVideoList(duration)→MergeCache→Translate checkpointAsync per batch + RefreshLocalStateAsync	F3 build + F8 translate — keep
YouTubeVideoService.cs	82	GetVideoDurationsAsync batched Videos.List	F3 duration — keep
YouTubeSortService.cs	421	LIS LongestIncreasingSubsequence plan minimizing writes ExecuteSortPlanAsync PlaylistItems.Update 100ms pacing IsQuotaOrRateLimit	F6 sort + F7 resume — keep
YouTubeDuplicateMerger.cs	446	MergeDuplicateGroupsAsync FindGroups→SelectWinner→GetTransferCandidates→insertCap 100→re-list ContainsAll verify→DeletePlaylistAsync→ArchiveMergeManifest→ArchiveLocalFiles	F5 duplicate merge after → before; bigger wins; missing merges — keep
YouTubeDuplicateMergePolicy.cs	65	FindGroups/SelectWinner/GetTransferCandidates/ContainsAll pure	F5 policy — keep
YouTubeTranslationService.cs	267	CollectTargets→BuildBatches MaxTexts 70 MaxChars 30000→ExecuteBatches Transliterate(Deva→Latn)/Translate(en)→ApplyResults+ApplyTranslationResults checkpoint	F8 translate — keep
DashboardService.cs	112	GenerateDashboardDataAsync LoadPlaylists+LoadVideosByPlaylist reverseLookup SanitizeFileName	Dashboard read — keep
GoogleSetup.cs	74	BuildYouTubeServiceAsync OAuth 5min→YouTubeService + AddGoogleServicesAsync 9 singletons	Wire — keep
No feature proposed for removal. Every delete/shrink below keeps F1—F10 flow.
2. Speculative taxonomy — YT.RateLimit unreachable
2.1 The codes
Core/Errors.cs:27  RateLimitExceeded => Failure("YT.RateLimit","YouTube API rate limit exceeded. Retrying...")
Core/Errors.cs:38  QuotaExceeded     => Failure("YT.QuotaExceeded", message)
Core/Errors.cs:36  ApiError          => Failure("YT.ApiError", message)
Core/Errors.cs:31  PlaylistNotFound  => NotFound("YT.PlaylistNotFound", ...)
Core/Errors.cs:33  VideoNotFound     => NotFound("YT.VideoNotFound", ...)
2.2 Consumer that expects it
YouTubeSyncProcessor.cs:79  if (error.Code is "YT.RateLimit" or "Azure.RateLimit") // Warn rate-limit, Break batch
YouTubeSyncProcessor.cs:88  if (error.Code is "Azure.AuthFailed")                  // Error auth, Break
ProcessSinglePlaylistAsync is the only place in repo that reads error.Code. It Breaks the for playlistsToProcess loop — batch stops, already-processed kept, rest skipped.
2.3 Producers — what they actually emit
Producer	Code path	Emits	Matches consumer?
YouTubePlaylistService.PaginateAsync:11	loops request.ExecuteAsync — no try/catch — exception propagates	GoogleApiException propagates, not Error	Not YT.RateLimit — exception not code
YouTubePlaylistService.DeletePlaylistAsync:281	catch(Exception ex) => ApiError($"Failed to delete {playlistId}: {ex.Message}")	YT.ApiError	No — 429/403 never mapped to YT.RateLimit/YT.QuotaExceeded
YouTubePlaylistService.InsertPlaylistItemAsync:328	catch(Exception ex) => ApiError($"Failed to insert {videoId}: {ex.Message}")	YT.ApiError	No
YouTubePlaylistService.GetPlaylistSummaries/Items	exception propagates to Orchestrator.FetchSummariesAndDetectAsync:50 which has no catch — bubbles to LoadStoredStateAsync catch? no — unhandled	exception	No
YouTubePlaylistProcessor.FetchItemsAsync:138	catch(Exception ex) => ApiError(ex.Message)	YT.ApiError	No — wraps any GoogleApiException as ApiError
YouTubeVideoService.GetVideoDurationsAsync	batched Videos.List — catch => ApiError shape	YT.ApiError	No
YouTubeSortService.ExecuteSortPlanAsync:305	catch(GoogleApiException ex) when IsQuotaOrRateLimit(ex) => QuotaExceeded($"Quota exhausted after {successes} updates") + catch(Exception ex) => ApiError	YT.QuotaExceeded correct — only producer that emits typed quota	Consumer never checks QuotaExceeded — opposite gap
YouTubeTranslationService.ExecuteTranslationBatchesAsync:155,186	if(transliterateResult.IsError) return FirstError — propagates Translate.ApiError from Azure.TranslateService	Translate.ApiError	Not Azure.RateLimit/AuthFailed — also unreachable
YouTubePlaylistProcessor duration missing	if(!durations.TryGetValue) skipped++ — not error	no Error	—
Result: YT.RateLimit has 1 consumer, 0 producers. Consumer dead branch — never taken. All YouTube list failures become YT.ApiError, so SyncProcessor:79 always falls through to SyncProcessor:94 Unexpected error: {Description} → Break. Batch still breaks, but via wrong log level (Error not Warn) and wrong message (Unexpected not Rate limit reached).
Same pattern for other factories:
- YT.PlaylistNotFound / YT.VideoNotFound — 0 producers, 0 consumers. Google 404 GoogleApiException.HttpStatusCode==NotFound mapped to ApiError never to these — speculative.
- Azure.AuthFailed / Azure.RateLimit at SyncProcessor:88 — translation failures become Translate.ApiError not Azure.AuthFailed/RateLimit — also dead. Only path that could produce Azure.* is TranslateService which does single catch(Exception)=>Translate.ApiError — typed rate never emitted.
2.4 Logical error vs dead code
Code	Consumed?	Produced?	Bucket	Why not just dead
YT.RateLimit	consumed SyncProcessor:79	never produced	Logical error — fix producer	Consumer expects typed backpressure signal; producer collapses 429 into generic. Fix: inspect GoogleApiException.HttpStatusCode is 429 or 403 && Reason=="rateLimitExceeded" → RateLimitExceeded, quotaExceeded/reason quota → QuotaExceeded, 404 → PlaylistNotFound. Consumer becomes reachable.
YT.QuotaExceeded	consumed nowhere (SyncProcessor never checks QuotaExceeded)	produced SortService:312	Logical error mirror — fix consumer	SortService correctly emits QuotaExceeded, but batch ProcessSinglePlaylistAsync never handles it — falls to Unexpected. Should check YT.QuotaExceeded same as YT.RateLimit → Warn + Break.
YT.PlaylistNotFound/VideoNotFound	never consumed	never produced	Dead code — delete	No handler expects 404 typed — no feature needs it until single-playlist delete-by-id path wants 404 vs 401 distinction. Keep only when delete handler branches on 404.
Azure.RateLimit/AuthFailed	consumed SyncProcessor:79,88	never produced (Translate emits Translate.ApiError)	Logical error — map at boundary	TranslateService should map 429→Azure.RateLimit, 401/403→Azure.AuthFailed so SyncProcessor branch reachable. If Translate stays generic, consumer should check Translate.ApiError message contains 429/401 — worse, fix at Translate.
Copy/paste signal: DeletePlaylistAsync:281 and InsertPlaylistItemAsync:328 identical shape catch(Exception)→ApiError + Activity.Complete(Debug) + Error log — copy-pasted typed catch never added. FetchItemsAsync:138 same. SortService:305 is the one that got it right — when IsQuotaOrRateLimit — proves pattern known, just not applied to list producers.
Fix spec YouTube-only:
YouTubePlaylistService.Delete/Insert/Fetch catch (GoogleApiException gex)
  if gex.HttpStatusCode==429 or (403 && gex.Error?.Errors.Any(e=>e.Reason=="rateLimitExceeded")) => Errors.YouTube.RateLimitExceeded
  if gex.HttpStatusCode==403 && gex.Message.Contains("quota") or gex.Error?.Errors.Any(e=>e.Reason is "quotaExceeded"/"dailyLimitExceeded") => Errors.YouTube.QuotaExceeded(g ex.Message)
  if gex.HttpStatusCode==404 => Errors.YouTube.PlaylistNotFound(playlistId)
  else => Errors.YouTube.ApiError(gex.Message)

YouTubeSyncProcessor.ProcessSinglePlaylistAsync:79
  if (error.Code is "YT.RateLimit" or "YT.QuotaExceeded" or "Azure.RateLimit") // add QuotaExceeded

TranslateService.TranslateBatchAsync/TransliterateBatchAsync
  catch HttpRequestException 429 => Azure.RateLimitExceeded
  catch 401/403 => Azure.AuthenticationFailed
Branches become reachable — telemetry Warn rate-limit hits youtube.jsonl+Seq, Error auth distinct, metrics triage correct. No speculative factory kept unless producer now emits it.
3. Wrappers + copy/paste — YouTube only
Wrapper / dupe	File:line	Copy count	One source
StateRoot = Path.Combine(RepoRoot,"state","youtube") + ManifestFile + ProcessedDir/RawDir/DeletedDir	DashboardService:9, Orchestrator:14, SyncProcessor:12, Merger:20, Processor:15	×5 constants ×6 path joins	PathResolver.GetStatePath("youtube") + PathResolver.YouTube{Manifest,Processed,Raw,Deleted} constants
ArchiveDeleted duplicate	YouTubeFetchState:94 ArchiveDeleted vs SyncProcessor:128 ArchiveDeletedPlaylists+ArchivePlaylist:134 same MoveFileIfExists SanitizedTitle.json → deleted	2 owners	Keep SyncProcessor owner — Orchestrator.FetchSummariesAndDetectAsync:79 calls it; delete FetchState one
MergeDuplicateGroupsAsync dict-filter Where(!loserIds).ToDictionary	Orchestrator:82 Deleted + Orchestrator:110 Losers identical	2	YouTubeFetchState.WithoutIds(stored, ids) helper
CombineNewAndChanged [..New,..Changed]	Orchestrator:375	1 expr 1 caller 89	Inline at 89
YouTubeChangeDetector 62 DetectChanges ETag+count pure	ChangeDetector:12 1 func 1 caller Orchestrator:53	—	Inline unless isolated tests exist — keep MergePolicy 65 which is destructive policy not predicate
PaginateAsync<T> 11 generic helper previousToken dup break	PlaylistService:11 3 callers GetPlaylists/Items/ItemPagesRaw	already correct single source	Keep — not dupe, reused
GetPlaylistItemPagesRawAsync 134 wraps PaginateAsync<[PlaylistItemListResponse]> via (IList<T>)[response] single-item list	PlaylistService:149	1	Keep but note awk adapter — typed PaginateRawAsync cleaner
Telemetry Debug Progress Sort progress {Current}/{Total} ({Percent}%)	SortService:286 vs similar SyncProcessor:48 Playlist {Title}: {Videos} — not same, keep	—	—
Dead version: Directory.Packages.props:19 Serilog.Sinks.Console 0 refs YouTube unrelated; YouTube dead version = DashboardService singleton GoogleSetup:69 static-only registration — delete registration. Not copy/paste.
4. Consumed-but-never-produced vs produced-but-never-consumed — YouTube data that never leaves
Data	Direction	Computed at	Consumed at	Logged?	Logical error vs dead
YT.RateLimit code	consumed, never produced	SyncProcessor:79 reads error.Code=="YT.RateLimit"	0 producers emit it	no — Error description logged as Unexpected not RateLimit	Logical error — missing producer mapping above
YT.QuotaExceeded code	produced, never consumed as code	SortService:312 emits it	SyncProcessor:79 checks only RateLimit not QuotaExceeded; SyncProcessor:94 logs error.Description still but batch break reason wrong	yes — sort path logs Quota exhausted after {successes} inside SortService, but ProcessSinglePlaylistAsync path never; SortStatistics returned then logged Finalize:230 Sort complete ... writes	Logical error — extend consumer check YT.QuotaExceeded
YT.PlaylistNotFound/VideoNotFound	never produced, never consumed	factories Errors:31	nowhere	no	Dead code — delete factories until delete handler maps 404
Azure.AuthFailed/RateLimit	consumed never produced	SyncProcessor:88	Translate emits Translate.ApiError	no — logged as Unexpected	Logical error — map at Translate boundary
SyncResult.UpdatedSnapshots Dictionary<string,PlaylistSnapshot>	produced SyncCounters.ToResult:370 updated[s]=s from processedSnapshots	never read — Orchestrator.Finalize:182 reads only ProcessedIds, PlaylistsWithNewVideos, TotalVideos; SaveIncrementalStateAsync:108 already persisted per-playlist so dict redundant	intermediate counters.UpdatedSnapshots[snapshot]=snapshot:58 written but ToResult rebuilds new dict from processedSnapshots anyway	Dead code — delete field SyncResult(ProcessedIds, PlaylistsWithNewVideos, TotalVideos, SkippedVideos)	 
SyncOutcome.IdsWithNewVideos	produced Orchestrator.Finalize:183 result?.PlaylistsWithNewVideos ?? []	consumer ExecuteAsync:25 outcome.Value.Ids only — IdsWithNewVideos second tuple never read	no — Finalize:172 Sync done {New}/{Changed}... logs Count not ids	Dead code — delete field	 
DuplicateMergeOutcome.GroupsProcessed/GroupsDeferred  int+SurvivorsvsRemovedLosers`	produced Merger:324 Survivors/RemovedLosers/WinnersRequiringProcessing/GroupsProcessed/GroupsDeferred	Orchestrator.MergePlaylistsAsync:106 reads only RemovedLosers, WinnersRequiringProcessing — GroupsProcessed/Deferred, Survivors only logged inside Merger:314 Info merge complete then discarded	yes — inside Merger:314 but outside discarded	Dead code from API perspective — keep log, drop fields or keep fields only if orchestrator branches on GroupsDeferred>0; today just SaveAsync regardless	 
SyncProcessor.ProcessResult.ShouldBreak bool	always Break on IsError at SyncProcessor:85,91,99 — ShouldBreak never false on error	checked ProcessPlaylistsAsync:45 if ShouldBreak break	no — logged as Warn/Error inside ProcessSinglePlaylistAsync	Unnecessary abstraction — return ErrorOr<ProcessResult> propagate error, batch break explicit	 
ChangeDetectionResult DeletedPlaylists archiving	consumed Orchestrator:79 ArchiveDeletedPlaylists archive + Where(!deletedIds).ToDictionary + Save	produced ChangeDetector	yes — Telemetry.Info Deleted: {Title}:78	Keep — feature not dead	 
LastSortMoves/LastSortAttempted/LastSortCompleted prioritization	produced SyncProcessor.SortSinglePlaylistAsync:266 stored[playlistId]=snapshot with {LastSort*} + SaveAsync:206	consumed Orchestrator.ExecuteWithSortAsync:201 OrderBy(Contains).ThenBy(LastSortMoves).ThenBy(LastSortAttempted).Where(!LastSortCompleted) resume next run	yes — Telemetry.Info Sorting {Count}/{Total} unsorted:220 + per-item Telemetry.Debug {Repositioned}:274	Keep — F7 resume no change, flagged only for collapse under opts.Sort	 
UpdateItemPositionAsync yt.PlaylistItems.Update write 50 quota	produced PlaylistService:127 Update(item) → SortService:298 loop 150 budget await Update 100ms pacing	consumed SortService:259 successes/writesConsumed/movedItemIds → SyncProcessor:188 writesConsumed+modified → Finalize log writes*50 units	yes — SortStatistics WritesConsumed logged Sort complete: {TotalWrites} writes ({WritesUnits} units):220 + InsufficientDisk? no	Keep — F6 quota feature core LIS/write budget	 
Rule: consumed-never-produced YT.RateLimit/Azure.AuthFailed = missing mapping bug. Produced-never-consumed UpdatedSnapshots/IdsWithNewVideos/GroupsProcessed/ShouldBreak = speculative return surface — delete fields, keep logs inside producer.
5. Buckets re-sorted — YouTube only, no feature loss confusion
A dead — delete safely (0 feature): Errors YT.PlaylistNotFound/VideoNotFound factories,
  SyncResult.UpdatedSnapshots, SyncOutcome.IdsWithNewVideos,
  DuplicateMergeOutcome.GroupsProcessed/GroupsDeferred(+Survivors if unused),
  ProcessResult.ShouldBreak, CombineNewAndChanged inline, DashboardService DI registration,
  StateRoot×5 constants → PathResolver, FetchState.ArchiveDeleted duplicate.

B dupe — shrink to one source (keep feature): StateRoot/Manifest path×5 → constant,
  dict-filter Where(!ids).ToDictionary ×2 → WithoutIds helper,
  Delete/Insert ApiError copy-paste → typed GoogleApiException mapper,
  PaginateAsync already single source keep.

C yagni file — collapse inline (keep feature): YouTubeChangeDetector 62 1 caller 1 func →
  inline into FetchSummariesAndDetectAsync unless tested; MergePolicy 65 KEEP destructive policy;
  4 Execute* dispatch sprawl → 2 methods ExecuteAsync(opts{Sort})+ExecuteForTitleAsync(title,opts).

D layer misplace — move not delete: (YouTube none beyond StateRoot placement)
  Title path direct playlistProcessor:292 bypasses SyncProcessor → delegate to
  syncProcessor.ProcessSinglePlaylistAsync — same F4 feature, correct depth
  CLI→Orchestrator→SyncProcessor→Processor (today CLI→Orchestrator→Processor skips layer).

E keep — legitimate SRP/algorithm: SortService 421 LIS quota optimization (OrderBy not equivalent),
  Merger 446+Policy 65 destructive group→winner→transfer→verify→delete→archive,
  PaginateAsync+PlaylistService CRUD, Processor Fetch→Build→MergeCache→Translate checkpoint,
  Translation 70/30000 batching + transliterate Deva, VideoService duration batch,
  FetchState manifest+save+sanitize, ChangeDetector predicate if tested.
6. YouTube net — ranked
Prio	Tag	File:line	Change	Lose feature?
P0	fix	PlaylistService:281,328 + Processor:138 + SortService:305 pattern	Typed GoogleApiException → RateLimitExceeded/QuotaExceeded/PlaylistNotFound else ApiError	No — rate backpressure becomes reachable
P0	fix	SyncProcessor:79 + TranslateService	if Code is YT.RateLimit or YT.QuotaExceeded or Azure.RateLimit + Translate 429/401 map to Azure.*	No — batch break logs correct level
P1	delete	Errors:31,33 PlaylistNotFound/VideoNotFound	Delete factories until 404 branch added	No — speculative
P1	delete	SyncProcessor:323 UpdatedSnapshots, Orchestrator:182 IdsWithNewVideos, Merger:10 Groups*	Drop speculative return fields — keep Merger:314 log inside	No — logged anyway
P1	shrink	Orchestrator:22,187,231,326 4 Execute*	ExecuteAsync(SyncOptions{Sort,NoTranslate}) + ExecuteForTitleAsync(title, SyncOptions) delegate title to SyncProcessor	No — 2 pipelines same, fewer entry points; orchestrator 418→~260
P2	shrink	Orchestrator:82,110 dict-filter	YouTubeFetchState.WithoutIds(stored, ids)	No
P2	delete	FetchState:94 vs SyncProcessor:128	Keep SyncProcessor archive	No — one archive
P2	delete	Orchestrator:375 + ChangeDetector:12	Inline trivial Combine + inline ChangeDetector if untested	No — DetectChanges same
P3	delete	GoogleSetup:69	Remove DashboardService singleton	No — static methods
P3	shrink	SyncProcessor:45 ShouldBreak	ErrorOr<ProcessResult> propagate	No — error path same
P3	shrink	Services/Google/* StateRoot ×5	PathResolver constants	No
YouTube net: -90 to -150 LOC + 3 factories + 4 result fields, files 13→12 if ChangeDetector inline,
418 god file → ~260, batch debug now hits youtube.jsonl after singular Telemetry gate fix
(groups P0-P1 10 min, verify `dotnet build` + tail youtube.jsonl rate log).

Singular Telemetry gate + audio debug-loss line
Codegraph: Services/Audio 21 files. Audio Debug grepped — all audio services log naked Telemetry.Debug with no ForService(Audio) scope except one.
1. Singular entry = Core/Telemetry.cs sole public surface
Spec: ONLY Telemetry.* is allowed to touch Serilog.
  Telemetry.Debug/Verbose/Info/Warn/Error/StartActivity → Log.Write / Log.Logger.StartActivity
  Telemetry.ForService(ServiceName)→ LogContext.PushProperty("Service", string)
  Telemetry.Configure(level) → sole config point

Ban: Log.*, Log.ForContext, ForContext("Service", string literal), EventLogger, Trace.Write
      outside Telemetry. 3 listeners + TraceListener must route via Telemetry.
Today core compliant — Services.Audio calls Telemetry.Debug/Warn/Info/Error correctly, ProcessRunner:37,260,270 logs via Telemetry. Violation at edges:
- AzureSdkEventListener:23 Log.ForContext("Service","SdkDiagnostics") + ClientModel:30 + Speech:11 — bypass ServiceName enum, string literal "SdkDiagnostics" not ForService(SdkDiagnostics).
- OciDashboardDeployer logs without scope → filtered out (below).
Fix: make enum literal impossible — Telemetry.Log(ServiceName service, LogEventLevel level, template, args) that pushes ForService internally and is only public method besides Configure/StartActivity. Existing Debug helpers become private or delegate to it. Log.ForContext banned outside Telemetry.cs via .editorconfig CA1707 rule or code-review gate.
2. Three sinks = always, gated correctly after fix
Configure:16 LevelSwitch = new(level) from args --verbose?Verbose : --debug?Debug : Information
  → Spectre:  .MinimumLevel.Verbose().ControlledBy(LevelSwitch) → console respects flag
  → File:    .MinimumLevel.Verbose().Filter(Service==X).WriteTo.File(CompactJson, Debug) ×10
  → Seq:     if TcpClient 500ms probe ok → WriteTo.Seq → Seq
  Log.Logger = config.CreateLogger()
Current spec wrong on two gates — fix required for singular gate to be meaningful:
- Bug — Spectre only gated. AddServiceLogger:52 MinimumLevel.Verbose() + restrictedToMinimumLevel:Debug ignores LevelSwitch. So --verbose Verbose hits Spectre but not state/logs/audio.jsonl; file floor Debug. Invoker expects Verbose in file → invisible. Fix T-01: AddServiceLogger takes LevelSwitch → .MinimumLevel.ControlledBy(LevelSwitch) or explicit restrictedToMinimumLevel = level <= Verbose ? Verbose : Debug. After fix, Verbose 5 callers (Audio probe start DsdConvert:40 etc.) appear in audio.jsonl only with --verbose.
- Bug — Seq probe TCP ≠ healthy. IsSeqReachableAsync:91 TcpClient.ConnectAsync(SEQ_URL) tests localhost:5341 open, not Seq HTTP /health. SEQ_URL default http://localhost:5341 no doc. Fix T-02: drop probe, WriteTo.Seq unconditionally + Serilog.Sinks.Seq handles retry; or probe HttpClient GET /api when SEQ_ENABLED=1.
After fixes: 1 entry (Telemetry) × 3 sinks (terminal/File/Seq) always.
3. Audio debug lost — exact line
PipelineOrchestrator:30  using var _ = Telemetry.ForService(Audio);   // whole RunAsync scoped ✓
  ProcessIsoAsync:184    Telemetry.Info("Probing {Disc}")            // inside scope → file ✓
  ProcessIsoAsync:228    Telemetry.Info("Disc {Disc}: case B")       // inside scope → file ✓
  ConvertDiscAsync:379   convertService.ProbeDsdAsync → Telemetry.Debug   // CALLS leave scope

DsdConvertService:40     Telemetry.Debug("ProbeStart file={File} size={Size}MB")      // NO ForService → LOG LOST to file
DsdConvertService:150    Telemetry.Debug("ProbeComplete rate={Rate}")                 // LOST
DsdConvertService:190    Telemetry.Debug("GainCalcStart")                               // LOST
DsdConvertService:222    Telemetry.Debug("GainCalcComplete peak={Peak} gain={Gain}")  // LOST

SaraconService:??        Telemetry.* via ProcessRunner path same
SoxService:??            Telemetry.* same
ProcessRunner:37         Telemetry.Debug("Start binary={Binary} args={Args}")         // LOST — RunAsync no ForService
ProcessRunner:103        Telemetry.Debug("CompletionDetected pattern={Pattern}")      // LOST
ProcessRunner:260        Telemetry.Debug("Complete exitCode={ExitCode} elapsed={ElapsedMs}") // LOST
ProcessRunner:270        Telemetry.Debug("Stderr …")                                   // LOST

FlacCompletenessChecker, DiscOutputInspector, DffMetadataStripper, CueParser — all Telemetry without ForService → all LOST to file
Why lost: AddServiceLogger:53 Filter.ByIncludingOnly(e.Properties["Service"] == service.ToString()). LogContext.PushProperty("Service","Audio") only inside PipelineOrchestrator.RunAsync:30 using block. Calls leave that method → DsdConvertService/Saracon/ProcessRunner execute still inside using if on same async stack? Actually RunAsync await ProcessIsoAsync → await ConvertDiscAsync → await DsdConvertService.ProbeDsdAsync — LogContext flows via AsyncLocal in Serilog, so nested calls do inherit Service=Audio while inside RunAsync scope. So bulk sacd-convert path does hit file.
Loss line is DsdConvertCommand path: CLI/Audio/DsdConvertCommand:?? calls DsdConvertService directly (not via PipelineOrchestrator:8), no ForService in DsdConvertCommand grep? Check DsdConvertCommand.cs — no ForService. So DsdConvertService Debug there has no Service property → filtered out → audio.jsonl empty. Live FS audio.jsonl 3.5KB vs youtube 1MB confirms bulk path rarely used, single-file path loses all Debug.
Second loss: SyncProcessor.ProcessPlaylistsAsync:39 Debug("Playlist {Title}") + SortPlaylistsAsync:153 Debug("Sorting") — YouTubeSyncProcessor never pushes ForService(YouTube); only Orchestrator.ExecuteCoreAsync:33 does. So batch-loop Debug lost to youtube.jsonl.
Singular gate fix — 5 lines, no feature loss:
File:line	Change	Effect
Telemetry:45 AddServiceLogger	.MinimumLevel.ControlledBy(LevelSwitch) not Verbose + restrictedToMinimumLevel = LevelSwitch mapping	File respects --verbose
Telemetry:70 new Log(ServiceName svc, LogEventLevel lvl, template, args)	using(var _=LogContext.PushProperty("Service", svc.ToString())) Log.Write(lvl, template, args); — push+write atomically, no caller ForService discipline	Caller cannot forget scope — file never lost
Telemetry:73 existing Debug/Info etc.	public static void Debug(ServiceName svc, template, args) => Log(svc, Debug, template, args); or keep overload delegating to Log(svc,...)	Singular entry preserved — 5 wrappers become sugar over 1 gate
PipelineOrchestrator:30 + DsdConvertCommand + SyncProcessor	Replace using var _ = ForService + Telemetry.Debug with Telemetry.Debug(ServiceName.Audio, ...) per call or one using at command entry ExecuteAsync ForService(Audio) covering DsdConvertService	DsdConvertCommand standalone path now hits audio.jsonl; SyncProcessor batch Debug hits youtube.jsonl
LogPaths:8 Setup/Format/FormatText	LogPaths becomes Telemetry enricher: Telemetry.EnrichAudio(isoRoot, outRoot) stores AsyncLocal roots, Telemetry.FormatPath called inside Log before Log.Write — one place «ISO/OUT/TMP» replacement	Side channel eliminated — Services.Audio stops importing LogPaths, single logging concern
LogPaths why it existed: «ISO»\file.dff human console brevity for ProcessRunner:40 LogPaths.FormatText(args) — not structured property. Keep Format as Telemetry private, call inside singular gate. Static IsoRoot/OutputRoot LogPaths:5 global mutable set in PipelineOrchestrator:71 try/finally — AsyncLocal after move, not static.
net: 0 deletions — all telemetry kept, 1 gate enforced.
Remaining gods: 0 — Audio largest file 474→~340 after FRM8 dedupe, logging no longer leaky.
Debug visible: terminal (Spectre) always, youtube.jsonl/audio.jsonl always when ForService present, Seq always (probe removed) — invoker sees same event 3 places.

Re-bucketed spec — feature vs dead vs abstraction
Your confusion valid — prior 418 dive mixed 3 buckets in one list. Below: every prior C-/A-/G-/SH- re-sorted into 5 buckets where only Bucket A deletes anything; B/C/D keep the feature, just cheaper. YT features explicitly mapped so nothing looks like feature removal.
YT feature inventory — what exists vs what was flagged
#	Feature (keep, not cut)	Owner file	Flagged?	What flagged really meant
F1	State cache manifest.json PlaylistSnapshots{ETag,ReportedVideoCount,LastChecked,LastUpdated,LastSort*} to skip full sync	YouTubeFetchState:7,33	No cut	Keep. Flagged SH-01 = StateRoot="state/youtube" literal ×6 → GetStatePath() constant. Dedupe path, not cache. G-01 duplicate archive owner, not cache.
F2	Change detection ETag+count diff DetectChanges(New/Changed/Deleted)	YouTubeChangeDetector:12	File inline only	Feature kept. Flag = 62 LOC file 1 caller 1 pure func. If untested, inline DetectChanges call into Orchestrator.FetchSummariesAndDetectAsync:53 — same logic, one file fewer. If tested, keep file. Not deletion.
F3	Bulk sync Load → Detect → Merge → Process batch → Save	Orchestrator.ExecuteCoreAsync:28 → SyncProcessor.ProcessPlaylistsAsync:22 → PlaylistProcessor	No cut	Keep. Flagged GOD-01 = 4 public Execute* dispatch sprawl (bulk, bulk+sort, title, title+sort) — collapse to 2 methods ExecuteAsync(opts{Sort,NoTranslate}) + ExecuteForTitleAsync(title,opts). Same 2 pipelines, fewer entry points.
F4	Single-playlist sync by title FindByTitle → ETag skip → ProcessPlaylistAsync → Save	Orchestrator.ProcessTitlePipelineAsync:257 + FindPlaylistByTitleAsync:349	No cut	Keep. Flagged layer bypass: title path calls playlistProcessor direct [Orchestrator:292] bypassing SyncProcessor depth CLI→Orch→Processor vs bulk CLI→Orch→SyncProcessor→Processor. Fix = delegate title to SyncProcessor.ProcessSingleAsync, not direct. Feature identical.
F5	Duplicate merge find same-name groups → SelectWinner biggest → GetTransferCandidates missing videos → insert insertCap 100 + 100ms pacing → re-list verify ContainsAll → delete losers → archive merge-manifests/*.json + move processed/raw → deleted/	YouTubeDuplicateMerger:32 446 + DuplicateMergePolicy:FindGroups/SelectWinner/GetTransferCandidates	No cut	Keep entire flow. Flagged only dead result fields GroupsProcessed/Deferred + Survivors kept, counts logged then discarded. Deleting fields ≠ deleting merge. ContainsAll linear → HashSet is stdlib perf, not feature.
F6	Sort playlists LIS plan minimizing YouTube writes, maxWritesPerRun 150, remainingBudget, quota break	YouTubeSortService:421 + SyncProcessor.SortPlaylistsAsync:147	No cut	Keep. Explicitly yagni: Do not replace LIS with OrderBy [YouTube report P11] — OrderBy loses quota optimization. Prior spec kept file — flagged only resume bug, not delete.
F7	Resume incomplete sort LastSortMoves/LastSortAttempted/LastSortCompleted + prioritized OrderBy(Contains)→ThenBy(LastSortMoves)→ThenBy(LastSortAttempted)→Where(!LastSortCompleted)	Orchestrator.ExecuteWithSortAsync:201 + SyncProcessor.SortSinglePlaylistAsync:266	No cut	Keep. Flagged prioritization kept verbatim — GOD-01 only moves it under collapsed opts.Sort.
F8	Translate titles batch 30k chars / 70 texts via Services.Azure.TranslateService checkpoint	YouTubeTranslationService:267	No cut	Keep. Not flagged for deletion.
F9	Sorted archive + incremental save ArchiveDeletedPlaylists + SaveIncrementalStateAsync per-playlist	SyncProcessor:108,128 + YouTubeFetchState.ArchiveDeleted:94	Dedupe only	Keep both behaviors. Flagged G-01 = 2 owners same MoveFileIfExists — keep one owner (SyncProcessor), not delete archive.
F10	Logging youtube.jsonl per sync ForService(YouTube) + StartActivity + ETag skip Info	Orchestrator/SyncProcessor	No cut	Keep. Flagged missing ForService at SyncProcessor:39,153 causes console-only log loss — fix is add using var _ = ForService, not remove log.
Bottom line 6.1: 418 flagged as god by dispatch 4× Execute* + 2× dict-filter + 2 trivial helpers, not by feature count. 7/10 features above live in other files; orchestrator only coordinates. No proposal deletes F1—F10.
Buckets — 5 categories, no feature loss confusion
Bucket A — DEAD CODE — safe delete, 0 feature loss
ID	File:line	What	Why dead	Feature lost?
C-01	Errors:9,21,27,44,56,159 13 factories	General×2, Validation×1, YouTube×3, Azure×3, LastFm×2, Audio×2 0 callers grep	Speculative taxonomy — YouTube.RateLimitExceeded checked at SyncProcessor:79 but producers emit ApiError → unreachable	None — re-add when needed
C-02	Text:28 Has,StartsWith	0 callers	Wrapper	None
C-03	5× csproj SSH.NET	Only OciDashboardDeployer:2 uses Renci	Copy-paste	None — keep in CLI
C-04	Directory.Packages.props:19 Sinks.Console	0 refs	Dead version	None
A-01	SacdProbeRunner:1 357 + SacdProbeService:1 15 + RealDffFixture:1 50	422 harness C:\Temp\t.dff hardcode, 0 pipeline caller, 4th FRM8 walker, hand-rolled Process.Start bypass	Dev forensic not prod pipeline — ProbeDsdAsync parser stays	None — diagnostic moves to tools/sacd-probe, ProbeDsdAsync → DffHeaderReader stays in Services.Audio for per-disc sampleRate/channels
A-02	PathValidator:18 ValidateOutputDirectory 25	0 callers	Dead	None
G-03 / C-01	SyncProcessor:323 UpdatedSnapshots, Orchestrator:182,394 IdsWithNewVideos, Merger:10 GroupsProcessed/Deferred	Computed → logged → discarded 0 readers	Speculative result surface	None — survivors/removed kept
CLI-01	TranslateCommand:59 --from	Registered, ignored at 22	No-op flag	None — wire or delete flag
CLI-02	SacdConvertCommand:18 24/both	Rejects all except 16 at 37	Advertise vs validate mismatch	None
A net: ~530 LOC, -6 files if harness moves, 0 features
Bucket B — DUPLICATE CODE — shrink to single source, feature preserved
ID	File:line	Dupe	Single source	Feature?
A-04	DsdConvert:50 + Saracon:304 + DffStripper:138 + Fixture:30 4× FRM8 walk	FRM8/PROP/FS/CHNL/DSD same padding	DffHeaderReader static →(rate,channels,dsdBytes,hasId3)	Probe stays, cheaper
A-06	ProbeRunner:210 vs Saracon.BuildD2pArgs:69 15 inline args	Same d2p args	Call BuildD2pArgs	Convert stays
G-01	FetchState:94 vs SyncProcessor:128 duplicate archive MoveFileIfExists	Same deleted/*.json	Keep SyncProcessor owner	Archive stays, one path
SH-01	Path.Combine(RepoRoot,"state","youtube") ×6, manifest.json ×5	Same path	PathResolver.GetStatePath("youtube")	State path stays
AZ-01	TextAnalytics:21 5× 5120-char guard + 5× telemetry/catch	Same plumbing 75-95 LOC	Central ValidateLength + RunTextAnalyticsAsync runner	5 ops stay, 1 guard
CLI-03	Azure/*.cs 84 Match ×7 + 60 AsyncCommand boilerplate	Same shape	CliResult.ToExitCode helper	7 commands stay
CLI-06	DashboardGenerate:21 vs SyncYoutube:65 Builder→Generator→WriteAllText	Same flow	DashboardService.GenerateAndPersistAsync one call	Dashboard stays
AP-01	Program:85 2× identical catch 6 lines	Same body	catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)	Startup stays, also fixes uncovered HttpRequest
B net: ~-220, 0 features removed — all shrink
Bucket C — UNNECESSARY ABSTRACTION / YAGNI FILE — collapse, feature preserved inline
ID	File:line	Abstraction	Collapse	Why yagni vs keep	Feature?
G-04	Orchestrator:375 CombineNewAndChanged 2 LOC	1 expr 1 caller	Inline at 89 [..New,..Changed]	Trivial — file not needed	toProcess stays
G-04b	YouTubeChangeDetector:12 62 LOC	1 pure Diff 1 caller Orchestrator:53	Inline unless isolated tests exist	Keep if tested — else yagni	DetectChanges stays
G-05	SyncProcessor:45 ProcessResult.ShouldBreak bool	Always Break then immediate break 48	ErrorOr<ProcessResult> propagate	Wrapper never varies	Break stays
A-03	DiscState:10 enum file	1 enum 3 consumers already import AudioModels	Merge into AudioModels.cs	File-per-enum ceremony	DiscState stays
Y-01	4× *CommandModule.cs 76 LOC	1 method 1 caller Program:109	Inline into Program.Configure until branch grows	Prior App report says pattern justifies keep for discoverability — optional yagni	Branch stays
C net: ~-70, 0 features — indirection removed
Bucket D — LAYER MISPLACEMENT — move, feature preserved, correct SoC
ID	From → To	LOC	Bug if not moved	Feature?
M-01	DashboardDataBuilder 144 + HtmlGenerator 364 CLI → Services/Google/DashboardOrchestrator	508	CLI/AGENTS:24 "no service logic" violated — CLI owns sort/DTO/HTML/JS pure	Dashboard render stays — hand-rolled $$""" kept, no Razor dep
M-02	OciConfig 13 Core → CLI/Dashboard/OciDeployOptions env OCI_HOST	13	Core/AGENTS:24 "Core never owns infra" — Core owns 100.68.154.15 tailnet IP fragile on realloc	Deploy stays — IP via env/DNS
CLI-04	DsdConvertCommand:44 133 workflow CLI → DsdConvertService	133	Pipeline logic in command	Convert stays — thin args→call→exit
CLI-05	SacdConvertCommand:37 report CLI → PipelineOrchestrator return	30	Report in command	Report stays
D net: 0 counted (relocated, not removed), biggest clarity win
Bucket E — KEEP — feature / legitimate SRP, do not cut
Kept	Why not cut	Bucket confusion avoided
PipelineOrchestrator 474 vs DsdConvertService 425	Merge = 899 monster — enumerate/resume vs parse/gain/convert distinct jobs	Not god — SRP split kept
Saracon 338 / Sox 142 / SacdExtract 121	Distinct binaries, BuildD2pArgs vs peak regex vs stereo regex	Not dedupe
YouTubeSortService 421 LIS + DuplicateMerger 446 + MergePolicy 65	Quota LIS not OrderBy, destructive merge needs policy separation	Prior C flagged as keep — agents counting files wrong
CueParser 215 + ReprocessGuard 111 + DiskSpaceChecker 35 + DiscOutputInspector 142	Parser/BOM, guard count→Failed at 3, 4×/8×+500MB — resume correctness	Not trivial
3 EventListeners Azure-Core vs System.ClientModel vs Speech EventLogger	Incompatible SDK APIs — one generic still contains 3 adapters behind façade	Not duping
z440.atl.core, SSH.NET in CLI, Google→Azure direct TranslateService	No stdlib SFTP/metadata, single provider no interface yet	Keep scoped
Net re-bucketed
Bucket A dead:              -530 LOC, -6 files, -3 dep scopes
Bucket B duplicate shrink:  -220 LOC
Bucket C yagni collapse:    -70 LOC, -1 file if ChangeDetector inline
Bucket D move:               0 LOC removed — 521 relocated CLI/Core → Services/CLI
Bucket E keep:               0 LOC — features explicitly preserved F1-F10

Total removable: -820 to -960 LOC (A+B+C), largest file 418→~260 after GOD-01/02/03,
longest method 364→relocated (M-01), 0 feature loss.
Ship A first — 10 min, dotnet build clean, all 10 YT features intact. B/C next — god reduction. D last — layer compliance.

Logging pipeline — full invoker→level→sink trace
Codegraph: Telemetry 112 + Program 126 + 3 EventListeners 104 + LogPaths 61, 29 ForService callers, 19 StartActivity callers, 32 ServiceName refs. Verbatim source above.
Pipeline
Program.Main:33
 args.Contains("--verbose")?Verbose : --debug?Debug : Information  [Program:50]
   → Telemetry.Configure(level)  [Telemetry:16]
       LevelSwitch = new(level)                         // sole level gate
       LoggerConfiguration.MinimumLevel.Verbose()        // global floor = Verbose always on
         .WriteTo.Logger(ControlledBy(LevelSwitch) → Spectre)   // console only gated
         .WriteTo.Logger(Filter Service==X → File) ×10          // file  MinimumLevel.Verbose + restrictedToMinimumLevel:Debug + Rolling Infinite
         .WriteTo.Seq(seqUrl) if TcpClient 500ms probe ok        // seq unconditional after probe

Invoker → ForService(ServiceName) → LogContext.PushProperty("Service", string) [Telemetry:70]
        → Telemetry.Info/Warn/Debug/Verbose/Error → Log.Write(level) [Telemetry:73]
        → Telemetry.StartActivity → SerilogTracing LoggerActivity(Debug) [Telemetry:88]
        → SdkDiagnostics: 3 listeners → Log.ForContext("Service","SdkDiagnostics").Write(mapped) [Azure:25,ClientModel:30,Speech:12]
        → TraceSource → SerilogTraceListener.Write → Telemetry.Debug("[TraceSource]") [Program:22]
        → Audio: LogPaths.Setup(isoRoot,outRoot) → Format/FormatText Replace «ISO/OUT/TMP» [LogPaths:8]
Is it well-integrated? 70% yes, 30% loosely coupled
Thought-through parts:
- Single LevelSwitch controls verbosity via --verbose/--debug — simple, correct.
- ForService + Filter.ByIncludingOnly(Service==X) gives per-service youtube.jsonl tailing without code branching — clean routing.
- StartActivity(Debug) gives structured YouTube.* activities for Seq — 19 callers consistent.
- 3 listeners correctly bridge 3 incompatible SDK event systems (Azure-Core via AzureEventSourceListener, System.ClientModel via EventListener, Speech via EventLogger.OnMessage) — one generic listener would still contain 3 adapters, not reduction.
Poorly integrated — 3 architectural gaps:
1. Level gate only covers Spectre — file/Seq diverge
Configure:24 ControlledBy(LevelSwitch) → Spectre only
AddServiceLogger:52 .MinimumLevel.Verbose()        // file ignores LevelSwitch
                   restrictedToMinimumLevel:Debug  // file floor = Debug, Verbose never hits file
IsSeqReachable:39 Seq sink after probe, no level filter
- --verbose → Spectre Verbose, file Debug (Verbose lost to file), Seq Verbose.
- --debug → Spectre Debug, file Debug, Seq Debug.
- No flag → Spectre Info, file Debug, Seq (probe) Debug.
- File always Debug regardless of flag — --verbose Verbose invisible in state/logs/*.jsonl. Verbose 1 caller? Grep 0 prod hits — dead level for files. Level intent not propagated.
Fix: AddServiceLogger should MinimumLevel.ControlledBy(LevelSwitch) or at least restrictedToMinimumLevel = LevelSwitch mapping, or keep file Debug fixed intentionally — document split.
2. Service scoping inconsistent — logs silently miss file
ForService pushes LogContext property required by Filter.ByIncludingOnly Telemetry:53. Without it, event hits Spectre/Seq but Filter drops it — file loss, no error.
Caller	ForService?	File hit?
YouTubePlaylistOrchestrator.ExecuteCoreAsync:33 ForService(YouTube)	yes	yes — bulk path
YouTubePlaylistOrchestrator.ExecuteForPlaylistTitleCoreAsync:251	yes	yes — title path
YouTubePlaylistService.Delete/Insert:268	yes	yes
YouTubeSyncProcessor.ProcessPlaylistsAsync:39	no	console only — Telemetry.Debug("Playlist {Title}: {Videos}") lost to file
YouTubeSyncProcessor.SortPlaylistsAsync:153	no	lost
YouTubeDuplicateMerger.Merge:37	yes	yes
DashboardService.GenerateDashboardDataAsync	yes? (grep says yes)	yes
LastFmService.FetchPageAsync	partial	some with, some without
Services/Audio/* PipelineOrchestrator, DsdConvert, Saracon, Sox	no	all Audio Debug lost to audio.jsonl — live audio.jsonl 3.5KB vs youtube 1MB confirms
OciDashboardDeployer.DeployAsync:11	no ForContext("Service","SdkDiagnostics") string literal, not ForService(SdkDiagnostics)	filtered only if string matches "SdkDiagnostics" — works but bypasses ServiceName enum, inconsistent
AzureSdkEventListener:25 + ClientModel:30	ForContext("Service","SdkDiagnostics")	same string-literal bypass
Live state/logs/: 8/10 files 0 B (docintel,lastfm,openai,sdk,speech,textanalytics,translate,vision) — not because no logs, because ForService missing or restrictedToMinimumLevel Debug filters Verbose. Architecture thought through for happy path, loosely coupled at edges.
Fix: Enforce using var _ = Telemetry.ForService(ServiceName.X) at operation entry for every Services.* public method — or make AddServiceLogger also accept Source property for deploy. One Telemetry.Log(ServiceName, level, template) helper that pushes scope internally removes caller error.
3. Diagnostics + auxiliary outside Telemetry
enableDiagnostics = logLevel <= Debug [Program:59]  // --debug or --verbose only
  using AzureSdkEventListener(Verbose)   // Azure-Core + Azure-Identity, AllowedEventSources [Azure:10]
  using ClientModelEventListener(Verbose) // System.ClientModel [ClientModel:17]
  using SpeechSdkEventListener(Debug).Activate() // Speech Level map [Speech:20]

Trace.Listeners.Add(SerilogTraceListener) [Program:48] before Configure:55 — Trace.Write → Debug always, unfiltered
LogPaths static IsoRoot/OutputRoot [LogPaths:5] set in PipelineOrchestrator try/finally [Orchestrator:71] — global mutable, not AsyncLocal, outside Telemetry, ReplaceRoot string.Replace over log text
IsSeqReachableAsync TcpClient 500ms [Telemetry:91] blocks startup, tests TCP open ≠ Seq healthy (HTTP)
- Listeners correctly gated by enableDiagnostics, but Speech.Activate() global EventLogger.OnMessage += Handler Speech:30 duplicates if 2 instances — Dispose removes one. MapLevel duplicated as private wrapper Azure:29,ClientModel:34 → EventLevelMapper.Map:8 — trivial but not duping.
- TraceListener + LogPaths are 2nd logging channel not via ForService — LogPaths.Format does «ISO»\path replacement for human console, not structured ForContext. Works but separate concern from Serilog pipeline — not duping, but not integrated.
Fix: LogPaths → AsyncLocal<string> or ILogger.ForContext("IsoRoot", isoRoot) enricher, move TraceListener add after Configure, make IsSeqReachable opt-in via SEQ_ENABLED or cache.
Verdict
Not poor — well-thought core, leaky edges. Core Configure + LevelSwitch + ForService + Filter is clean service-oriented logging. Poorness is inconsistent invoker discipline (29 with scope, ~15 without → silent file loss) and split level gating (Spectre vs file). Not loosely coupled duping — duplication is minimal (MapLevel wrapper ×2, StateRoot ×6 flagged elsewhere). Fix is discipline + 5 lines: propagate LevelSwitch to file sinks, wrap Telemetry.* to auto-scope, move LogPaths to enricher.
net logging: 0 lines removed, +1 helper `Telemetry.ForService` enforcement, +2 config fixes (file level, Trace order)
Largest clarity win: every Service method opens with `using var _ = Telemetry.ForService(...)` — file loss gone.

eep dive on YouTubePlaylistOrchestrator 418 — pulling source to map god signals line-by-line.
⚙codegraph_codegraph_explore [query=YouTubePlaylistOrchestrator, maxFiles=4]
⚙codegraph_codegraph_explore [query=YouTubeSyncProcessor YouTubeChangeDetector YouTubeDuplicateMerger, maxFiles=3]
6.1 — YouTubePlaylistOrchestrator.cs 418 — deep dive
Verbatim source pulled via codegraph — 418 LOC, 1 file, 7 private methods, 4 public entry points, 4 record structs. 1 caller SyncYoutubeCommand, 0 tests.
File map — line-by-line
Line	Symbol	LOC	Branches	Job
22	ExecuteAsync(noTranslate)	5	1	Bulk entry — ExecuteCoreAsync → [] on error
28	ExecuteCoreAsync	15	1	Railway Load → Detect → Merge → Process → Finalize
45	FetchSummariesAndDetectAsync	46	4	GetSummaries + DetectChanges + log new/changed/deleted + ArchiveDeleted + Save + filter dict + CombineNewAndChanged
93	MergePlaylistsAsync	52	4	merger.MergeDuplicateGroupsAsync + prune stored dict + prune changes + re-add WinnersRequiringProcessing
147	ProcessIfNeededAsync	19	1	if 0 → done else syncProcessor.ProcessPlaylistsAsync
168	Finalize	18	1	Telemetry.Info + new SyncOutcome(ids, idsWithNewVideos, stored)
187	ExecuteWithSortAsync	41	3	ExecuteCoreAsync + prioritizedIds OrderBy(Contains)→ThenBy(LastSortMoves)→ThenBy(LastSortAttempted)→Where(!LastSortCompleted) + SortPlaylistsAsync
231	ExecuteForPlaylistTitleAsync	12	1	Single-title entry — ExecuteForPlaylistTitleCoreAsync → Id or null
245	ExecuteForPlaylistTitleCoreAsync	10	1	LoadStoredStateAsync → ProcessTitlePipelineAsync railway
257	ProcessTitlePipelineAsync	67	5	FindPlaylistByTitleAsync → GetPlaylistSummaryAsync → ETag skip → playlistProcessor.ProcessPlaylistAsync → Save + Info
326	ExecuteForPlaylistTitleWithSortAsync	21	1	ExecuteForPlaylistTitleCoreAsync + SortPlaylistsAsync([id]) — always sort
349	FindPlaylistByTitleAsync	24	2	stored.Values.IsEqualToIgnore → else GetSummaries + IsEqualToIgnore
375	CombineNewAndChanged	2	0	[..New, ..Changed] 1 expr, 1 caller FetchSummariesAndDetectAsync:89
378	LoadStoredStateAsync	14	1	try LoadAsync catch → ApiError
394	SyncOutcome / SinglePlaylistOutcome / SyncContext / ProcessOutcome	24	0	4 DTOs — IdsWithNewVideos dead after Finalize:183, UpdatedSnapshots dead in SyncProcessor
Static: StateRoot = RepoRoot/state/youtube:14 + ManifestFile = StateRoot/manifest.json:20 — duplicated ×6 across YouTube* (flagged SH-01).
Why 418 flagged — god signals
god file: 418 > 300 threshold
god dispatch: 4 public Execute* each full pipeline (bulk, bulk+sort, title, title+sort)
god by responsibility: 1 file owns 5 distinct jobs:
  1) global change detection logging (FetchSummariesAndDetectAsync)
  2) duplicate-merge coordination (MergePlaylistsAsync)
  3) batch delegation (ProcessIfNeededAsync → SyncProcessor)
  4) single-playlist direct path (ProcessTitlePipelineAsync → PlaylistProcessor bypassing SyncProcessor)
  5) sort orchestration (ExecuteWithSortAsync prioritizedIds)
god by state mutation: 2 places do stored.PlaylistSnapshots.Where(!ids).ToDictionary + SaveAsync
  FetchSummariesAndDetectAsync:80-86 (deleted) + MergePlaylistsAsync:103-115 (losers)
  Both identical dict-filter pattern — duplicated, not shared helper.
Not just LOC — coordination god. Largest file PipelineOrchestrator 474 kept — single job ISO→extract→convert. This file splits 2 pipelines in 1 class:
Bulk path:  CLI → Orchestrator.ExecuteCoreAsync → SyncProcessor.ProcessPlaylistsAsync → PlaylistProcessor (per item)
Title path: CLI → Orchestrator.ExecuteForPlaylistTitleCoreAsync → PlaylistProcessor.ProcessPlaylistAsync (direct)
                ↑ bypasses SyncProcessor layer → violates depth 5 chain
SyncProcessor already owns ProcessPlaylistsAsync + SortPlaylistsAsync + ArchiveDeletedPlaylists:79 — orchestrator duplicates 2 of 3.
Trivial + dead inside god:
- CombineNewAndChanged:375 — 1 line, 1 caller — inline.
- IdsWithNewVideos:183 computed result?.PlaylistsWithNewVideos ?? [] → SyncOutcome:394 field IdsWithNewVideos → 0 readers after Finalize. SyncResult.UpdatedSnapshots:323 + DuplicateMergeOutcome.GroupsProcessed/Deferred:10 same — logged then discarded.
- YouTubeFetchState.ArchiveDeleted:94 vs YouTubeSyncProcessor.ArchiveDeletedPlaylists:79 — two owners for state/youtube/deleted/*.json, same MoveFileIfExists shape.
Over-engineering vs SRP vs SoC — which is which here
Finding	Bucket	Why
4× Execute* wrapping same core ± sort	Over-engineering — speculative entry points	ExecuteAsync + ExecuteWithSortAsync = bulk × sort flag. ExecuteForPlaylistTitle* ×2 = same. Collapse to ExecuteAsync(opts) + ExecuteForTitleAsync(title, opts) — SyncOptions{bool Sort, bool NoTranslate}. 4→2 methods, -30 LOC. Not SRP — API sprawl.
Bulk vs single-title in 1 class	SRP violation — true god	Bulk = global stored+changes+merge+batch. Title = point lookup FindByTitle+ETag+direct Process. Distinct jobs, distinct error paths. Title path belongs in SyncProcessor or PlaylistProcessor — orchestrator keeps bulk only.
Dict-filter + Save ×2, StateRoot ×6	Duplication — shrink	Where(!ids).ToDictionary copy-paste — extract YouTubeFetchState.WithoutIds(stored, ids) or PruneSnapshots. StateRoot constant → PathResolver.GetStatePath("youtube") (SH-01).
CombineNewAndChanged, YouTubeChangeDetector:62 single pure func	YAGNI — inline	Each 1 caller. ChangeDetector kept only if isolated tests exist — else inline into FetchSummariesAndDetectAsync. DuplicateMergePolicy:65 kept — destructive policy deserves file.
Dead IdsWithNewVideos, GroupsProcessed	Dead code — delete	No caller — speculative result surface. G-03.
Railway ThenAsync in ExecuteCoreAsync vs direct ProcessTitlePipelineAsync imperative	Inconsistency — keep railway	Bulk uses ErrorOr.ThenAsync chain (correct), title uses if IsError return. Not over-engineering — unify on railway.
Agents that flagged ChangeDetector/Combine as yagni correct. Agents that flagged DuplicateMerger/SortService as yagni wrong — those are distinct destructive + quota-critical algorithms (SortService:421 LIS minimizes YouTube write quota).
Fix spec — 6.1 alone
ID	File:line	Change	Rational	After
G-03	Orchestrator:182,394 + SyncProcessor:323 + Merger:10	Drop IdsWithNewVideos, UpdatedSnapshots, GroupsProcessed/Deferred	0 readers — dead	-4 fields, SyncOutcome(Ids, State)
G-01	YouTubeFetchState:94 vs SyncProcessor:128	Delete one ArchiveDeleted — keep SyncProcessor owner	Single archive path — drift	-16
G-04a	Orchestrator:375	Inline CombineNewAndChanged → [..changes.NewPlaysists, ..changes.ChangedPlaylists] at 89	1 expr, 1 caller	-2
G-04b	YouTubeChangeDetector:12	Inline into FetchSummariesAndDetectAsync if untested, else keep	File for 1 func, 1 caller	-62 if inlined
SH-01	Orchestrator:14,20	StateRoot/ManifestFile → PathResolver.GetStatePath("youtube") + "/manifest.json" constant	6 copies across YouTube*	-4
GOD-01	Orchestrator:22,187,231,326	4 Execute* → Task<IReadOnlyList<string>> ExecuteAsync(SyncOptions opts, ct) + Task<string?> ExecuteForTitleAsync(string title, SyncOptions opts, ct) — sort flag inside opts, title path delegates to syncProcessor.ProcessSinglePlaylistAsync not direct playlistProcessor	Collapse dispatch god, fix layer bypass	-30, depth restored CLI→Orchestrator→SyncProcessor→PlaylistProcessor
GOD-02	Orchestrator:80,103	Extract PruneSnapshots(stored, idsToRemove) helper in YouTubeFetchState	Dedup dict-filter+Save	-10
GOD-03	Orchestrator:394-417	Move 4 record structs to YouTubeSyncModels.cs or co-locate with consumer	DTOs in orchestrator inflate file	-24, file 418→~260
418 after: ~260 (+ G-04b inline → ~200 if ChangeDetector moves). Largest method ProcessTitlePipelineAsync 67 stays — cohesive ETag+process+save. True god gone — remains coordination file, not dispatch god.
net 6.1: -80 to -140 LOC, -1 layer bypass, -3 dead fields, -1 duplicate archive.
Whole Google module: 3146 → ~3010, max file 418→~300, orchestration depth 5→4 clean.
Debug if moved: nothing lost — Telemetry.ForService(YouTube) + StartActivity in ExecuteCoreAsync:33 and Merger:37 stay in prod; title path Telemetry.Info("unchanged ETag skip"):287 + per-playlist result.Videos/Skipped:316 stay. Forensic saracon matrix already deleted per 6 — not this file.
Lean already? No — true god, shrink not delete. Pipeline topology clean, coordination overgrown.

Why probe flagged + where debug goes
Flagged: SacdProbeRunner:357 + SacdProbeService:15 + RealDffFixture:50 = 422 LOC harness, not ProbeDsdAsync parser. Parser stays — harness moves.
Why flagged
Reason	Evidence
0 pipeline callers	Grep: SacdProbeRunner only hit SacdProbeService:7 → Runner.RunAllAsync. SacdProbeService registered in AudioSetup:20 but no CLI command calls it, PipelineOrchestrator never calls it. Dead prod path.
Prod assembly ships dev forensic	4-variant matrix raw/stripped × headless/visible [SacdProbeRunner:49], registry/OLE classify, journal append docs/superpowers/audits, C:\Temp\t.dff hardcode [RealDffFixture:7 internal but in Services.Audio prod dll], internal fixture shipped to users. Forensic one-off, not operational.
4th FRM8 walker + 2 dupes	DsdConvertService.ProbeDsdAsync:50 + SaraconService.EstimateExpectedPcmBytes:304 + DffMetadataStripper.Scan:138 + RealDffFixture:30 same FRM8/PROP/FS/CHNL/DSD padding. FindSaracon:272 dup ProcessRunner.IsOnPath:333, inline d2p args:210 dup SaraconService.BuildD2pArgs:69, hand-rolled Process.Start+ReadToEndAsync:238 bypasses ProcessRunner timeout/completion. Drift risk.
Wrong concern in Services	Services = ISO→DFF→PCM→FLAC→tag pipeline. Harness = saracon variant matrix diagnostic. SRP violation — operation vs investigation. CLI/AGENTS.md:24 "orchestration in Services" but also Core/AGENTS: Core never owns infra — same logic, Services.Audio shouldn't own dev-machine forensics.
God-like harness	357 LOC, 4 public methods, TerminationReason clone, Kill(entireTree) half used — speculative generality serving 1 adhoc need.
Not flagged: DsdConvertService.ProbeDsdAsync:33 40 LOC FRM8 reader — essential per-disc sampleRate/channels. That walker gets extracted to DffHeaderReader and stays in prod. Flag isolates harness, not capability.
How you keep debug if harness moves
Pipeline debug != harness matrix. Pipeline already has 3 debug channels — harness added a 4th that duplicated them badly:
Debug you need in prod	Where it lives today (stays)
saracon stdout/stderr per convert	ProcessRunner:78 concurrent drain + completionPattern:"100%" + Telemetry.Debug SaraconService:139
sox stats Pk lev → gain	SoxService.GetPeakLevelAsync:11 regex + DsdConvertService.CalculateGainAsync:183
FLAC duration >2s / last <30s	FlacCompletenessChecker:22 sox -D
3× FRM8 header	ProbeDsdAsync (prod)
Moving harness loses nothing from per-convert --verbose/--debug path. LogPaths.Format:20 «ISO/OUT/TMP» replace + Telemetry.ForService(SdkDiagnostics) sinks stay in Services.Audio.
3 options — laziest first
A. tools/sacd-probe/ standalone Exe (spec's pick) — 10 min
tools/sacd-probe/Program.cs  →  refs Services.Audio (ProcessRunner + future DffHeaderReader)
dotnet run --project tools/sacd-probe -- <dff> --journal docs/audits/saracon.md
- Prod Services.Audio -422 LOC, -3 files, no C:\Temp.
- Debug on demand: run tool when DsdConvertService returns Audio.ProbeFailed/ConversionFailed, paste journal. Same 4-variant matrix, same registry classify, writes same audit md.
- Cost: one csproj:11 like ProbeVerify (already pattern), not in Toolbox.slnx or add if you want dotnet build coverage.
B. audio diagnose <iso|dff> CLI command (if you want discoverability)
Toolbox audio diagnose <path> [--visible]  →  calls tools/sacd-probe library
- Same code as A, just CLI/Audio/DiagnoseCommand.cs:30 thin wrapper → ErrorOr. Keeps forensic out of Services.Audio core, but toolbox --help surfaces it. Good if support needs "send me toolbox audio diagnose output".
- Cost: +1 command file, reuses ProcessRunner. No prod pipeline impact.
C. Keep in Services behind IDiagnoseProbe + feature flag (not recommended)
- AddAudioServices(addProbe:false) default, probe only when flag. Still ships C:\Temp path to prod, still 4th walker. YAGNI indirection for 0 pipeline callers.
Recommendation
Ship A, add B if support asks — ponytail ladder rung 1→2: Does this need to exist in prod? No → move to tool. Already in codebase? Reuse ProcessRunner/DffHeaderReader, don't reimplement.
Spec amendment: flag stands, clarified — ProbeDsdAsync + DffHeaderReader stay in Services.Audio for PipelineOrchestrator:350 ConvertDiscAsync gain path; SacdProbeRunner/SacdProbeService/RealDffFixture 422 LOC harness moves to tools/sacd-probe. Debug Telemetry --debug unchanged; deep saracon matrix debug via dotnet run --project tools/sacd-probe.
net after amendment: -422 harness to tools (not counted as deletion), 
                     FRM8 dedupe keeps 1 reader in prod (~-90 net), 
                     docs/audits journal path updates to tools output.

                     Final spec — all proposed changes + rational
79 files, 7 projects, 8 vertice sweeps. No edits applied — spec only. Every change maps to ponytail-audit tag + god-file remediation. Ranked by leverage.
0. Executive
 	Before	After	Delta
LOC removable	 	 	-820 to -960
Files	79	73	-6
Package deps removed	 	 	-3 scopes (Serilog.Sinks.Console dead, SSH.NET ×5, DotNetEnv → stdlib)
God files remaining	4	0	largest 474→~340, longest method 364→relocated
God module	Audio 21 files, Google 2-layer overlap	15 files, 1 orchestration layer	-6 files, -1 layer
Principle: delete > shrink > move > keep. Every keep justified below — not counted in net.
1. Deletions — dead / speculative / prod-leaked
ID	Tag	File:line	Change	Rational	If not done
C-01	delete	Core/Errors.cs:9,21,27,44,56,159 — 13 factories	Remove General.Unexpected/Internal, Validation.RequiredField, YouTube.RateLimit/PlaylistNotFound/VideoNotFound, Azure.AuthFailed/RateLimit/ServiceUnavailable, LastFm.RateLimitExceeded/UserNotFound, Audio.ProcessFailed/PathTooLong	0 callers grep. Speculative taxonomy — YAGNI. Taxonomy re-add cheap when caller appears. Inflates public API 36→23.	Speculative surface misleads (RateLimitExceeded unreachable — YouTubeSyncProcessor:182 checks it but producers emit ApiError).
C-02	delete	Core/Text.cs:28 Has, StartsWith	Delete 2 extensions — 0 callers	YAGNI. Stdlib string.Contains/StartsWith direct.	Dead API suggests used — wastes reader time.
C-03	delete	Core/Core.csproj:11 SSH.NET, Services/Azure/Azure.csproj:12 SSH.NET, Services/Audio/Audio.csproj:6 SSH.NET, Services/Google/Google.csproj:9 SSH.NET, Services/LastFm/LastFm.csproj:6 SSH.NET	Remove PackageReference from 5 projects — keep only CLI (sole Renci.SshNet consumer OciDashboardDeployer:2)	1 file uses SSH, 6 declare it — copy-paste from Directory.Packages.props. Bloats graph, restores.	Transitive confusion, dotnet list package noise.
C-04	delete	Directory.Packages.props:19 Serilog.Sinks.Console PackageVersion	Delete line — 0 PackageReference consumes it, 0 using hits grep	Dead version entry.	Version drift ghost.
C-05	delete	Services/Azure/Azure.csproj:12 SSH.NET already in C-03	Same as C-03 — Azure never imports Renci	—	—
A-01	delete	Services/Audio/SacdProbeRunner.cs:1 357 + SacdProbeService.cs:1 15 + RealDffFixture.cs:1 50 = 422 LOC	Move to tools/sacd-probe/ CLI or delete. Remove from Services.Audio prod assembly.	Prod ships diagnostic harness with C:\Temp\t.dff hardcode RealDffFixture:7, 4th FRM8 walker RealDffFixture:30, hand-rolled Process.Start+ReadToEndAsync bypassing ProcessRunner, FindSaracon dup of IsOnPath. SacdProbeService:3 pure delegation Runner.RunAllAsync 1 call site. 0 pipeline caller. Largest single cut.	Ships dev-machine path to users, duplicate chunk parser drifts, 422 LOC dead in prod.
A-02	delete	Services/Audio/PathValidator.cs:18 ValidateOutputDirectory 25 LOC	Delete method — grep 0 call sites (only definition hit)	Dead code.	Misleads — looks like output validated elsewhere.
A-03	delete	Services/Audio/DiscState.cs:1 10 LOC enum file	Merge enum DiscState {Complete,NeedsPrimaryConversion,NeedsExtraction,InvalidArtifacts,Failed} into AudioModels.cs or DiscOutputInspector.cs	Single-enum file — file-per-enum ceremony. 3 consumers already import AudioModels.	File sprawl — 21 files avg 171 LOC, 5 files <50.
G-01	delete	Services/Google/YouTube/YouTubeFetchState.cs:94 ArchiveDeleted 16 LOC	Delete — duplicate of YouTubeSyncProcessor:128 archival (File.Move + dir create). Keep one owner.	Drift risk — two archive paths diverge.	Duplicate deleted/ handling.
G-02	delete	Services/Google/GoogleSetup.cs:69 DashboardService singleton registration	Remove AddSingleton<DashboardService> — all methods static DashboardService:19, 0 DI consumers (2 call sites call static)	Registration does nothing.	DI lie — looks injected, isn't.
G-03	delete	Services/Google/YouTube/YouTubeSyncProcessor.cs:323 SyncResult.UpdatedSnapshots, YouTubePlaylistOrchestrator.cs:182 SyncOutcome.IdsWithNewVideos, YouTubeDuplicateMerger.cs:12 DuplicateMergeOutcome.GroupsProcessed/Deferred	Drop 4 fields — computed, logged, then discarded by public APIs (0 readers after log)	Dead result surface — suggests consumers that don't exist.	API confusion, payload bloat.
CLI-01	delete	CLI/Azure/TranslateCommand.cs:59 --from option	Wire from into TranslateService.TranslateBatchAsync(..., fromLang) or delete option	Registered but ignored TranslateCommand:22-26 — user passes --from fr no effect.	Silent no-op flag.
CLI-02	delete	CLI/Audio/SacdConvertCommand.cs:18 format 24/both validation 18-20 rejects all except 16	Fix validation or update advertised formats— currently SacdConvertCommand:37-44 claims 24/both but throws.	Dead feature claim.	User sees option that always fails.
Deletions net: ~530 LOC + 1 PackageVersion + 5 PackageReference scopes + 13 API members
2. Shrinks — same logic, fewer lines (god reduction)
ID	Tag	File:line	Change	Rational	Effort
A-04	shrink/dedupe	Services/Audio/DsdConvertService.cs:50 ProbeDsdAsync + SaraconService.cs:304 EstimateExpectedPcmBytes + DffMetadataStripper.cs:138 ScanChunksAsync + RealDffFixture.cs:30	Single DffHeaderReader static helper → (sampleRate, channels, dsdBytes, hasId3) — 3 callers reuse. Delete 3 duplicate FRM8/PROP/FS/CHNL/DSD walks with same padding.	4 walkers same FRM8 parsing, same padding logic. God by duplication not size. Single source fixes drift.	1 file + ~30 line net
A-05	shrink	Services/Audio/ProcessRunner.cs:9,78 361 LOC → ~240	Keep timeout + Cancellation, drop inactivityTimeout, completionPattern:"100%" grace-kill, KilledAfterCompletionMarker, 2 of 6 TerminationReason variants. Gate completion grace behind single SaraconService:139 call if needed.	Only saracon uses completionPattern:"100%". Sox/sacd_extract sub-second never hit it. 6 termination reasons serve 1 path — speculative generality. God method RunAsync ~180 LOC, 8 branches.	~-120
A-06	shrink/dedupe	Services/Audio/SacdProbeRunner.cs:210 inline d2p args vs SaraconService.cs:69 BuildD2pArgs	Delete inline new[]{"-c","d2p",...} — call BuildD2pArgs	15 args drift risk.	1 line
A-07	fix	Services/Audio/DffMetadataStripper.cs:22 HasId3Chunk sync-over-async GetAwaiter().GetResult()	Make caller await HasId3ChunkAsync — single caller DsdConvertService.PrepareDffAsync already async	Antipattern — blocks thread, deadlock risk.	2 lines
G-04	shrink	Services/Google/YouTube/YouTubePlaylistOrchestrator.cs:375 CombineNewAndChanged + YouTubeChangeDetector.cs:12 62 LOC	Inline CombineNewAndChanged (1 expr, 1 caller). Inline YouTubeChangeDetector into orchestrator unless isolated tests exist — 1 public func, 1 caller Orchestrator:53	YAGNI files for trivial pure funcs. YouTubeDuplicateMergePolicy:65 kept — destructive policy justifies file.	-63
G-05	shrink	Services/Google/YouTube/YouTubeSyncProcessor.cs:45 ProcessResult.ShouldBreak	Return ErrorOr<ProcessResult> or propagate — ShouldBreak always Break on error then immediate exit	Wrapper carries bool that never varies.	5 lines
T-01	shrink	Core/Telemetry.cs:31-68 10 sinks × Filter.ByIncludingOnly	One state/app.jsonl + jq 'select(.Service=="YouTube")' or keep 10 with rollingInterval:Day, retainedFileCountLimit:7, fileSizeLimitBytes:10MB, fix retainedFileCountLimit:null unbounded, RollingInterval.Infinite no rotation. Live FS: 8/10 files 0 B, 500 MB ceiling.	10 sub-loggers for per-file tailing — YAGNI if Seq available. Current config violates AGENTS.md: 7-day retention. Also OciDeployer logs without ForService — filtered out, silent loss.	5 lines
T-02	shrink	Core/Telemetry.cs:91 IsSeqReachableAsync TCP probe 500ms every startup	Configure Seq sink unconditionally, let Serilog handle unreachable — drop TcpClient.ConnectAsync probe (ignores HTTP health).	Probe tests TCP open ≠ Seq healthy, costs 500ms every cold start when SEQ_URL=localhost:5341 unreachable.	-20
AZ-01	shrink	Services/Azure/TextAnalyticsService.cs:21,82,123,160,197 5× 5120-char validation + 27-73 5× telemetry+catch	Centralize ValidateTextLength guard + one RunTextAnalyticsAsync(Func<Task<T>>) runner preserving per-op messages	75-95 LOC duplicated boilerplate — 14-19% of Azure layer. Not god method, cloned methods.	-75
AZ-02	shrink	Services/Azure/SpeechService.cs:259 ffmpeg Arguments interpolation	ProcessStartInfo.ArgumentList.Add(arg) per token	Manual quoting edge cases. Stdlib ArgumentList escapes correctly.	3 lines
AZ-03	shrink	Services/Azure/AzureSdkEventListener.cs:21 + ClientModelEventListener.cs:31 private MapLevel()	Call EventLevelMapper.Map() direct — wrappers add 0 behavior	Indirection without value.	-6
CLI-03	shrink	CLI/Azure/*.cs 84 LOC Result.Match ×7 + CLI/*Command.cs 60 LOC AsyncCommand<T>.ExecuteAsync boilerplate	One static class CliResult { static int ToExitCode<T>(ErrorOr<T>, Func<T,int>, Func<IReadOnlyList<Error>,int>) } helper	7 copies identical success/error shape + 12 copies signature. Thin-wrapper discipline.	-60
CLI-04	shrink	CLI/Audio/DsdConvertCommand.cs:44 133 LOC probe→tmp→gain→convert→split→tag in command	DsdConvertCommand.ExecuteAsync → DsdConvertService.ConvertSingleFileAsync(input, output, ct) → ErrorOr<ConvertResult> — command keeps args, call, result.Match(onSuccess: print, onError: return 1)	CLI/AGENTS.md:21 "no service logic" violation. 180 LOC command file.	-110
CLI-05	shrink	CLI/Audio/SacdConvertCommand.cs:37 report + DsdConvertCommand 26-38 since-parse	Move reporting into PipelineOrchestrator return value, move --since parse to LastFm settings boundary	Same violation.	-30
CLI-06	shrink	CLI/Dashboard/DashboardGenerateCommand.cs:21 + Sync/YouTube/SyncYoutubeCommand.cs:65 duplicate generation	Single DashboardService.GenerateAndPersistAsync(ct) → ErrorOr<DashboardPaths> reused by both commands	Two paths do Service→Builder→Generator→WriteAllText independently — drift.	-20
SH-01	shrink	Services/Google/YouTube/*.cs Path.Combine(RepoRoot,"state","youtube") ×6	PathResolver.GetStatePath("youtube") + PathResolver.YouTubeManifestPath constant — ReprocessGuard:11 already uses it	6 copies manifest path, 5 copies manifest.json literal.	-5
SH-02	shrink	Services/Audio/FlacCompletenessChecker.cs:100,116 GetFlacsByTrackNumber+FindDffDir statics	Move both into DiscOutputInspector private helpers — only consumer	Split utils from consumer.	0 (move)
AP-01	shrink	App/Program.cs:85 duplicate catch(InvalidOperationException){6} + catch(OperationCanceledException){6}	catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException) — also fixes uncovered HttpRequestException/FileNotFoundException from AddGoogleServicesAsync	Identical bodies — dup-catch smell. Single filter.	-5
AP-02	shrink	App/Program.cs:73 commandArgs.Contains("audio")	commandArgs.FirstOrDefault()=="audio" — path C:\audio\file.iso false-positives fast path	Substring over string[] — fragile.	1 line
Shrinks net: -290 LOC + god reduction (ProcessRunner 361→240, TextAnalytics 255→~160, Orchestrator dispatch 4→1)
3. Moves — correct SoC, wrong layer (not counted in net, but required)
ID	From → To	LOC	Rational
M-01	CLI/Dashboard/DashboardDataBuilder.cs:11 144 + DashboardHtmlGenerator.cs:3 364 → Services/Google/YouTube/DashboardOrchestrator.cs	508	CLI/AGENTS.md:24 "Orchestration belongs in Services". Pure PlaylistSnapshot→HTML/JS with no IO, no Spectre — service logic living in CLI. CLI becomes DashboardService.GenerateDashboardDataAsync → DashboardOrchestrator.BuildAndRenderAsync → WriteAllText. Hand-rolled $$""" raw-string kept — no Razor/Scriban needed (one Generate call, loops already in builder).
M-02	Core/OciConfig.cs:3 13 → CLI/Dashboard/OciDeployOptions.cs or env OCI_HOST/OCI_KEY_PATH	13	Core/AGENTS.md:24 "Core must never reference Services" — Core now owns Host=100.68.154.15 Tailscale tailnet IP, User=ubuntu, ~/.ssh/oci/id_ed25519. Infra in shared. Move to CLI deploy layer, default to OCI_HOST env with tailnet DNS fallback. Hardcoded 100.68.154.15/32 fragile on node re-alloc.
M-03	CLI/Audio/DsdConvertCommand.cs:44 workflow → Services/Audio (already CLI-04)	133	Same layering — not a line cut, a boundary fix. Listed in shrinks for counting; move is the mechanism.
4. Stdlib / native replacements
ID	Tag	File:line	Hand-roll	Stdlib	Why higher rung
S-01	stdlib	Core/Text.cs:7 SanitizeFileName Aggregate+StringBuilder+Contains per char	Simple foreach(c in name) + HashSet<char> of GetInvalidFileNameChars() or string.Create	Current Aggregate allocates StringBuilder per char + Contains linear scan. foreach is correct — not clever, just shorter.	 
S-02	stdlib	Core/Text.cs:23 IsEqualTo/IsEqualToIgnore/Has wrappers	string.Equals(a,b,Ordinal/OrdinalIgnoreCase) / a?.Contains(b,Ordinal) ?? false direct	Thin wrappers over BCL — 1 consumer each for IsEqualTo.	 
S-03	stdlib	CLI/Dashboard/DashboardDataBuilder.cs:129 Escape 5 Replace	System.Net.WebUtility.HtmlEncode(s) + Replace("'", "&#39;")	Stdlib covers 4/5; ' extra preserved.	 
S-04	stdlib	App/Program.cs:46 DotNetEnv.Env.Load(".env") 1 call in repo	10-line File.ReadAllLines(".env") → foreach line handle '#', quotes, '=', SetEnvironmentVariable	Single call, trivial stdlib — dep DotNetEnv 3.1.1 not justified. Keep dep only if .env quoting complexity grows.	 
S-05	native	Core/Telemetry.cs:91 TCP probe	Drop probe — configure sink, let Serilog handle backpressure	Platform already handles unreachable sink.	 
S-06	stdlib	Services/Google/YouTube/YouTubeDuplicateMergePolicy.cs:39 missingIds.Contains(id) linear	HashSet<string> for transfer candidate lookup	Hot loop — Contains O(n) → O(1). Correctness, not just laziness.	 
S-07	native	Services/Azure/SpeechService.cs:259 Arguments	ProcessStartInfo.ArgumentList	Platform escapes correctly.	 
S-08	yagni (keep)	Core/Telemetry.cs:88 SerilogTracing.StartActivity single use	System.Diagnostics.ActivitySource stdlib could replace SerilogTracing 2.4.0	Weak keep — tracing only when --debug Program:59. Delete if not sampled. Listed as optional.	 
5. YAGNI inlines — abstraction with one implementation/caller
ID	File:line	One-impl abstraction	Action	Why yagni vs SRP
Y-01	CLI/Azure/AzureCommandModule.cs:5 + Audio/AudioCommandModule.cs:5 + Dashboard/DashboardCommandModule.cs:5 + Sync/SyncCommandModule.cs:7	4 modules each 1 method, 1 caller Program:109-112	Inline into Program.Configure until branch grows	Pattern consistency justifies retention for discoverability per App audit — keep if team prefers, but strictly yagni today.
Y-02	Services/Google/YouTube/YouTubeChangeDetector.cs:12 62	1 pure Diff(stored,current) func, 1 caller Orchestrator:53	Inline into orchestrator unless isolated tests exist	DuplicateMergePolicy kept — destructive policy justifies file. Detector is diff predicate — no policy.
Y-03	Core/PathResolver.cs:29 GetStatePath + Core/ServiceName.cs:17 ToFileSlug switch	Trivial wrappers, 2 and 1 consumers	Keep GetStatePath only after 3rd state appears; inline ToFileSlug in Telemetry	Higher rung = inline until reuse.
Y-04	Services/Audio/SacdProbeService.cs:3 15	Pure delegation RunAllAsync	Delete wrapper — call runner direct if retained	Already deleted via A-01.
Y-05	Core/Telemetry.cs:73 5 one-line Info/Warn/Debug/Verbose/Error over Log.Write	5 wrappers, 1 behavior	One Telemetry.Log(level, template, args) or call Serilog direct	Wrapper adds no scope policy.
Y-06	Services/Azure/AzureSetup.cs:15 IServiceCollection return vs Audio/LastFm void	No caller chains return Program:79-83	Normalize to void	Chainable return yagni.
6. God remediation — mapping to changes above
God	Type	Fix (ID)	After
YouTubePlaylistOrchestrator 418 4 entry points, global+single	True god — overlapped layer	Collapse 4 Execute* into 1 with SyncOptions{PlaylistTitle?, Sort bool} or push title path to SyncProcessor (G-04) + drop dead fields (G-03)	~340, 1 entry point, single layer
ProcessRunner 361 8 branches, 6 reasons	Speculative generality	Drop inactivity/completion grace (A-05)	~240, 3 reasons
DashboardHtmlGenerator 364 single method template	God method	Move to Services (M-01), keep raw-string — god by definition but relocated not split	364 relocated, layer correct
DsdConvertService 425 probe in conversion facade	Weak god	Extract DffHeaderReader (A-04), ProbeDsdAsync leaves facade	~340, probe isolated
TextAnalyticsService 255 5× cloned plumbing	God by duplication	Centralize guard+runner (AZ-01)	~160
Services/Audio 21 files file-count god	Sprawl not bloat	Delete harness+dead (A-01..A-03) → 15 files, median 135→110	Lean
Services/Google 13 files 2-layer overlap	Layer god	Dedupe archive (G-01), collapse orchestration (G-04), atomic writes (SH-01)	1 orchestration layer
7. What NOT to cut — and why
Kept	Rational
PipelineOrchestrator 474 vs DsdConvertService 425 split	Merging = 899 LOC monster. Facade isolates DFF parse+gain+conversion from ISO enumerate/resume/cleanup — distinct jobs.
Saracon 338 / Sox 142 / SacdExtract 121 each	Distinct binaries, distinct BuildD2pArgs / peak regex / stereo regex. Shared ProcessRunner already deduped spawn.
YouTubeSortService 421 LIS	Quota-optimized LIS minimizes YouTube write quota — OrderBy not equivalent. ponytail: O(n log n) LIS, brute-force if playlists small not needed.
YouTubeDuplicateMerger 446 + DuplicateMergePolicy 65	Destructive merge workflow vs policy — side effects separated from rules. Keep both.
CueParser 215	8 directive parser with BOM/1252 handling — parser density justified.
ReprocessGuard 111 + DiskSpaceChecker 35 + DiscOutputInspector 142	Resume correctness — not speculative. PathValidator only one flagged.
LogPaths 61 static	Fix scope not delete — AsyncLocal or scoped enricher pending, currently global mutable set in try/finally PipelineOrchestrator:71.
3 EventListeners (Azure-Core, System.ClientModel, Speech)	Incompatible SDK APIs — AzureEventSourceListener vs EventListener vs EventLogger.OnMessage. One generic listener still contains 3 adapters.
Services.Google → Services.Azure direct TranslateService dep	Single concrete provider — interface speculative until 2nd provider or test seam.
SSH.NET kept in CLI	No stdlib SFTP — dep justified, just scoped.
z440.atl.core 7.15.3	No stdlib audio metadata.
Hand-rolled $$""" HTML + CDN Tabulator	Adding Razor/Scriban for one Generate call net complexity. Keep.
ErrorOr 2.1.1	Railway explicit — custom Result<T> would trivialize. Keep.
ProbeVerify tools/ProbeVerify 326 standalone harness	AGENTS.md: no test NuGet, standalone Main() verification — satisfies niche. Add to slnx or doc, not delete.
8. Implementation order — laziest first
Phase 1 — deletions (0-risk, no behavior change): C-01..C-05, A-01..A-03, G-01..G-03, CLI-01/02 → -530 LOC, dotnet build clean.
Phase 2 — shrinks (god reduction): A-04..A-07, T-01/02, AZ-01..03, CLI-03..06, SH-01/02, AP-01/02 → -290 LOC, longest method drops below 110.
Phase 3 — moves (boundary fixes): M-01 (Dashboard 508 to Services, biggest clarity win), M-02 (OciConfig env), CLI-04 workflow relocation → 0 net LOC but CLI/AGENTS.md compliance restored.
Phase 4 — stdlib drops: S-04 (DotNetEnv → 10 lines), S-01..03 — dep -1, scope -5.
Verification after each phase: dotnet build (TreatWarningsAsErrors), dotnet run --project tools/ProbeVerify still passes, tail state/logs/youtube.jsonl not empty, dashboard generate html identical diff.
9. Net
net: -820 to -960 lines removable via deletions + shrinks,
     -3 deps (DotNetEnv, Serilog.Sinks.Console, 5× SSH.NET PackageReference scopes),
     -6 files (3 harness + DiscState + ValidateOutputDirectory + ChangeDetector if inlined),
     ~521 LOC relocated (not removed) Dashboard view + OciConfig to correct layer.
     Remaining gods after: 0 true gods — largest file ~340, median ~110.
Ship Phase 1 first — 10-minute win, 530 lines gone, zero behavior change.
▣  Sisyphus - Ultraworker · Muse Spark 1.2 Contributor · 2m 12s

God audit — file / class / method / module vs over-engineering
Criteria: god file >300 LOC · large >200 · god method >75 LOC or >7 branches · god class >7 public methods or 2+ responsibilities · god module >2000 LOC or >12 files or mixed domains. One class per file enforced by repo — god file = god class.
1. God files — ranked
File	LOC	Public methods	Responsibilities in file	God?	Verdict vs prior findings
PipelineOrchestrator.cs	474	4	ISO enumerate, guard, inspector routing, extract, convert, cleanup, log scope	Borderline	Prior: keep split justified — correct. Large but 1 job: orchestrate resume→extract→convert. Not true god; file count is problem elsewhere, not here.
YouTubeDuplicateMerger.cs	446	2	Group, policy, winner pick, transfer, API mutate, archive, manifest	No	Prior: keep — size justified by destructive workflow. Cohesive, not god.
DsdConvertService.cs	425	6	FRM8 probe, ID3 strip dispatch, gain calc, convert+split+tag, derive	Weak god	Prior: facade too broad — probe belongs elsewhere. God because probe (pure IO) leaks conversion facade. Fix: extract DffHeaderReader → file drops to ~340, god gone. Shrink not delete.
YouTubeSortService.cs	421	3	LIS plan, quota-optimized mutation	No	Prior: keep — LIS justified. Algorithmic density, not god.
YouTubePlaylistOrchestrator.cs	418	4	State load, change detect, merge, batch+single sync, sort, persist	True god	Prior: overloaded coordinator, overlaps SyncProcessor. 4 entry points × 1 CLI each, owns both global + single-playlist paths. Fix: move title-sync into SyncProcessor or collapse layer.
YouTubeSyncProcessor.cs	383	3	Batch process, incremental state, sort budget, archive	Weak god	Prior: overlaps orchestrator. Pair completes god module — neither alone god, together duplication creates god layer.
ProcessRunner.cs	361	3	Arg quoting, stdout/stderr drain, timeout/inactivity/completionPattern/kill	True god — speculative generality	Prior: shrink - half serves 1 caller. 6 TerminationReason + completionPattern:"100%" only saracon uses. Sox/sacd_extract sub-second never hit it. Cut ~120 LOC → ~240, god gone.
SacdProbeRunner.cs	357	4	4-variant matrix, registry/OLE classify, journal, hand-rolled Process	Dead god	Prior: delete 422 LOC prod harness. Not god — dead code shipping with C:\Temp hardcode. Delete, not split.
DashboardHtmlGenerator.cs	364	1	Generate(DashboardData)→string embedding 40 CSS + 60 HTML + 280 JS Tabulator	True god method + file	Prior: move CLI→Services, keep hand-roll. God method by definition — 364 LOC single method. Justified to keep raw-string over Razor (no deps), but must be relocated not split. Layer error, not SRP error.
YouTubePlaylistProcessor.cs	351	2	Fetch, video map, cache, translate	No	Cohesive per-playlist processor.
YouTubePlaylistService.cs	339	8	Pagination, CRUD, summaries, raw pages	No	Broad but single boundary: YouTube API facade. Coherent.
SaraconService.cs	338	4	BuildD2pArgs, ConvertDsdToPcm/Flac, header validation, size heuristic	No	Distinct binary wrapper.
TextAnalyticsService.cs	255	5	Sentiment/Entities/KeyPhrases/Lang/PII — each identical 5120-char guard + telemetry + catch	God by duplication, not size	Prior: shrink 75-95 LOC duplicated plumbing. Not god method — 5 medium methods × same template. Extract guard + operation runner → ~160.
SpeechService.cs	284	3	Transcribe, Synthesize, file variant + ffmpeg WAV	No	3 audio ops, distinct SDKs. Keep.
YouTubeTranslationService.cs	267	3	Batch chunk, checkpoint, transliterate	No	Coherent.
DffMetadataStripper.cs	285	2	HasId3Chunk (sync-over-async) + StripId3TagsAsync	No	Single concern, but dedupe FRM8 walker — not god.
CueParser.cs	215	1	BOM detect + 8 cue directives	No	Parser density justified.
Non-god large (>150) kept: DocIntel/Vision/OpenAi <100, Sox 142, SacdExtract 121 — all single binary wrappers. FlacCompletenessChecker 135 borderline — statics belong in inspector (prior fold).
Small-file sprawl (anti-god): 6 files <62 LOC in Audio — DiscState 10, SacdProbeService 15, DiskSpaceChecker 35, PathValidator 43, AudioSetup 47, RealDffFixture 50. Prior correctly flagged: 3 delete/inline, not merge into god.
2. God methods — top 10
Method	File	Est. LOC	Branches	God?	Fix
Generate(DashboardData)	DashboardHtmlGenerator.cs:5	~364	1	Yes — template method	Keep hand-roll, move file to Services/Dashboard. One method is the product; splitting adds indirection.
RunAsync() + ProcessIsoAsync() + ConvertDiscAsync()	PipelineOrchestrator.cs:22,146,350	474 across 3	12+	No individually	Each <120. File looks god but methods are sequenced, not branching monster. Keep.
RunAsync(binary,args,ct,timeout,completionPattern)	ProcessRunner.cs:20	~180	8	Yes	Prior shrink — inactivityTimeout + completionPattern + KilledAfterCompletionMarker branches serve 1 path. Keep timeout+Cancellation.
ConvertAndSplitAsync() + ProbeDsdAsync()	DsdConvertService.cs:33,247	~200	6	Weak	Extract probe to shared reader → method shortens.
ExecuteAsync() variants ×4	YouTubePlaylistOrchestrator.cs:30	418 across 4	10	Yes — dispatch god	Source of true god file. Collapse 4 entry points into 1 with SyncOptions{Playlist,Sort} or push title path to SyncProcessor.
ProcessPlaylistsAsync()	YouTubeSyncProcessor.cs:30	~110	7	No	Borderline but single batch loop. Keep.
ExecuteTranslationBatchesAsync()	YouTubeTranslationService.cs:80	~90	5	No	Chunking logic justified.
AnalyzeAsync() ×5	TextAnalyticsService.cs:21	5× ~45	2 each	No — cloned methods	Not god size; god by copy-paste. Centralize guard.
ProbeDsdAsync() walkers ×4	DsdConvertService/Saracon/DffStripper/Fixture	4× ~40	4 each	Duplicated walkers, not god	Prior dedupe → DffHeaderReader.
Main(args)	App/Program.cs:33	126	6	No	Bootstrap — 126 is correct for wiring; prior ordering fixes (trace before configure, dual catch) matter more than size.
3. God modules
Module	Files	LOC	Domains in module	God?	Verdict vs prior
Services/Audio	21	3601	SACD extract → DSD probe → saracon/sox convert → tag → guard	File-count god, not LOC god	Mean 171, median 135 — sprawl not bloat. 5 files <50, 1 dead method, 1 harness in prod, 4 FRM8 walkers. Net fix is -6 files, -530 LOC (prior P0). After prune: 15 files, ~3070 LOC — lean. Pipeline topology itself clean.
Services/Google	13	3146	Fetch state + playlist/video API + sort + merge + translate + dashboard read	Layer god — overlapped orchestration	Depth 5 CLI→Orchestrator→SyncProcessor→PlaylistProcessor→API. Orchestrator 418 + SyncProcessor 383 own same state dict + archive dup. Not file god — coordination god. Fix: single orchestration layer, dedupe archive, atomic writes.
Services/Azure	12	1084	6 Azure AI SDKs + 3 event listeners	No	Prior keep 3 listeners — incompatible APIs. LOC/file ~90 — not god. Only dedup is TextAnalytics 5× plumbing.
CLI	~22	~1618 scope	Azure/Audio/Dashboard/Sync commands + DataBuilder/HtmlGenerator/Deployer	Layer-violation god	Prior delete/move 587 LOC to Services. CLI should be thin args→service→exit code; currently owns DSD workflow (133) + dashboard view (508). Move, not split.
Core	6	416	Errors + Telemetry + Text + PathResolver + ServiceName + OciConfig	No	Prior delete dead factories + wrappers. Small, cohesive utility. Only god is Telemetry combining 10 sinks + probe — prior shrink to 1 sink.
App	1	126	Bootstrap, DI, log level, cancellation	No	Prior P1-P5 wiring bugs, not size.
Toolbox repo	79	~10k	Audio + Azure + Google + LastFm + CLI + Core + App	No	Build infra exemplary (CentralPackageManagement, UseArtifactsOutput, slnx, TreatWarningsAsErrors). God risk is SSH.NET ×6 + DotNetEnv single call + tailnet IP — scope/deletion fixes.
4. Over-engineering vs SRP vs SoC — where gods land
True over-engineering (cut)        → dead harness, dead fields/factories,
                                     duplicate archive/FRM8/PATH/Match,
                                     10 sinks for 8 empty files
                                     ≠ god — speculative generality, not size

True god (shrink/split)            → YouTubePlaylistOrchestrator,
                                     ProcessRunner completion grace,
                                     DashboardHtmlGenerator single-method template
                                     = size + responsibility overload

Legit SRP (keep, despite size)     → PipelineOrchestrator/DsdConvertService split,
                                     Saracon/Sox/SacdExtract wrappers,
                                     SortService LIS, DuplicateMerger,
                                     DocIntel/Vision/Speech SDK boundaries
                                     = not god; merging would create god

Correct SoC, wrong layer (move)    → DataBuilder+HtmlGenerator CLI→Services,
                                     OciConfig Core→CLI, DSD workflow CLI→Services
                                     = not god file; misplaced concern

Apparent god but adapter necessity → 3 EventListeners, Google→Azure direct dep
                                     = keep; generic façade would hide, not reduce
Agents fit: 5/7 vertice agents correctly flagged dead/duplicated surface; 2 scored SRP splits as yagni by call sites == 1 or LOC >300 alone without checking external contract or binary boundaries. Rule: files > LOC and LOC > responsibility — count responsibilities, not lines.
5. Net after god fixes
Deletions (true cut):     -530  Audio harness + dead method + enum wrapper + SSH scope + dead factories
Shrinks (god reduction):  -290  ProcessRunner grace -120 + TextAnalytics plumbing -95 + DsdConvert probe extract -40 + Orchestrator collapse -35
Moves (not counted):       ~521  Dashboard view 508 + OciConfig 13  CLI/Core → Services/CLI
Net removable:             -820 to -960 lines, -3 deps, -6 files
Remaining gods after:      0 true gods — largest file drops to ~340, longest method to ~110.

Ponytail review — over-engineering only
79 files, 7 projects, 8 vertice agents. Dashboard lane landed late — now closed.
Ranked cuts — L<line>: <tag> <what>. <replacement>.
L1: delete 422 LOC diagnostic harness shipping in prod (C:\Temp hardcode, 4th FRM8 walker, hand-rolled Process.Start). Move to tools/sacd-probe/. [Services/Audio/SacdProbeRunner.cs:1 + SacdProbeService.cs:1 + RealDffFixture.cs:1]
L18: delete PathValidator.ValidateOutputDirectory 25 LOC — 0 callers. Delete method. [Services/Audio/PathValidator.cs:18]
L1: delete DiscState 10 LOC single-enum file + PathValidator 43 LOC trivial wrapper. Inline ValidateInputPath, merge enum into AudioModels. [Services/Audio/DiscState.cs:1 | PathValidator.cs:1]
L94: delete YouTubeFetchState.ArchiveDeleted duplicate of YouTubeSyncProcessor archival. Keep one owner. [Services/Google/YouTube/YouTubeFetchState.cs:94 + YouTubeSyncProcessor.cs:128]
L323: delete dead result fields SyncResult.UpdatedSnapshots / SyncOutcome.IdsWithNewVideos / DuplicateMergeOutcome.GroupsProcessed,Deferred never read after log. Drop fields. [Services/Google/YouTubeSyncProcessor.cs:323 | YouTubePlaylistOrchestrator.cs:182 | YouTubeDuplicateMerger.cs:12]
L9: delete 13 unused Error factories speculative (General×2, Validation×1, YouTube×3, Azure×3, LastFm×2, Audio×2). Re-add when caller appears. [Core/Errors.cs:9,21,27,44,56,159]
L11: delete SSH.NET PackageReference from 5 projects with 0 Renci.SshNet usage — keep only CLI (deployer). [Core/Core.csproj:11 | Services/Azure/Azure.csproj:12 | Audio/Audio.csproj:6 | Google/Google.csproj:9 | LastFm/LastFm.csproj:6]
L19: delete Serilog.Sinks.Console PackageVersion dead — 0 references. [Directory.Packages.props:19]
L69: delete DashboardService singleton registration — all methods static, 0 DI consumers. Remove registration or make instance. [Services/Google/GoogleSetup.cs:69]
L59: delete TranslateCommand --from option registered but ignored in service call. Wire or remove. [CLI/Azure/TranslateCommand.cs:59]
L28: delete Text.Has + Text.StartsWith 0 callers. Delete. [Core/Text.cs:28]
L272: delete SacdProbeRunner.FindSaracon PATH scan dupe of ProcessRunner.IsOnPath. Reuse IsOnPath. [Services/Audio/SacdProbeRunner.cs:272 | ProcessRunner.cs:333]
L210: delete SacdProbeRunner inline d2p args dupe of SaraconService.BuildD2pArgs. Call builder. [Services/Audio/SacdProbeRunner.cs:210 | SaraconService.cs:69]
L116: shrink 4× FRM8/DSD chunk walkers (ProbeDsdAsync + EstimateExpectedPcmBytes + DffMetadataStripper.Scan + RealDffFixture) same FRM8/PROP/DSD padding. Single DffHeaderReader helper. [Services/Audio/DsdConvertService.cs:50 | SaraconService.cs:304 | DffMetadataStripper.cs:138]
L78: shrink ProcessRunner 361 LOC — completionPattern/inactivityTimeout/KilledAfterCompletionMarker serve only saracon 100% case. Keep timeout+Cancellation, drop grace-kill half. ~-120. [Services/Audio/ProcessRunner.cs:9,78]
L85: shrink duplicate catch(InvalidOperation) + catch(OperationCanceledException) identical 6-line bodies. Single catch(Exception ex) when (ex is InvalidOperationException or OperationCanceledException). [App/Program.cs:85]
L44: shrink DsdConvertCommand 133-line probe→tmp→gain→convert→split→tag workflow in CLI. One service call returning presentation result. [CLI/Audio/DsdConvertCommand.cs:44]
L37: shrink SacdConvertCommand result reporting + format validation in CLI. Move into PipelineOrchestrator. [CLI/Audio/SacdConvertCommand.cs:37]
L21: shrink 84 LOC repeated Azure Result.Match ×7 + 60 LOC AsyncCommand<T> boilerplate ×12. One generic Result→exitCode helper. [CLI/Azure/TranslateCommand.cs:22 et al]
L21: shrink DashboardGenerateCommand + SyncYoutubeCommand.RegenerateDashboardAsync duplicated generation flow. Single DashboardService.GenerateAndDeployAsync reused. [CLI/Dashboard/DashboardGenerateCommand.cs:21 | Sync/YouTube/SyncYoutubeCommand.cs:65]
L12: shrink Path.Combine(RepoRoot,"state","youtube") ×6 + manifest path ×5. Use PathResolver.GetStatePath + constant. [Services/Google/YouTube/DashboardService.cs:9 etc]
L31: shrink 10 Serilog sub-loggers via Filter.ByIncludingOnly (8 files 0 B, 50 MB each, retainedFileCountLimit:null unbounded). One app.jsonl + jq filter, or keep 10 with rollingInterval:Day, retainedFileCountLimit:7. [Core/Telemetry.cs:31]
L21: shrink TextAnalytics 5× identical 5120-char validation + 5× StartActivity+catch boilerplate (~95 LOC). Centralize guard + one operation runner. [Services/Azure/TextAnalyticsService.cs:21]
L7: shrink DashboardDataBuilder+HtmlGenerator 364+144 LOC living in CLI. Move to Services.Google as DashboardOrchestrator — CLI becomes thin args→call. [CLI/Dashboard/DashboardHtmlGenerator.cs:3 + DataBuilder.cs:7]
L45: shrink 5 one-line Telemetry wrappers Info/Warn/Debug/Verbose/Error over Log.Write. One structured entry or call Serilog direct. [Core/Telemetry.cs:73]
L7: shrink Text.SanitizeFileName Aggregate+StringBuilder+Contains per char. Simple foreach + HashSet lookup. [Core/Text.cs:7]
L23: shrink EventListener private MapLevel() wrappers that add nothing. Call EventLevelMapper.Map() direct. [Services/Azure/AzureSdkEventListener.cs:21 + ClientModelEventListener.cs:31]
L26: yagni 4 command modules each 1 method/1 caller (Azure/Audio/Dashboard/Sync). Inline into Program.cs until branch grows. [CLI/Azure/AzureCommandModule.cs:5 etc]
L12: yagni YouTubeChangeDetector 62 LOC — 1 pure function, 1 caller. Inline into Orchestrator unless tested separately. [Services/Google/YouTube/YouTubeChangeDetector.cs:12]
L45: yagni CombineNewAndChanged 1 expr, 1 caller. Inline. [Services/Google/YouTube/YouTubePlaylistOrchestrator.cs:375]
L45: yagni ProcessResult.ShouldBreak — always Break on error then immediate exit. Return ErrorOr<ProcessResult> or propagate. [Services/Google/YouTube/YouTubeSyncProcessor.cs:45]
L3: yagni SacdProbeService 15 LOC pure delegation Runner.RunAllAsync, 1 call site. Delete wrapper. [Services/Audio/SacdProbeService.cs:3]
L29: yagni PathResolver.GetStatePath trivial Path.Combine wrapper used by 2 callers — inline or keep only if 3rd state appears. [Core/PathResolver.cs:29]
L17: yagni ServiceNameMethods single switch extension with 1 consumer (Telemetry). Inline in Telemetry until 2nd consumer. [Core/ServiceName.cs:17]
L15: yagni AddAzureServices returns IServiceCollection vs Audio/LastFm void — no caller chains. Normalize to void. [Services/Azure/AzureSetup.cs:15 | Google/GoogleSetup.cs:54]
L5: yagni OciConfig in Core (infra in shared) + hardcoded tailnet IP in Core. Move to CLI/Dashboard env OCI_HOST. [Core/OciConfig.cs:5]
L88: yagni SerilogTracing + Spectre sink for single StartActivity call. stdlib ActivitySource or delete if --debug only. [Core/Telemetry.cs:88 | App/Program.cs:60]
L100: yagni FlacCompletenessChecker statics GetFlacsByTrackNumber+FindDffDir used only by DiscOutputInspector. Fold into inspector. [Services/Audio/FlacCompletenessChecker.cs:100,116]
L46: stdlib DotNetEnv single Env.Load(".env") — 10-line File.ReadLines→SetEnvironmentVariable with #/quotes handling. Drop dep. [App/Program.cs:46 | Directory.Packages.props:12]
L23: stdlib Text.IsEqualTo/IsEqualToIgnore/Has wrappers over string.Equals/Contains. Call string.Equals directly. [Core/Text.cs:23]
L259: stdlib SpeechService ffmpeg arg quoting via interpolated Arguments — use ProcessStartInfo.ArgumentList. [Services/Azure/SpeechService.cs:259]
L129: stdlib DashboardDataBuilder.Escape hand-rolls 5 replaces — WebUtility.HtmlEncode + "'→&#39;". [CLI/Dashboard/DashboardDataBuilder.cs:129]
L91: stdlib Telemetry.IsSeqReachableAsync manual TCP probe before Seq sink — configure sink, let Serilog handle unreachable. [Core/Telemetry.cs:91]
L39: stdlib YouTubeDuplicateMergePolicy Contains linear scan in hot loop — HashSet<string> for O(1). [Services/Google/YouTube/YouTubeDuplicateMergePolicy.cs:39]
Over-engineering vs SRP vs Separation of Concern
Bucket	Verdict	Examples	Why not cut
True over-engineering — cut	Delete/shrink	422 LOC probe harness in prod, 13 dead factories, duplicate archive/FRM8/PATH, 5 dead result fields, 84 LOC repeated Match, 10 sinks for 8 empty files	Dead, duplicated, or speculative — no second consumer, no caller, no behavior.
Legit SRP — keep split	Keep	PipelineOrchestrator 474 vs DsdConvertService 425; Saracon 338 / Sox 142 / SacdExtract 121 each with distinct arg+parse; YouTubeSortService 421 LIS quota optimization; DuplicateMergePolicy 65 policy vs merger side effects; TextAnalytics 5 ops distinct per Azure API	Merging creates 900 LOC monsters or collapses distinct external contracts. One responsibility per class holds. Agents that flagged these as yagni were wrong — flagged file count, not cohesion.
Correct SoC, wrong layer — move not delete	Relocate	Dashboard DataBuilder+HtmlGenerator 508 LOC in CLI; OciConfig in Core; DsdConvert workflow in CLI	Separation correct (IO vs pure transform vs deploy), location violates CLI/AGENTS.md:24 "no service logic in CLI" and Core/AGENTS.md:24 "Core never owns infra". Fix = move file, not merge.
Apparent over-engineering that is adapter necessity — keep	Keep	3 EventListeners (Azure-Core vs System.ClientModel vs Speech EventLogger), 3 SDKs with incompatible event APIs; Services.Google→Azure direct dep for Translate	One generic listener would still contain 3 adapters behind a façade — indirection without reduction. Interface for Translate speculative until 2nd provider.
Gray — keep until 2nd consumer proves need	Lean keep	YouTubeChangeDetector single caller, LastFm ApiClient 220+Service 196 for one endpoint, 4 command modules, FlacChecker statics	YAGNI says inline; SRP says testable unit. Cost of file < benefit of isolated tests. Decision: inline only if file stays <60 LOC and untested.
How agents fit: 5/7 agents correctly identified dead/duplicated code; 2 flagged legitimate SRP splits as yagni by counting files/caller count without checking cohesion or external contract boundaries. Clarified above: file-per-responsibility is not over-engineering when the responsibilities are distinct binaries, SDK event systems, or quota-critical algorithms. Over-engineering is speculative abstraction, duplication, and dead surface — not separation that makes each file do one thing well.
Net
net: -820 to -960 lines removable via deletions + shrinks, -3 deps (DotNetEnv, Serilog.Sinks.Console, 5× SSH.NET scopes), -6 files.
     Relocations (not counted): ~508 LOC Dashboard view layer CLI→Services, ~13 LOC OciConfig Core→CLI.


     Leanest ship order: P0 422 LOC probe harness → dead fields/factories → SSH scope → Match/validation dedup → sinks retention → layer moves.

     