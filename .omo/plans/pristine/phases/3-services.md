# Phase 3: Services (resolve → playback → poll loop)

## Tasks

### Task 15: Create PristineAlbumService.cs — ResolveAlbumId + StartPlayback + ParseTracklist + Artwork/Pdf

Single file with private helpers — mirrors `pristine.py:393-657`:

- `ResolveAlbumIdAsync(IPage page, string code)` — 3 attempts, `.pp-navbar__search__input`, fill+Enter, `WaitForLoadStateAsync(NetworkIdle,5000)`, URL contains `code` or `code[4:]`, click `[href*='/albums/']` → `.pp-browse-grid__item` → `.pp-search-results__item`, parse `int(url.Split('/').Last())`, verify `.pp-album-view__title` case-insensitive contains code. Same swallow+retry via `GotoAsync(PRISTINE_APP)`.

- `StartPlaybackAsync(IPage page)` — seekbar toggle if `value!='1'`, `WaitForSelectorAsync(".pp-tracklist__item",15000)`, `HoverAsync`+`ClickAsync(".pp-tracklist__item__playnow")` fallback JS `MouseEvent('click')`/`dblclick`, `WaitForFunctionAsync("!!document.querySelector('body > audio[src]')",5000)`.

- `ParseTracklistAsync(IPage)` → `EvaluateAsync<string[]>("Array.from(...pp-tracklist__item__title...).map(el=>el.textContent.trim())")`

- `DownloadArtworkAndPdfAsync(IPage, string albumOut, string albumTitle, HttpClient)` — `.pp-album-view__artwork > img` src → `{albumTitle}{ext}` + `S3_COVERS+{nameNoExt}.pdf` → `{nameNoExt}.pdf` via `PristineDownloader`. Missing artwork → early return; PDF fail → log non-fatal.

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineAlbumService resolve/playback/artwork`

---

### Task 16: Create PristinePollService.cs — Poll loop state machine

Port `pristine.py:781-1003` core loop into `DownloadSingleAlbumAsync(IBrowserContext ctx, string code, string outDir, HttpClient http, CancellationToken ct)`:

State: `seenUrls HashSet<string>`, `seenTitles HashSet<string>`, `stallCount int`, `trackNum int`, `capturedUrls List<string>` via `Page.Request` event filtered by `PristineText.IsAudioUrl`.

Loop `while (stallCount < 60)`:

- newSrc = first `capturedUrls` not in `seenUrls` else `EvaluateAsync<string?>("...!el.paused&&el.hasAttribute('src')")`
- if found: `seenUrls.Add`, `trackNum++`, title = `EvaluateAsync("...pp-playbar__now-playing__track...")` ?? `f"Track {trackNum:02d}"`, duplicate-title break, `NormalizeTrackTitle().TitleCase` → `SanitizePathComponent` → `f"{trackNum:02d}. {safe}{ext}"` where `ext=".flac" if ".flac" in src else ".mp3"`, `EvaluateAsync("pause all")` then `PristineDownloader.DownloadAsync`, expected-count break, `Task.Delay(2000)` → `ClickForwardAsync` → `WaitForFunctionAsync("...readyState>=2",4000)` swallow → `ClickPlayAsync` → `WaitForFunctionAsync("...!paused",3000)` swallow.
- else: `stallCount++`, if `HasReadyPausedAudio` → `ClickPlay`, else if `stallCount==5` → JS re-dispatch playnow.

Tail: verification (missingOnDisk quirk — uses final `trackNum` prefix, keep bug-for-bug then fix in follow-up), `Task.Delay(10000)`, finally `Page.CloseAsync()`.

**ponytail: stall 60*1s global, sequential poll — per-track timeout if streaming stalls longer**

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristinePollService`

---

### Task 17: Create PristineOrchestrator.cs

Port `pristine.py:1006-1072`:

- mkdir `dest`, `CreateAsync` browser, seed page `GotoAsync(PRISTINE_APP)` + `_wait_for_login` (180s), require `AuthPath` exists else `Errors.Pristine.AuthMissing`, loop `RELEASES` or passed codes with `Task.Delay(3000)` inter-album, per-album try/catch, `context.CloseAsync` finally, log `ALL DOWNLOADS COMPLETE`.

Use `Telemetry.ForService(ServiceName.Pristine)` scope + `ErrorOr` return.

Keep constants: `S3_COVERS`, `PRISTINE_APP`, `POLL_SLEEP=1.0`, `POST_DL_WAIT=2.0`, `MAX_STALL=60`, `TOSCANINI_BEETHOVEN/GENERAL/STOKOWSKI/RELEASES` as `static readonly string[]` — lazy: inline at top of orchestrator, not separate constants file.

**QA:** `dotnet build`

**Commit:** `feat(pristine): add PristineOrchestrator`

## Verify Phase 3

```bash
dotnet build src/Services/Pristine/Pristine.csproj
```

**Dependencies:** Phase 2
**Blocks:** Phase 4
