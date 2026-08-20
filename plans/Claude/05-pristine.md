---
concern: Pristine (PASC downloader)
status: complete — direct-API download + auto 16-bit transcode shipped and live-verified; browser automation kept as fallback only
ref: github.com/Bearmancer/Toolbox @ master (git log for exact history)
source_docs: superseded — see git history for the original audit and fix commits
---

# Pristine — Plan

## Current state

`pristine download <codes>` resolves each album, downloads every track's FLAC, and
auto-transcodes it to 16-bit before keeping it — all via Pristine's own JSON API
(`/api/v1/authenticate`, `/api/v1/search`, `/api/v1/albums/{id}?with[]=tracks`,
`/api/v1/listen/{trackId}`), discovered and verified live against the real site and
the user's paid account. No browser is opened on the success path.

**Pipeline:**

1. `PristineApiClient` — authenticates via the session cookies already in
   `state/auth/pristine/auth.json`, then calls search/album/listen endpoints directly
   over `HttpClient`. No Playwright involved.
2. `PristineApiPollService` — resolves an album, fans out its tracks (5 concurrent,
   matching the original design's cap), downloads each via `PristineDownloader`, and
   verifies each via `PristineAudioVerifier` (ffprobe: must be FLAC, 24-bit — that's
   what Pristine actually serves; a genuine 16-bit source was never observed).
3. `Services.Audio.FlacTranscodeService` (new, Pristine-agnostic) — downsamples the
   verified 24-bit FLAC to 16-bit via SoX (`rate -v -L <rate> dither -s`), re-verifies
   the output, and replaces the original. Runs as its own step, composed by
   `PristineOrchestrator` after each album finishes — not fused into the download/verify
   internals, so it's reusable standalone via `audio transcode <directory>`.
4. **Fallback**: if the API path errors (cookie expired, unexpected response shape,
   network failure), that album — and only that album — drops to the original
   Playwright browser automation (`PristineBrowser`/`PristineAlbumService`/
   `PristinePollService`), which is still fully live and was itself fixed this session
   (stale `.pp-*` selectors from an old site redesign, cookie `Path`/`Url` conflict,
   `int`→`long` album-ID overflow, race between navigation and the site's own
   `/api/v1/authenticate` call). The browser is only ever launched lazily, on first
   fallback, and shared across the rest of the batch.

**Output layout:** `Desktop/Pristine/PASC<code> - <Album Title>/<track>.flac` — folder
prefixed with the code (search results already suffix it, so the trailing `- PASC###`
is stripped before prefixing to avoid duplication).

**CLI:** `pristine download PASC552 PASC553` or `pristine download "PASC552,PASC553"`
— space, comma, or semicolon separated, already handled by `NormalizeCodes`. Malformed
codes (not `letters+digits`) are rejected per-code with a clear message; if every code
in a batch is malformed the command exits 1 rather than silently falling back to the
default catalogue. Concurrency is capped at 5 total, not per-album, because albums are
processed strictly sequentially (matches the original P9 acceptance criterion).

## What's proven live (this session, against the real account)

- Full album download + verify + transcode: PASC552, 12/12 tracks, all confirmed
  16-bit/44.1kHz FLAC on disk via independent ffprobe check after the run.
- Multi-code batch (comma- and space-separated), genuinely distinct albums, not one
  code resolving repeatedly: all 13 Stokowski codes plus PAKM059 (non-PASC prefix) —
  14 different titles, all resolved and downloaded correctly.
- 24-bit→16-bit transcode at two different source sample rates (44.1kHz and 48kHz),
  confirming the SoX `rate` pass-through generalizes rather than assuming 44.1kHz.
- The one real "no tracks" case found (PASC575) confirmed against the live site UI
  itself (not just the API) — a genuine catalogue gap (never digitized for streaming),
  correctly surfaced as `Expected=0`, not a bug.

## Deliberately not done

- **P4 (diagnostics contract / retry isolation / resolver)** and **P6 (Azure/runtime
  preflight)** from the original hardening plan: no recoverable spec ever existed for
  these: P6's source text was literally "redacted," and P4's themes never mapped to a
  concrete defect. Not reinstated.
- **Full-album fallback proof** (≥2 albums forcing the browser path deliberately): the
  browser path itself was proven correct via direct single-album live runs earlier this
  session; deliberately breaking auth to force the fallback trigger wasn't done since it
  would mean invalidating working session cookies against the real account for a test
  that doesn't change any code path already verified independently.
