# draft: youtube-quota-logging

intent: clear
review_required: false
status: exploring

## evidence gathered

### runtime log (C:\Users\Lance\logs\youtube.jsonl)
- 145 playlists, 1 changed (Georg Solti +1 video)
- sort batch: 20/145 picked (changed-first, then alphabetical by ID)
- 8 playlists repositioned before quota death
- 147 successful writes + 109+ failed 403s on ONE large playlist (positions 78-241+)
- total attempts: ~256 writes x 50 units = 12,800 units > 10,000/day

### code findings
- YouTubeSortService.cs:243 - no early-exit on 403 quota, loops hammering
- YouTubeSortService.cs:28 - maxPasses=3, totalRepositioned += successes (inflates across passes)
- YouTubePlaylistOrchestrator.cs:201 - batch picks 20 by (changed, ID-alpha), no sort-state awareness
- Telemetry.cs:26 - log path is `logs/{slug}.jsonl` (CWD-relative, NOT RepoRoot)
- Program.cs:50-53 - --verbose sets global LevelSwitch, file sink shares same switch

### quota facts (official Google docs)
- playlistItems.update = 50 units per call
- read ops (list) = 1 unit per call
- default daily quota = 10,000 units
- max ~200 writes/day (10,000 / 50)
- no programmatic quota check API
- quota exceeded = HTTP 403, reason: quotaExceeded

## decisions adopted (defaults)
- quota budget per run: 150 writes (7,500 units), leaves headroom for reads
- batch size stays 20, but quota cap is safety net
- file sink: always Debug+ (decoupled from console level)
- log path: PathResolver.RepoRoot + /logs/ (stable, not CWD-relative)
- sort prioritization: changed-first + previously-interrupted-first
- track sort state per playlist (last sort attempt result) to break churn cycle

## status: plan-delivered
plan_path: .omo/plans/youtube-quota-logging.md
