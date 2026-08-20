# Plans

`Claude/05-pristine.md` documents the Pristine downloader, now complete: direct-API
download (no browser on the success path) with automatic 24-bit→16-bit transcode,
live-verified against the real account across 14+ distinct albums and two sample rates.
Browser automation is kept only as a per-album fallback.

| File                                           | Concern                    | Status   |
| ---------------------------------------------- | -------------------------- | -------- |
| [Claude/05-pristine.md](Claude/05-pristine.md) | Pristine (PASC downloader) | complete |

`00-repo-hygiene.md`, `01-audio.md`, `02-lastfm.md`, `03-azure.md`, and `04-core.md` closed out and
were deleted per this file's own policy below — their outcomes are folded into the relevant
`AGENTS.md` files, and the reasoning/audit trail lives in git commit history (`git log`), not here.

What remains at the root, alongside this README, is kept deliberately — evidence, not active plans:

- `erroror_migration_assessment.md`, `ponytail_audit_verified.md` — prior audits, cross-checked and
  kept as a "don't re-propose this" record.
- `removals.json`, `removals_analysis.csv`, `removals_analysis.txt`, `youtube_search_results.json` —
  raw evidence backing past decisions, not forward-looking plans.
- `taste.md` — synced from `~/.commandcode/taste/taste.md` (canonical source); working-style
  preferences, not project plans.

When a `Claude/*.md` file's own scope closes out, fold its outcome into the relevant `AGENTS.md` and
delete the plan file rather than letting it linger here.
