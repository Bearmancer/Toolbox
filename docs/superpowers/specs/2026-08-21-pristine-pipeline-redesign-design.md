# Pristine album pipeline redesign

**Date:** 2026-08-21
**Scope:** `src/Services/Pristine/PristineApiPollService.cs`, `PristineOrchestrator.cs`,
`PristineAudioVerifier.cs`, `PristineVerification.cs`, `PristineModels.cs`, and
`src/Services/Audio/FlacTranscodeService.cs`. The browser-fallback path
(`PristinePollService.cs`) is explicitly **out of scope** — see Non-goals.

## Motivation

This session's debugging work (see git log around 2026-08-21) found and fixed several
bugs in the existing per-track resume/transcode pipeline: a title-formatting change that
silently orphaned already-downloaded files, a verify gate that permanently rejected
legitimate 16-bit source tracks, and a silent early-return that skipped transcoding with
zero log output. Rather than keep patching the existing per-track-resume-then-directory-scan-
transcode shape incrementally, this redesigns the album pipeline around an explicit,
upfront per-album decision, with the live source format known before any download starts.

## Scope note: API path only

This redesign applies to `PristineApiPollService` (the direct-API download path, which
handles the large majority of albums). The browser-fallback path (`PristinePollService`)
is structurally unable to pre-probe a track's URL — it doesn't know a track's URL until
that URL is captured live during in-browser playback, after playback has already started.
The browser path keeps today's already-hardened per-track resume logic unchanged.

## Per-album pipeline

```
1. Resolve PASC code via the API (GetAlbumTracksAsync — always a live call, every
   branch, regardless of local state; this also gives current expected track count,
   titles, positions).
   → code doesn't resolve: Error-log immediately with the code and reason. Album
     recorded as Failed. Batch continues to the next code — never aborts the run.

2. Print "Downloading: <Album Title>" (album-level line, unindented).

3. Check LOCAL state first — no network beyond step 1's API call:
   For each expected track, does a local file already exist, and does it already
   satisfy the fixed target invariant (16-bit AND rate ∈ {44100, 48000})?
   This is self-contained: target form never depends on comparing against the live
   source, only on the local file's own current bits/rate (see "Why local-state-first
   is sound" below).

   - Local FLAC count == expected count AND every local file already matches the
     target invariant
       → e3: DONE. Zero further network calls for this album (no live-probe, no
         download). Log it, move to the next album.
   - Otherwise → e1/e2 (unified, see below).

4. e1/e2 (some tracks missing locally, or present but not yet target-form):
   a. Live-probe track 1's URL directly via ffprobe (remote URL, zero local file
      involved) to establish {Codec, Bits, SampleRate} for any track this run needs
      to FETCH. Existing local-but-wrong-format tracks use their own local probe
      instead (see above) — the live probe is only for tracks not yet on disk.
      → probe fails (network/ffprobe error): Error-log, album recorded as Failed,
        batch continues.
   b. Compute the album's download target from that probe (applies to every newly
      fetched track — see "Target matrix" below).
   c. Download every missing track (concurrency capped at 5). This is a NEW outer
      retry loop, distinct from `PristineDownloader`'s existing inner 3x HTTP-retry
      (which handles transient network failures — 5xx, dropped connections — during
      a single download attempt). The new outer loop wraps the whole
      download-then-verify cycle: if the file downloads successfully at the HTTP
      level but fails ffprobe verify (corrupt/truncated content), the entire
      download-then-verify cycle repeats from scratch, up to 3 total attempts. Still
      corrupt after 3 → loud-skip that track only (Error-log, named in the final
      summary), rest of the album's downloads continue.
   d. Wait for EVERY track's download phase to fully resolve (success, or exhausted
      retries) before starting ANY transcoding. No partial-download + partial-
      transcode state is ever reached.
   e. Transcode every track (existing-but-wrong-format + freshly downloaded) that
      isn't yet target-form. Sequential, one file at a time. A track whose transcode
      produces a broken result (our own sox/verify/move failure) retries the entire
      transcode attempt (re-run sox from the original file, re-verify) up to 3 total
      attempts; still broken after 3 → loud-skip that track's transcode only
      (Error-log, named in the final summary, track stays at its downloaded/original
      bit depth), rest of the album's transcodes continue.
```

### Why local-state-first is sound

"Target form" is a fixed, self-describing invariant (16-bit AND rate ∈ {44100, 48000})
that is a pure function of a file's _own_ current bits/rate — it never needs to be
compared against what the live source currently reports. A local file already at
16-bit/44100 or 16-bit/48000 is final-form by definition, regardless of what the
original source was. A local file at 24-bit or above-floor-rate already tells you,
from its own probe, exactly what it needs to become. The live-probe-not-local-file
instruction matters specifically for a track not yet on disk (deciding what a fresh
download's target will be, printing the `Source:` line before the download
completes) — for a track already present, we have strictly more information locally
(the actual bytes) than a remote probe would give.

Known limitation (pre-existing, not introduced by this redesign): if Pristine
re-issues a track at different quality after we've already downloaded it, nothing in
this system detects that (no version/etag tracking) — this is an existing assumption
of the whole resume model, unchanged here.

## Target matrix

| Source bits | Source rate | Transcode?      | Target bits | Target rate |
| ----------- | ----------- | --------------- | ----------- | ----------- |
| 16          | 44100       | No              | 16          | 44100       |
| 16          | 48000       | No              | 16          | 48000       |
| 16          | 88200       | Yes (rate only) | 16          | 44100       |
| 16          | 96000       | Yes (rate only) | 16          | 48000       |
| 16          | 176400      | Yes (rate only) | 16          | 44100       |
| 16          | 192000      | Yes (rate only) | 16          | 48000       |
| 24          | 44100       | Yes (bits only) | 16          | 44100       |
| 24          | 48000       | Yes (bits only) | 16          | 48000       |
| 24          | 88200       | Yes (bits+rate) | 16          | 44100       |
| 24          | 96000       | Yes (bits+rate) | 16          | 48000       |
| 24          | 176400      | Yes (bits+rate) | 16          | 44100       |
| 24          | 192000      | Yes (bits+rate) | 16          | 48000       |
| MP3         | any         | No              | —           | —           |

Rule: bit depth folds to 16 only when source is 24-bit (no other bit depths expected
from Pristine; 32-bit explicitly out of scope). Rate independently folds toward the
44.1/48kHz floor by repeated halving whenever it's above 48000, regardless of bit
depth. MP3 sources are never transcoded, at any rate.

This is a generalization of today's `FlacTranscodeService.ResolveTargetSampleRate` —
same halving logic, now also applied when bit depth is already 16 (today it only
fires when bits > 16).

## Concurrency

- Downloads: capped at 5 concurrent, per album (unchanged from today's
  `MaxConcurrent`). A corrupt-track retry happens inline within that track's own
  slot, not a second slot.
- Transcode: sequential, one file at a time (unchanged — sox is CPU-bound; running 5
  sox processes concurrently would thrash the machine for no gain). Never starts
  until the album's entire download phase (including all retries) has settled.

## Logging ownership (no duplicate transcode lines)

- `FlacTranscodeService` owns every user-facing Info line about transcoding: the
  `Transcoding: <file>` start line and the per-file result line. Nobody else prints
  these.
- `PristineOrchestrator` only logs a Debug-level one-line aggregate per album — never
  re-prints per-file info.
- Per-track lines (`Downloading: <track>`, `— kept`, `— rejected`, `— already
  present, skipping`) are indented two spaces, matching today's convention. The
  album-level `Downloading: <Album Title>` line is unindented, so album vs. track
  lines are visually distinguishable without extra wording.

## Error / retry / throw matrix

| Failure                                                              | Scope                | Treatment                                                                                                                                                                                                                     |
| -------------------------------------------------------------------- | -------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| PASC code doesn't resolve                                            | Album                | Error-log, album = Failed, batch continues                                                                                                                                                                                    |
| Live probe fails for track 1                                         | Album (pre-download) | Error-log, album = Failed, batch continues                                                                                                                                                                                    |
| Track HTTP download fails outright                                   | Track                | Existing `PristineDownloader` 3x retry w/ backoff; still failing → track = failed, rest of album continues                                                                                                                    |
| Track downloads but corrupt (fails ffprobe verify)                   | Track                | New outer retry loop (distinct from `PristineDownloader`'s inner HTTP-retry): repeat download-then-verify cycle, 3 total attempts; still corrupt → loud-skip that track only, named in final summary, rest of album continues |
| Track's transcode output is broken (our own sox/verify/move failure) | Track                | Repeat the whole transcode-then-verify cycle, 3 total attempts; still broken → loud-skip that track's transcode only, named in final summary, track stays at downloaded bit depth, rest of album's transcodes continue        |

No failure type silently skips an album or a track without an Error/Warn-level log
naming what happened. Nothing propagates as an unhandled exception that would abort
the whole batch — every failure category above is caught and reported at the
narrowest scope that makes sense (track, then album), never wider.

## Final batch summary

Printed once, after all albums in the run have been attempted. Three buckets:

- **Success** (N albums): every track fully resolved to target form. Listed as codes
  only.
- **Partial** (M albums): album completed but one or more tracks permanently failed
  after retries. Listed as code + which specific track(s) + why (download-corrupt-
  exhausted vs. transcode-broken-exhausted).
- **Failed** (K albums): never got started — code didn't resolve, or the live probe
  failed before any download began. Listed as code + reason.

## Non-goals

- Browser-fallback path (`PristinePollService`) is unchanged by this redesign.
- 32-bit source files are out of scope (not observed from Pristine; no handling
  defined).
- No change to how albums are discovered/ordered in a batch run, or to the
  `--out`/CLI surface.
