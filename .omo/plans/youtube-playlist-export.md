# youtube-playlist-export - Work Plan

## TL;DR (For humans)
<!-- Fill this LAST, after the detailed plan below is written, so it summarizes the REAL plan. -->

**What you'll get:** A background YouTube playlist sync system that polls daily, detects changes, backs up everything to `state/youtube/` — raw API dumps, per-playlist video JSONs with translated titles/descriptions, a manifest/index (`sync.json`) for change detection, and preserved deleted playlists. File-only, no database, resumable if interrupted.

**Why this approach:** Mirrors the old `YouTubePlaylistOrchestrator` pattern that already works. Uses a lightweight `sync.json` manifest/index for change detection so only changed playlists get re-fetched (quota-efficient). Raw layer preserves API fidelity; playlists layer gives you the key video data with translation.

**What it will NOT do:** No PostgreSQL, no CLI export subcommands, no Last.fm or spreadsheet integration. Not a one-shot export — it's a stateful sync system. No YouTube Studio comparison.

**Effort:** Medium
**Risk:** Medium — depends on Google OAuth credentials and YouTube Data API v3 quota (10,000 units/day)
**Decisions to sanity-check:** File-only (no DB), translation from the start, resume-on-interrupt, raw = full paginated API response per page

Your next move: approve, or run a high-accuracy review. Full execution detail follows below.

---

> TL;DR (machine): Medium effort, Medium risk. Background sync orchestrator writing to state/youtube/{raw,playlists,deleted}/ + sync.json manifest. Change-detected, translated, resumable. Batch-optimized sort. 8 todos across 4 waves + AGENTS.md.

### Raw vs playlists output (with example)

The system has two output layers for each video in a playlist. Here is a single video shown in both formats:

**`raw/{playlistId}_p1.json`** — Full paginated API response, verbatim:
```json
{
  "kind": "youtube#playlistItemListResponse",
  "etag": "\"5n7m6c4xTMMhJfm2q9d93OjFul4\"",
  "nextPageToken": "CAUQAA",
  "pageInfo": { "totalResults": 5, "resultsPerPage": 5 },
  "items": [{
    "kind": "youtube#playlistItem",
    "etag": "\"abc123\"",
    "id": "UEw...2Ux",
    "snippet": {
      "publishedAt": "2024-01-15T08:30:00Z",
      "channelId": "UC...",
      "title": "Beethoven - Symphony No.5 | Kubelik BRSO live",
      "description": "Live recording from Munich, 1971...",
      "thumbnails": { "default": {"url": "...", "width": 120, "height": 90}, "medium": {...}, "high": {...} },
      "channelTitle": "The Just Sound",
      "videoOwnerChannelTitle": "The Just Sound",
      "videoOwnerChannelId": "UCDc...UiQ",
      "playlistId": "PL1z...Go",
      "position": 0,
      "resourceId": { "kind": "youtube#video", "videoId": "CZHnlrb1dZc" }
    },
    "contentDetails": {
      "videoId": "CZHnlrb1dZc",
      "videoPublishedAt": "2023-06-20T00:00:00Z",
      "note": ""
    },
    "status": { "privacyStatus": "public" }
  }]
}
```
→ Contains: API plumbing (kind, etag, nextPageToken, pageInfo), thumbnails, resourceId polymorphism, status, every byte the API returned. Best for debugging and audit trails.

**`playlists/{SanitizedTitle}.json`** — One file per playlist, flat array of key fields only:
```json
[{
  "Title": "Beethoven - Symphony No.5 | Kubelik BRSO live",
  "Description": "Live recording from Munich, 1971...",
  "Duration": "00:33:37",
  "ChannelName": "The Just Sound",
  "VideoId": "CZHnlrb1dZc",
  "ChannelId": "UCDcxRVRSMoKFxdMQIVDLUiQ",
  "TranslatedTitle": "Beethoven - Symphony No. 5 | Kubelik BRSO live",
  "TranslatedDescription": "Live recording from Munich, 1971..."
}]
```
→ Only the 8 fields you care about. No API noise. TranslatedTitle/TranslatedDescription populated when original is non-English (mirrors old `YouTubeVideo` DTO with `DisplayTitle`/`DisplayDescription` computed properties). Same format as the old `state/youtube/playlists/` files.

### Manifest: `sync.json`

`state/youtube/sync.json` is the single source of truth for what playlists exist and their current state. Schema:
```json
{
  "PlaylistSnapshots": {
    "PL1zgNCoWt_7bGHz1ITg7oDKPJiW3lXhGo": {
       "PlaylistId": "PL1zgNCoWt_7bGHz1ITg7oDKPJiW3lXhGo",
       "Title": "Alain Altinoglu",
       "LastUpdated": "2026-04-19T01:22:16.0885104Z",
       "ETag": "\"5n7m6c4xTMMhJfm2q9d93OjFul4\"",
       "ReportedVideoCount": 73
    }
  },
  "LastChecked": "2026-06-24T08:00:00Z",
  "LastUpdated": "2026-06-24T08:00:05Z",
  "FetchComplete": true
}
```
→ Used for change detection: compares ETag + ReportedVideoCount against API to decide if a playlist needs re-fetching. VideoIds are NOT stored here (snapshots are lightweight summaries) — they're loaded from playlist files on-demand during resume.

## Scope
### Must have
- `state/youtube/` directory scaffold: `raw/`, `playlists/`, `deleted/`
- `state/youtube/sync.json` — manifest/index (see schema above)
- `state/youtube/raw/{playlistId}_p{N}.json` — full paginated PlaylistItemListResponse per page, all parts (snippet, contentDetails, status, etc.)
- `state/youtube/playlists/{SanitizedTitle}.json` — JSON array of YouTubeVideo objects: `{ Title, Description, Duration (HH:mm:ss), ChannelName, VideoId, ChannelId, TranslatedTitle, TranslatedDescription }`
- `state/youtube/deleted/{SanitizedTitle}.json` — archived playlists when removed from YouTube, same playlists schema
- `YouTubeVideo` DTO matching old implementation: Title, Description, Duration (TimeSpan), ChannelName, VideoId, ChannelId, TranslatedTitle?, TranslatedDescription?, DetectedLanguage?, TranslatedAt?, with computed DisplayTitle, DisplayDescription, NeedsTranslation, FormattedDuration
- `YouTubeFetchState` model: PlaylistSnapshots dictionary, sync metadata (LastChecked, LastUpdated, FetchComplete)
- `YouTubeChangeDetector` — compares API playlist summaries vs stored PlaylistSnapshots to find new/changed/deleted playlists
- `YouTubePlaylistOrchestrator` — full sync pipeline: discover → change-detect → fetch raw → enrich → translate → persist, with resume-on-interrupt
- `YouTubeTranslationService` — translates non-English titles/descriptions using existing `TranslateService`
- `sync yt` CLI command — invokes the orchestrator, registered in `CLI/Google/GoogleCommandModule.cs` or new sync branch
- Quota tracking: cumulative units logged per run

### Must NOT have (guardrails, anti-slop, scope boundaries)
- No PostgreSQL database or EF Core — file-only
- No `export-playlists`, `export-items`, `export-clean` CLI commands — dropped
- No spreadsheet ID tracking (old `SpreadsheetId` field — dropped)
- No Last.fm or other service integration
- No IAsyncEnumerable or streaming patterns
- No configurable state path (hardcoded `root/state/youtube/`)
- No video search, channel, or subscription API operations beyond playlist sync

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: manual QA via `sync yt --verbose` against real API + file content verification (no xUnit or test NuGet packages)
- Evidence: `.omo/evidence/task-{N}-youtube-playlist-export.{json,txt}`

## Execution strategy
### Parallel execution waves
- **Wave 1** (no deps): T1 (verbose flag + path constants + sanitizer), T2 (DTOs + state model)
- **Wave 2** (depends on T1, T2): T3 (YoutubeService extension + batch sort optimization), T4 (ChangeDetector)
- **Wave 3** (depends on T3, T4): T5 (Orchestrator + TranslationService)
- **Wave 4** (depends on T5): T6 (CLI entry point), T7 (manual verification), T8 (AGENTS.md — independent, can run anytime)

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| T1 | — | T3 | T2, T8 |
| T2 | — | T3, T4 | T1, T8 |
| T3 | T1, T2 | T5 | T4, T8 |
| T4 | T2 | T5 | T3, T8 |
| T5 | T3, T4 | T6, T7 | T8 |
| T6 | T5 | — | T7, T8 |
| T7 | T5 | — | T6, T8 |
| T8 | — | — | T1-T7 |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->

- [ ] 1. Create state path utility and wire --verbose flag
  What to do:
  - Add `--verbose` flag handling in `Program.cs`: alongside the existing `--debug` flag, add `--verbose` that also calls `Telemetry.Configure(debug: true)`. Both flags activate debug-level Serilog logging so the orchestrator's per-playlist progress is visible. Add `IsDebugEnabled` static property to `Telemetry` class.
  - Add path constants at top of `src/Services/Google/YoutubeService.cs` (or new orchestrator file): `private static readonly string StateRoot = Path.Combine(AppContext.BaseDirectory, "state", "youtube");` and `private static readonly string SyncFile = Path.Combine(StateRoot, "sync.json");`. Subdirectories: `Path.Combine(StateRoot, "raw")`, `Path.Combine(StateRoot, "playlists")`, `Path.Combine(StateRoot, "deleted")`. Create directories on first write. Matches existing codebase pattern of keeping paths as file-level constants.
  - Create `src/Services/Google/FileNameSanitizer.cs`: static method `string Sanitize(string title)` — replaces `< > : " / \ | ? *` with `_`, truncates to 200 chars, returns `"untitled"` for empty/null input.
  - Both classes use `System.Text.Json` (implicit via net10.0) for serialization.
  - NOTE: GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET are set globally as system environment variables — no `.env` changes needed.
  Must NOT: No configurable root path. No third-party JSON library. No .env modifications.
  Parallelization: Wave 1 | Blocked by: — | Blocks: T3, T4
  References: Old `src/Data/State/StateManager.cs` (paths pattern), `src/App/Program.cs:20` (.env loading — same `AppContext.BaseDirectory` approach), `src/Core/Telemetry.cs` (Configure method), `Directory.Build.props:6` (ImplicitUsings enable)
  Acceptance criteria: `SyncFile` constant resolves to `<AppBaseDir>/state/youtube/sync.json`. `Sanitize("A/B:C*D?")` returns `"A_B_C_D_"`. `--verbose` flag activates debug logging (verify via `Telemetry.IsDebugEnabled`).
  QA scenarios: Happy — FileNameSanitizer handles empty string → `"untitled"`. Failure — path construction with null BaseDirectory throws ArgumentNullException (unreachable on .NET). Evidence: `.omo/evidence/task-1-youtube-playlist-export.txt`
  Commit: Y | `feat(google): add state path utility and verbose logging flag`

- [ ] 2. Create YouTubeVideo DTO and YouTubeFetchState model
  What to do: Create `src/Services/Google/Models/YouTubeVideo.cs` and `src/Services/Google/Models/YouTubeFetchState.cs`:
  - `YouTubeVideo` (record): `Title` (string), `Description` (string), `Duration` (TimeSpan), `ChannelName` (string), `VideoId` (string), `ChannelId` (string), `TranslatedTitle` (string?, init), `TranslatedDescription` (string?, init), `DetectedLanguage` (string?, init), `TranslatedAt` (DateTimeOffset?, init). Computed: `DisplayTitle` → `TranslatedTitle ?? Title`, `DisplayDescription` → `TranslatedDescription ?? Description`, `NeedsTranslation` → `DetectedLanguage is not null and DetectedLanguage != "en" and TranslatedTitle is null`, `FormattedDuration` → `$"{Duration.Hours:D2}:{Duration.Minutes:D2}:{Duration.Seconds:D2}"`, `VideoUrl` → `$"https://www.youtube.com/watch?v={VideoId}"`, `ChannelUrl` → `$"https://www.youtube.com/channel/{ChannelId}"`. Factory: `static YouTubeVideo FromPlaylistItem(PlaylistItem item, TimeSpan? duration)` — maps snippet + contentDetails fields.
  - `PlaylistSnapshot` (record): `PlaylistId` (string), `Title` (string), `LastUpdated` (DateTimeOffset), `ETag` (string), `ReportedVideoCount` (long). NOTE: VideoIds is intentionally NOT stored here — snapshots are lightweight summaries fetched without item-level API calls. VideoIds are loaded on-demand from playlist files when needed for resume.
  - `YouTubeFetchState` (record): `PlaylistSnapshots` (Dictionary<string, PlaylistSnapshot>), `LastChecked` (DateTimeOffset?), `LastUpdated` (DateTimeOffset?), `FetchComplete` (bool). Static methods: `LoadAsync(string path, CancellationToken ct)` — reads `sync.json` deserialized with System.Text.Json, returns empty state if file doesn't exist. `SaveAsync(string path, YouTubeFetchState state, CancellationToken ct)` — writes indented JSON, creates directory.
  Must NOT: No EF Core annotations. No database mapping. No mutable properties on records (all init/readonly).
  Parallelization: Wave 1 | Blocked by: — | Blocks: T3, T4
  References: Old `src/Models/YouTube.cs` (YouTubeVideo, PlaylistSnapshot, YouTubeFetchState), Old `src/Models/StateTransitions.cs` (LoadAsync/SaveAsync pattern), `Directory.Build.props:7` (net10.0 supports records + init)
  Acceptance criteria: `YouTubeVideo.FromPlaylistItem(item, TimeSpan.FromSeconds(212))` produces object with FormattedDuration `"00:03:32"`, VideoUrl `"https://.../watch?v=..."`. `YouTubeFetchState.LoadAsync` on non-existent file returns state with empty PlaylistSnapshots. Save → Load round-trips identical.
  QA scenarios: Happy — Save and load round-trip preserves all fields. Failure — Load from corrupted JSON throws JsonException (acceptable — caller handles). Evidence: `.omo/evidence/task-2-youtube-playlist-export.json`
  Commit: Y | `feat(google): add YouTubeVideo DTO and YouTubeFetchState model`

- [ ] 3. Extend YoutubeService with full-parts fetch, duration fetch, and quota tracking
  What to do: Modify `src/Services/Google/YoutubeService.cs`:
  - Add field `int QuotaUsed { get; private set; }` — increments on every API call.
  - Refactor `GetPlaylistsAsync` to accept `string parts = "snippet"` parameter (default preserves existing behavior). Export calls use `"snippet,contentDetails,status"`.
  - Refactor `GetPlaylistItemsAsync` to accept `string parts = "snippet"` parameter. Export calls use `"snippet,contentDetails,status"`.
  - Refactor `SortPlaylistAlphaAsync` to use Google API batch requests for large playlists: instead of N sequential `UpdateItemPositionAsync` calls (N × 50 quota units + N × HTTP roundtrips), collect all position updates into a single `Google.Apis.Requests.BatchRequest`. Call `youtubeService.HttpClient.ExecuteAsync(batchRequest)` once. This keeps the same quota cost (N × 50) but reduces latency from N roundtrips to 1. For a 200-video playlist, saves 199 roundtrips (~30s → ~1s). Implementation: `var batch = new BatchRequest(youtubeService.HttpClient); foreach update { batch.Queue<PlaylistItem>(request, callback); } await batch.ExecuteAsync(ct);`
  - Add `GetPlaylistItemPagesRawAsync(string playlistId, string parts, CancellationToken ct)` → `IReadOnlyList<PlaylistItemListResponse>` — returns ALL paginated response objects (not just items), each page as a full response. Used by raw dump.
  - Add `GetVideoDurationsAsync(IReadOnlyList<string> videoIds, CancellationToken ct)` → `Dictionary<string, TimeSpan?>` — batches 50 IDs per `Videos.list` call (`part="contentDetails"`), parses ISO 8601 duration (`PT#H#M#S`), returns null for live/unknown. Returns empty dict for empty input.
  - Add `GetPlaylistIdsAsync(string playlistId, CancellationToken ct)` → `IReadOnlyList<string>` — fetches just the video IDs for a single playlist using `part="id"` (returns minimal data, 1 quota unit). Used by resume logic to compare against cached files. No snippet/contentDetails — just IDs.
  - Add `GetPlaylistSummariesAsync(CancellationToken ct)` → `IReadOnlyList<PlaylistSnapshot>` — fetches all user playlists using `part="snippet,contentDetails"` (1 unit per 50 playlists). Returns snapshot list with PlaylistId, Title, LastUpdated (from snippet.publishedAt), ETag (from etag), ReportedVideoCount (from contentDetails.itemCount). Does NOT fetch VideoIds — snapshots are lightweight. Accumulates quota.
  Must NOT: Don't break existing `SortPlaylistAlphaAsync` or `UpdateItemPositionAsync`. Don't add retry logic. Don't use IAsyncEnumerable.
  Parallelization: Wave 2 | Blocked by: T1, T2 | Blocks: T5
  References: `src/Services/Google/YoutubeService.cs:12-47` (existing methods), librarian: Videos.list API (contentDetails.duration = ISO 8601), Old `src/Services/Sync/YouTube/YouTubeService.cs` (stub pattern)
  Acceptance criteria: `GetPlaylistSummariesAsync()` returns list with all fields populated. `GetVideoDurationsAsync(["dQw4w9WgXcQ"])` returns `{ "dQw4w9WgXcQ": TimeSpan(0,3,32) }`. `GetPlaylistItemPagesRawAsync(id, "snippet,contentDetails", ct)` returns list of PlaylistItemListResponse with all pages. QuotaUsed increments by 1 per API call.
  QA scenarios: Happy — empty input to GetVideoDurationsAsync returns empty dict, no API call. Failure — invalid video ID returns null entry, does not throw. Evidence: `.omo/evidence/task-3-youtube-playlist-export.txt`
  Commit: Y | `feat(google): extend YoutubeService with full-parts fetch, duration, and quota tracking`

- [ ] 4. Create YouTubeChangeDetector
  What to do: Create `src/Services/Google/YouTubeChangeDetector.cs`:
  - Static class with single method: `DetectChanges(IReadOnlyList<PlaylistSnapshot> current, YouTubeFetchState stored) → (IReadOnlyList<PlaylistSnapshot> NewPlaylists, IReadOnlyList<PlaylistSnapshot> ChangedPlaylists, IReadOnlyList<PlaylistSnapshot> DeletedPlaylists, IReadOnlyList<PlaylistSnapshot> UnchangedPlaylists)`.
  - Logic: Build dictionary from current list keyed by PlaylistId. Compare against stored.PlaylistSnapshots:
    - **New**: in current but not stored → needs full fetch + raw dump
    - **Changed**: in both, but ETag differs OR ReportedVideoCount differs → needs re-fetch (video IDs changed)
    - **Deleted**: in stored but not current → move playlists file to deleted/, remove from sync.json snapshots
    - **Unchanged**: in both, same ETag and same ReportedVideoCount → skip fetch (but record in new snapshots with updated title if title changed)
  - Log detected changes using Telemetry: counts of new/changed/deleted/unchanged.
  Must NOT: Don't fetch video IDs during detection (summaries only — save quota). Don't modify YouTubeFetchState (caller does).
  Parallelization: Wave 2 | Blocked by: T2 | Blocks: T5 | Can parallelize with: T3
  References: Old `src/Services/Sync/YouTube/YouTubeChangeDetector.cs`, T2 (PlaylistSnapshot, YouTubeFetchState models), T3 (GetPlaylistSummariesAsync)
  Acceptance criteria: Empty stored state → all playlists marked New. Same ETag + same count → Unchanged. Different ETag → Changed. Stored playlist not in current → Deleted.
  QA scenarios: Happy — 3 current, 2 stored (1 match, 1 changed, 1 only in stored) → 1 new, 1 changed, 1 deleted, 1 unchanged. Failure — null stored state treated as empty. Evidence: `.omo/evidence/task-4-youtube-playlist-export.txt`
  Commit: Y | `feat(google): add YouTubeChangeDetector for playlist change detection`

- [ ] 5. Create YouTubePlaylistOrchestrator and YouTubeTranslationService
  What to do: Create `src/Services/Google/YouTubePlaylistOrchestrator.cs` and `src/Services/Google/YouTubeTranslationService.cs`:
  
  **YouTubeTranslationService** (class, takes `TranslateService` via primary constructor):
  - `TranslateVideosAsync(List<YouTubeVideo> videos, CancellationToken ct)` → `List<YouTubeVideo>` — for each video where `NeedsTranslation` is true: calls `TranslateService.TranslateAsync(video.Title, "en", ct: ct)` and `TranslateService.TranslateAsync(video.Description, "en", ct: ct)`. Strips any `"xx -> en:"` prefix from the response via `response[(response.LastIndexOf(":") + 1)..].Trim()`. Returns new YouTubeVideo records with TranslatedTitle, TranslatedDescription, DetectedLanguage (from response or language detection), TranslatedAt set. Videos already English are returned unchanged. On failure: log warning via Telemetry, leave translation fields null, continue.

  **YouTubePlaylistOrchestrator** (class, takes `YoutubeService`, `YouTubeTranslationService`):
  - `ExecuteAsync(CancellationToken ct)` → Task:
    1. Load `YouTubeFetchState` from `StatePaths.SyncFile` (or create empty)
    2. Call `youtubeService.GetPlaylistSummariesAsync(ct)` for current API state
    3. Call `YouTubeChangeDetector.DetectChanges(current, stored)` to get 4 buckets
    4. Log change counts via `Telemetry.Info("Sync: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged")`
    5. For each **Deleted** playlist: move `playlists/{title}.json` → `deleted/{title}.json`, remove from snapshots
    6. For each **New** or **Changed** playlist:
       - **Raw**: call `GetPlaylistItemPagesRawAsync(playlistId, "snippet,contentDetails,status", ct)`, write each page to `raw/{playlistId}_p{N}.json`
       - **Enriched**: call `GetPlaylistItemsAsync(playlistId, "snippet,contentDetails", ct)`, collect video IDs, call `GetVideoDurationsAsync(videoIds, ct)`, map to `YouTubeVideo.FromPlaylistItem()`
       - **Translate**: call `translationService.TranslateVideosAsync(videos, ct)`
       - **Persist**: write videos to `playlists/{sanitizedTitle}.json` using `FileNameSanitizer.Sanitize(title)`
    7. Update snapshots: all current playlists (including Unchanged) recorded in new `YouTubeFetchState` with updated titles/ETags/counts/timestamps
    8. Save `YouTubeFetchState` to `StatePaths.SyncFile`
    9. Log quota used via `Telemetry.Info("Quota used this run: {QuotaUsed} units")`
  - **Resume support**: Before fetching items for a Changed playlist, check if `playlists/{sanitizedTitle}.json` already exists AND the playlist is marked Changed (not New). If yes: load existing videos from the file, call `youtubeService.GetPlaylistIdsAsync(playlistId, ct)` to get current video ID list (1 quota unit), diff the two lists to find new/removed videos, only fetch durations and full data for new videos, keep existing video data for unchanged IDs. For New playlists: always full fetch (no resume check).
  - **Verbose logging**: When `Telemetry.IsDebugEnabled` (set via `--verbose` flag or `LOG_LEVEL=Debug` env var): log per-playlist progress ("Fetching raw for {title}...", "Translating {count} videos...", "Resume: {existing} cached, {new} new"). Quiet otherwise — only summary log line at end.
  Must NOT: Don't fetch full video data for Unchanged playlists. Don't delete playlists files without moving them (never erase). Don't fail on translation errors (log warning via Telemetry, leave Translated fields null, continue). No dry-run mode.
  Parallelization: Wave 3 | Blocked by: T3, T4 | Blocks: T6, T7
  References: Old `src/Orchestrators/YouTubePlaylistOrchestrator.cs:40-120` (ExecuteAsync flow), Old `src/Services/Sync/YouTube/YouTubeTranslationService.cs`, `src/Services/Azure/TranslateService.cs` (existing translate — method is `TranslateAsync(string text, string toLang, CancellationToken ct)`, returns string with `"xx -> en:"` prefix), `src/Core/Telemetry.cs` (IsDebugEnabled, Info, Warning), T2 (StatePaths, Sanitizer), T3 (YoutubeService — new GetPlaylistIdsAsync), T4 (ChangeDetector)
  Acceptance criteria: `ExecuteAsync(ct)` creates raw/, playlists/, deleted/ files. Deleted playlist moved from playlists/ to deleted/. sync.json updated with new snapshots. `Telemetry.Info` logs quota and change counts.
  QA scenarios: Happy — first run (empty state): all playlists treated as new, raw + playlists files created, log shows "Sync: 5 new, 0 changed, 0 deleted, 0 unchanged". Second run (no changes): nothing fetched, sync.json timestamps updated, log shows "Sync: 0 new, 0 changed, 0 deleted, 5 unchanged". Resume: create partial playlists file (only 3 of 5 videos), re-run → calls GetPlaylistIdsAsync (1 unit), detects 2 new videos, fetches only those, no duplicate raw pages. Failure — translation API unavailable: logs warning via Telemetry, leaves Translated fields null, continues. Evidence: `.omo/evidence/task-5-youtube-playlist-export.json`
  Commit: Y | `feat(google): add YouTubePlaylistOrchestrator with change detection, translation, and resume`

- [ ] 6. Add sync yt CLI command
  What to do: Create `src/CLI/Google/YouTube/SyncYouTubeCommand.cs`:
  - `SyncYouTubeCommand(YouTubePlaylistOrchestrator orchestrator)` — primary constructor, uses `YouTubePlaylistOrchestrator` directly (not YoutubeService).
  - `ExecuteAsync` — calls `orchestrator.ExecuteAsync(ct)`. Orchestrator handles all logging internally via Telemetry.
  - `Settings`: `--verbose` (bool, default false) — when set, calls `Telemetry.Configure(debug: true)` before execution so the orchestrator's per-playlist progress logs are emitted. Quiet mode otherwise (summary only).
  Register in `GoogleCommandModule.ConfigureCommands`:
  ```csharp
  // Spectre.Console.Cli 0.55.0 supports nested AddBranch
  cfg.AddBranch("google", b => {
      b.AddCommand<SortPlaylistCommand>("sort-playlist");
      b.AddBranch("sync", sb => sb.AddCommand<SyncYouTubeCommand>("yt"));
  });
  ```
  Must NOT: Don't wire to `sync` top-level branch (that's for old system). No --dry-run flag (removed).
  Parallelization: Wave 4 | Blocked by: T5 | Blocks: — | Can parallelize with: T7
  References: `src/CLI/Google/GoogleCommandModule.cs:8-9`, `src/CLI/Google/YouTube/SortPlaylistCommand.cs:8-24` (command pattern), `src/App/Program.cs:34` (--debug flag sets Telemetry.Configure debug mode), `src/Core/Telemetry.cs` (Configure method — existing, add IsDebugEnabled property), T5 (YouTubePlaylistOrchestrator)
  Acceptance criteria: `sync yt` runs full sync, creates state/youtube/ files, exits 0. `sync yt --verbose` prints per-playlist progress lines ("Fetching raw for Alain Altinoglu...", "Translating 73 videos..."). `Program.cs` already passes `--debug` to `Telemetry.Configure`; add `--verbose` alongside it (both activate debug logging).
  QA scenarios: Happy — run with--verbose on empty account prints detailed progress, exits 0. Failure — auth error prints clear message to stderr, exits 2. Evidence: `.omo/evidence/task-6-youtube-playlist-export.txt`
  Commit: Y | `feat(cli): add sync yt command invoking YouTubePlaylistOrchestrator`

- [ ] 7. Manual verification — run sync against real YouTube account
  What to do:
  - Build the app: `dotnet build AzureAI.slnx`
  - Run `sync yt --verbose` against real YouTube account (requires GOOGLE_CLIENT_ID/SECRET as global env vars)
  - Verify `state/youtube/sync.json` exists and contains PlaylistSnapshots with at least one playlist
  - Verify `state/youtube/raw/` contains `*_p1.json` files with valid PlaylistItemListResponse structure (check: `kind` = "youtube#playlistItemListResponse")
  - Verify `state/youtube/playlists/` contains `.json` files where each entry has all 8 fields present, Duration matches `\d{2}:\d{2}:\d{2}`, VideoUrl starts with `https://www.youtube.com/watch?v=`
  - Run a second time, verify no duplicate raw files created (idempotent — Unchanged playlists skipped)
  - Delete one `playlists/*.json` file, run again, verify it's recreated with same video count (resume works)
  No test NuGet packages (xUnit, NUnit, MSTest) — purely manual verification with CLI and file inspection.
  Must NOT: Don't create a test project. Don't add any test NuGet package references. No xUnit, NUnit, MSTest, or similar.
  Parallelization: Wave 4 | Blocked by: T5 | Blocks: —
  References: T5 (orchestrator), T6 (CLI command), T1 (state paths)
  Acceptance criteria: All 5 verification steps pass against a real YouTube account. Auth errors print clear message and exit gracefully.
  QA scenarios: Happy — all files created with correct structure on first run. Second run is idempotent. Resume recreates deleted file. Failure — missing credentials prints "GOOGLE_CLIENT_ID environment variable not set" and exits. Evidence: `.omo/evidence/task-7-youtube-playlist-export.txt`
  Commit: N (manual verification — no code commit)

- [ ] 8. Create project AGENTS.md with style preferences and architecture
  What to do: Create `AGENTS.md` at project root capturing coding conventions and architecture for the New project. Extends the global AGENTS.md at `C:\Users\Lance\.config\opencode\AGENTS.md`. Must include: architecture diagram (App→CLI→Services→Core), PascalCase everywhere, file-level constants pattern, primary constructors for DI, records for DTOs, no comments rule, credentials via env vars, state directory layout, quota awareness, batch request optimization for sorting, and the exact Directory.Build.props conventions (net10.0, nullable, ImplicitUsings, TreatWarningsAsErrors).
    Must NOT: Don't contradict global AGENTS.md. No emoji. No fluff paragraphs.
   Parallelization: Wave 4 | Blocked by: — | Blocks: —
   References: Global `C:\Users\Lance\.config\opencode\AGENTS.md`, `Directory.Build.props`, `YoutubeService.cs` (file-level constant pattern), `AzureAI.slnx` (project structure)
   Acceptance criteria: `AGENTS.md` at project root covering architecture, PascalCase, no-comments rule, credentials, state layout, batch optimization.
   QA scenarios: File exists and is valid Markdown. Evidence: `AGENTS.md` (the file itself).
   Commit: Y | `docs: add project AGENTS.md with style and architecture conventions`

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. Plan compliance audit — all 8 todos marked complete, all acceptance criteria verified, all QA evidence on disk
- [ ] F2. Code quality review — no warnings (TreatWarningsAsErrors), no dead code, no commented code, all classes ≤ 300 lines
- [ ] F3. Real manual QA — run `sync yt --verbose` against real YouTube account, verify state/youtube/ directory structure and file contents, confirm batch-optimized sort works for >50 item playlist
- [ ] F4. Scope fidelity — confirm no PostgreSQL, no export commands, no spreadsheet ID, deleted/ preservation works, translation populated for non-English titles

## Commit strategy
- One commit per todo (8 commits)
- Prefix: `feat(google):` for service/models, `feat(cli):` for command, `docs:` for AGENTS.md
- No squashing — each commit independently reviewable and revertable

## Success criteria
1. `dotnet build` passes with zero warnings
2. `sync yt --verbose` prints detailed per-playlist progress and accurate change counts
3. `sync yt` creates `state/youtube/{sync.json, raw/*, playlists/*}` with correct content
4. Second `sync yt` run skips unchanged playlists (idempotent — no duplicate raw files)
5. Deleted playlists moved to `deleted/` directory (never erased)
6. TranslatedTitle and TranslatedDescription populated for non-English videos
7. Duration format: `HH:mm:ss` (e.g., `"00:03:37"`)
8. Resume works: deleting a playlists file and re-running recreates it without re-fetching raw
9. SortPlaylistAlphaAsync uses batch requests for >10 items (1 roundtrip, not N)
10. All 5 manual verification steps pass (or fail gracefully with clear error messages)
11. QuotaUsed logged after each run
12. AGENTS.md exists with style preferences and architecture captured
