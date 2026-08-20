# Plans

`Claude/` is the authoritative, current plan set — one file per concern, each re-verified directly
against source rather than inherited from prior passes. Start there.

| File | Concern | Status |
|---|---|---|
| [Claude/00-repo-hygiene.md](Claude/00-repo-hygiene.md) | Repo/plan-corpus hygiene | read this first |
| [Claude/01-audio.md](Claude/01-audio.md) | Audio / SACD pipeline | active |
| [Claude/02-lastfm.md](Claude/02-lastfm.md) | Last.fm sync/scrobble | active — must be redone |
| [Claude/03-azure.md](Claude/03-azure.md) | Azure services (Vision/Translate/DocIntel/Speech/TextAnalytics/OpenAI) | active |
| [Claude/04-core.md](Claude/04-core.md) | Core cross-cutting (Telemetry, Errors, dead code) | active |
| [Claude/05-pristine.md](Claude/05-pristine.md) | Pristine (PASC downloader) | active |

Everything else that used to live in `plans/` was raw source material for the six files above and has
been deleted once its content was distilled in. What remains at the root, alongside this README, is
kept deliberately — evidence, not active plans:

- `erroror_migration_assessment.md`, `ponytail_audit_verified.md` — prior audits, cross-checked and
  kept as a "don't re-propose this" record.
- `content.md`, `removals.json`, `removals_analysis.csv`, `removals_analysis.txt`,
  `youtube_search_results.json` — raw evidence backing past decisions, not forward-looking plans.
- `taste.md` — synced from `~/.commandcode/taste/taste.md` (canonical source); working-style
  preferences, not project plans.

When a `Claude/*.md` file's own scope closes out, fold its outcome into the relevant `AGENTS.md` and
delete the plan file rather than letting it linger here.
