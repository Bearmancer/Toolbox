---
slug: youtube-playlist-export
status: approved
intent: clear
pending-action: <none - rewriting plan>
approach: Background sync orchestrator (not CLI export tool). Mirrors old YouTubePlaylistOrchestrator pattern. Polls YT API daily, detects changes via sync.json snapshots, writes raw API dumps + enriched per-playlist JSON. File-only (no PostgreSQL). Resume on interrupt. Translation included from start.
---

# Draft: youtube-playlist-export

## Components (topology ledger)
| id | outcome | status | evidence path |
|----|---------|--------|---------------|
| C1 | Google Auth configured (GOOGLE_CLIENT_ID + GOOGLE_CLIENT_SECRET placeholders) | active | .env |
| C2 | state/youtube/ directory scaffold (raw/, enriched/, deleted/) | active | Old state/youtube/ |
| C3 | YouTubeVideo DTO + YouTubeFetchState model (matching old schema) | active | Old csharp/src/Models/YouTube.cs |
| C4 | YoutubeService extension: full-parts fetch, Videos.list duration, quota tracking | active | src/Services/Google/YoutubeService.cs |
| C5 | YouTubeChangeDetector: compare API snapshots vs stored state | active | Old csharp/src/Services/Sync/YouTube/YouTubeChangeDetector.cs |
| C6 | YouTubePlaylistOrchestrator: discover → detect → fetch → translate → persist | active | Old csharp/src/Orchestrators/YouTubePlaylistOrchestrator.cs |
| C7 | CLI: sync yt command invoking orchestrator | active | Old csharp/src/Program.cs (sync yt branch) |
| C8 | Translation via Azure TranslateService | active | src/Services/Azure/TranslateService.cs |

## Decisions (all resolved)
| Decision | Choice | Rationale |
|----------|--------|-----------|
| Architecture | Background sync orchestrator, not CLI export tool | Matches old pattern; daily polling with change detection |
| State root | root/state/youtube/ | Matches old implementation |
| Directories | raw/, enriched/, deleted/ + sync.json | raw = API dumps, enriched = processed data, deleted = archived |
| Raw format | Full paginated API response per page | Max fidelity |
| Enriched fields | Title, Description, Duration (HH:mm:ss), ChannelName, VideoId, ChannelId, TranslatedTitle, TranslatedDescription | Matches old YouTubeVideo DTO |
| Database | File-only, no PostgreSQL | User's explicit choice |
| Translation | Included from start via Azure Translator | Matches old YouTubeTranslationService |
| Resume | Support resume on interrupt | Loads cached files, skips fetched video IDs |
| Duration | HH:mm:ss | Standard format, user's choice |
| Empty playlists | Write [] | Consistent output |
| Error recovery | Wait for internet; stop if unavailable | User's explicit choice |
| Polling | Daily (timer / external scheduler) | User described "background daily task" |

## Scope IN
- `GOOGLE_CLIENT_ID` + `GOOGLE_CLIENT_SECRET` placeholders in .env
- `state/youtube/sync.json` — lightweight index (PlaylistSnapshots map + sync metadata)
- `state/youtube/raw/` — full paginated API response per page per playlist
- `state/youtube/enriched/` — per-playlist JSON with 8 fields + translation
- `state/youtube/deleted/` — archived playlists preserved on removal
- `YouTubeVideo` DTO matching old schema (Title, Description, Duration, ChannelName, VideoId, ChannelId, TranslatedTitle, TranslatedDescription, DisplayTitle, DisplayDescription, NeedsTranslation)
- `YouTubeFetchState` model (PlaylistSnapshots: Dict<string, Snapshot>, LastChecked, LastUpdated, FetchComplete)
- `YouTubeChangeDetector` — compares API state vs stored snapshots
- `YouTubePlaylistOrchestrator` — full sync pipeline with resume support
- Translation via existing `TranslateService` for non-English titles/descriptions
- `sync yt` CLI command invoking orchestrator
- Integration test with real API against known playlist

## Scope OUT (Must NOT have)
- No PostgreSQL database (file-only)
- No CLI export commands (export-playlists, export-items, export-clean — dropped)
- No Last.fm or other service integration
- No YouTube Studio comparison (API re-fetch only)
- No spreadsheet ID tracking (old SpreadsheetId field dropped)
- No soft-delete DB logic
- No IAsyncEnumerable or streaming patterns
