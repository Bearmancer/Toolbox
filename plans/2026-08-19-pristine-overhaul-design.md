# Pristine Overhaul Design — 2026-08-19

## 1. Context

Toolbox `pristine` port copies `old/pristine.py` selectors verbatim into C# Playwright services. Basic runs chronically fail: empty `catch{}` swallows every Playwright step, no telemetry explains why, `state/auth/pristine/auth.json` contains only Shopify cookies (no `pristinestreaming.com` session), `singleTrack` exists in `PristineOrchestrator`/`PristinePollService` but has no CLI flag, `HttpClient` is leaked via `new()`, login wait is duplicated (180s+180s), and album resolve uses brittle `url.Contains(code)` + `title.Contains(code)` checks. `.editorconfig` rules (pascal fields, `var` only for built-ins, `new()`/`[]`, no `!`) are violated by null-forgiving operators.

Source of truth for behavior: `Pristine Script.md` session ses_fe6b9606cffe, `src/Services/Pristine/*`, `src/Core/Telemetry.cs`, `src/Core/Errors.cs`, `Directory.Build.props`.

## 2. Goals

- Make `pristine download` actually land a single 16-bit FLAC for one PASC (e.g. PASC552), then scale to an explicit list of PASCs.
- Eliminate silent failures: every catch logs, no `!`/`null!`, all optionality via `TryGetProperty`/`is` patterns.
- Align fully with repo style: `.editorconfig` clean, `ErrorOr<T>`, `Telemetry.ForService(ServiceName.Pristine)` scoping, `Errors.Pristine` taxonomy, `ServiceName.Pristine` `pristine.jsonl` at `state/logs/pristine.jsonl`.
- Sequential album download, at most 5 concurrent FLAC fetches, forced 16-bit; probe available audio options before downloading.

## 3. Non-Goals

- Parallel album download. GUI automation. Re-implementing `pristine.py` streaming logic outside Playwright (API reverse-engineer is future work). Changing `state/pristine/out` layout beyond sanitized `AlbumTitle/NN. Title.flac`.

## 4. Success Criteria

- `dotnet build` 0 warnings 0 errors; `dotnet format` clean.
- `dotnet run --project src/App -- pristine download PASC552 --single -H` creates one file at `PRISTINE_BASE_OUT_DIR/<SanitizedAlbum>/01. <SanitizedTitle>.flac` (ffprobe: 16-bit, 44100 Hz), `pristine.jsonl` contains `Poll.Start`, `Poll.Resolved`, `Poll.Candidate`, `Poll.Selected16`, `Dl.Success`.
- `dotnet run --project src/App -- pristine download PASC552 PASC553 --headless` downloads both albums sequentially (album 2 starts only after album 1 finishes), with at most 5 `Downloader.DownloadAsync` in-flight.
- Corrupt `auth.json` does not hang: `Warn` + actionable error. Invalid PASC returns `Pristine.ResolveFailed` with probed candidates listed.
- No `!` operator remains in `src/Services/Pristine/**` (grep gate); no `catch{}` without `Telemetry`.

## 5. Architecture

```
CLI: PristineCommandModule -> PristineDownloadCommand
        | variadic [codes...], -o|--out-dir, -H|--headless, -1|--single
        v
Orchestrator.DownloadAsync(codes?, outDir?, headless, singleTrack)
        | dest = outDir ?? PristineCredentials.Read().BaseOutDir
        | using ForService(Pristine)
        | browser.CreateAsync(headless)
        | seed Goto browse + WaitForLogin (180s) + seed Close
        |owns HttpClient via IHttpClientFactory (disposed)
        v (sequential foreach code in effective)
PollService.DownloadSingleAlbumAsync(ctx, code, dest, http, singleTrack)
        +-- AlbumService.ResolveAlbumIdAsync(page, code)  -> int? (ErrorOr)
        +-- AlbumService.StartPlaybackAsync + probe 16-bit candidates
        +-- SemaphoreSlim(5) track downloads via Downloader.DownloadAsync
        +-- AlbumService.ParseTracklist + DownloadArtworkAndPdf
Browser.CreateAsync  -> LaunchPersistentContext msedge @ LocalAppData/pristine-playwright-profile
        + StorageState load from PristinePaths.AuthPath = state/auth/pristine/auth.json (parse errors logged)
Paths: PristinePaths.AuthPath, UserDataDir
Text: PristineText.SanitizePathComponent / NormalizeTrackTitle / IsAudioUrl
Errors: Errors.Pristine.*  Telemetry: Core.Telemetry
```

Dependency direction: `CLI -> Core, Services.Pristine`; `Pristine` services depend only on `Core` + `Microsoft.Playwright` + `System.Net.Http`.

## 6. Components

### 6.1 PristineBrowser
- `LaunchPersistentContextAsync` with `Channel="msedge"`, `Headless=headless`, `AcceptDownloads=true`, `Args=["--autoplay-policy=no-user-gesture-required"]`.
- Auth load: `File.Exists(AuthPath)` -> `ReadAllTextAsync` -> `JsonDocument.Parse`. On `JsonException`/`IOException`: `Telemetry.Warn("Pristine.Browser.AuthParseFailed: {Error}", ex.Message)` and continue unauthenticated (do not throw, do not swallow silently). Cookies mapped via `TryGetProperty` for `name/value/domain/path/expires/httpOnly/secure/sameSite`; invalid entries skipped with `Debug`. `origins/localStorage` via `AddInitScriptAsync` with same guarding.
- No `!` on `GetString()`; use `is string s ? s : ""` and `GetValueOrDefault`.

### 6.2 PristineAlbumService
- `ResolveAlbumIdAsync(IPage, code, ct): Task<ErrorOr<int?>>` (change from `int?` to `ErrorOr` so callers get reason).
- Search loop 3 attempts: each Playwright call (`ClickAsync`, `EvaluateAsync`, `FillAsync`, `WaitForLoadStateAsync`, `GotoAsync`, `QuerySelectorAsync`, `ClickAsync sel`, `WaitForLoadState`, `page.Url`, title eval) wrapped in `try/catch (Exception ex)` with `Telemetry.Debug("Pristine.Album.StepFailed attempt={Attempt} step={Step}: {Error}", attempt, step, ex.Message)`.
- URL gate: log actual `searchUrl` at `Debug`; if not containing `code` nor `code[4..]`, `Debug("Pristine.Album.UrlMismatch url={Url}", searchUrl)` then `GotoAsync(PristineApp)` and continue.
- Album link selectors tried in order `["[href*='/albums/']",".pp-browse-grid__item",".pp-search-results__item"]`; first hit clicked with 5s timeout; `TimeoutException` -> `Debug` and try next selector.
- After nav, `page.Url` checked for `/albums/`; `last.Split('/')[^1]` parsed; `int.TryParse` failure -> `Warn("Pristine.Album.IdParseFailed url={Url} token={Token}", currentUrl, last)` and `return Error.Failure`.
- Title gate: `title` fetched; if empty `Debug`; comparison is `title.Contains(code, OrdinalIgnoreCase)` OR `page.Url.Contains(code)` as fallback; on mismatch `Debug` and treat as not-found for that attempt (not early break).
- `StartPlaybackAsync`: toggle seekbar, wait `.pp-tracklist__item` 15s (Timeout -> Warn), hover+click playnow 5s, fallback JS click/dblclick, wait `body > audio[src]` 5s — all logged. `ParseTracklistAsync`: returns `ErrorOr<List<string>>` (empty list is valid, logged `Warn` upstream). `DownloadArtworkAndPdfAsync`: artwork `src` logged, `Downloader` result checked, PDF delete on failure logged.

### 6.3 PristinePollService
- `DownloadSingleAlbumAsync(ctx, code, outDir, http, singleTrack, ct): Task<PristineAlbumResult>` — keeps return type but internal errors become `Telemetry.Error` + early `PristineAlbumResult` with `Downloaded=0`; resolve failure uses `ErrorOr` from AlbumService to log specific reason.
- Preamble: `GotoAsync(browse)` -> `WaitForLoginAsync` (180s) — single wait; orchestrator seed wait is removed (poll service owns login check). Log `GotoBrowseFailed` as `Error`.
- `albumService.ResolveAlbumIdAsync` result is `ErrorOr`; on error `Warn("Pristine.Poll.ResolveError code={Code}: {Error}", code, err)` and return.
- `GotoAsync(/albums/{id})` + `WaitForSelectorAsync(.pp-album-view__title, 30s)` — Timeout `Warn`, other `Debug`.
- Title sanitized via `PristineText`, `albumOut` created, `Debug` path; `ParseTracklistAsync` expected count `Info`; artwork `try/catch` `Warn`.
- **16-bit probing (new, mandatory before any download):** after `StartPlaybackAsync`, attach `page.Request` handler for `IsAudioUrl`, wait 4000ms capturing. Then `EvaluateAsync` to find quality selector (`[data-quality]`, `.pp-quality`, `button:has-text("16")`, `select`) — if found, click 16-bit option, log `Info("Pristine.Poll.QualitySelected ...")`, wait 2000ms and re-capture. Score candidates: `candidate.Contains("16", OrdinalIgnoreCase) || candidate.Contains("44100")` and `EndsWith(.flac)` = tier 0; plain `.flac` = tier 1; `.mp3` = tier 2. Log each `Debug("Pristine.Poll.Candidate tier={Tier} url={Url}", tier, id)`. Select tier 0 if any else fail with `Warn("Pristine.Poll.No16Bit code={Code} candidates={Count}", capturedUrls.Count)` and return `AlbumNotFound` with `Description` listing candidates. In `singleTrack` mode selected set is first tier-0 URL only.
- Stall loop: `MaxStall=60`, `PollMs=1000`, `PostDlWaitMs=2000`. Poll `capturedUrls` unseen -> `body > audio[src]` paused/ready checks -> `pp-playbar__now-playing__track` title -> sanitize/normalize -> dest `NN. Safe.flac` if 16-bit else skip with `Warn`. `downloader.DownloadAsync` awaited via `SemaphoreSlim(5)` when full album; `--single` bypasses semaphore (1 download, then break). `Telemetry.Info` for `Track`, `DownloadOk/Failed`, `SingleTrackDone`, `AllExpectedDone`; `Debug` for `Src id`, `ClickedForward`, `WaitReady/Playing` timeouts, `Stall n/60`, `ReadyPausedRetryingPlay`, `Stall5RetryPlaynow`. `stall>=MaxStall` -> `Warn(StallLimitReached)`.

### 6.4 PristineDownloader
- `DownloadAsync(url, dest, http, ct): Task<bool>` — unchanged signature. Internals: `part = dest + ".part"`, 3 attempts `2*(1<<(attempt-1))s` backoff, `HttpCompletionOption.ResponseHeadersRead`, `IsSuccessStatusCode` else 5xx retry else `false`. `File.Create(part)` -> `CopyToAsync` -> `FlushAsync` -> `File.Move(part,dest,true)`. All branches `Telemetry.Debug/Warn/Info/Error` with truncated url `url[..Math.Min(80,url.Length)]` and `Path.GetFileName(dest)`. `OperationCanceledException` rethrow after `Debug`. `.part` cleanup `Debug` on failure. Keep `Telemetry.ForService` scope.

### 6.5 PristineOrchestrator
- `DownloadAsync(string[]? codes, string? outDir, bool headless=false, bool singleTrack=false, ct)` — signature adds `singleTrack` plumbed from CLI.
- `dest` via `PristineCredentials.Read().BaseOutDir` else `MissingBaseOutDir` `Validation` error; `Directory.CreateDirectory(dest)`.
- `effective = codes is {Length:>0} ? codes : Releases` (still 48 default when no args — preserved).
- `using ForService(Pristine)`; `browser.CreateAsync(headless, ct)` -> `BrowserFailed` on exception.
- Seed page: `NewPageAsync` -> `GotoAsync(browse, DOMContentLoaded)` `Warn` on fail -> `WaitForLoginAsync(seed,180)` (single place; PollService also waits on its own page — orchestrator seed wait stays as auth gate, poll wait is per-album re-check) -> `Info(LoginCheck)` -> `CloseAsync` `Debug` on fail. If not logged in `LoginTimeout`. `File.Exists(AuthPath)` else `AuthMissing` (error text updated to `state/auth/pristine/auth.json`).
- `HttpClient http = httpClientFactory.CreateClient()` via `IHttpClientFactory` (registered `AddHttpClient()`; no `new HttpClient()` leak) — disposed via `using`.
- Sequential `foreach (var code in effective)`: `ct.ThrowIfCancellationRequested()`, `await pollService.DownloadSingleAlbumAsync(ctx, code, dest, http, singleTrack, ct)` with `catch OperationCanceledException throw`, `catch Exception -> Error AlbumFailed + result Title="error"`. `await Task.Delay(3000, ct)` between albums (rate kindness). Return `List<PristineAlbumResult>`.
- Remove duplicated login helper duplication where possible — keep one `WaitForLoginAsync` in Orchestrator (seed) and one in PollService (per-album page); both log identically.

### 6.6 PristineDownloadCommand
- `Settings: [CommandArgument(0,"[codes]")] string[] Codes {get;init;}=[], [CommandOption("-o|--out-dir")] string? OutDir, [CommandOption("-H|--headless")] bool Headless, [CommandOption("-1|--single")] bool Single`.
- `ExecuteAsync`: if `Headless` set env `PRISTINE_HEADLESS=1`; normalize codes: split each `Codes` entry on `[,;\s]+`, trim, upper-case, filter `IsNullOrWhiteSpace`, dedup preserve order. `codes.Length>0 ? codes : null` passed as `codes` (so Releases default preserved when no args). `await orchestrator.DownloadAsync(codes, settings.OutDir, settings.Headless, settings.Single, ct)` -> `Match` prints `Code Title Downloaded/Expected -> OutPath` green, error red.

### 6.7 Paths / Credentials / Text / Errors / ServiceName
- `PristinePaths.AuthPath = Path.Combine(RepoRoot,"state","auth","pristine","auth.json")` (already migrated). `UserDataDir` unchanged.
- `Errors.Pristine.AuthMissing` message updated to `state/auth/pristine/auth.json`.
- No new `ServiceName`; `Pristine` already registered.

## 7. Data Flow — Single FLAC Then List

1. User: `pristine download PASC552 --single -H` -> CLI normalizes `["PASC552"]`.
2. Orchestrator resolves `dest`, creates `IBrowserContext`, seed goto+login, HttpClient from factory.
3. PollService for PASC552: goto browse (login re-check), resolve id (3 attempts logged), goto `/albums/{id}`, read title, mkdir, parse tracklist, artwork/pdf, start playback, probe candidates 4s + quality click + re-probe, select tier-0 FLAC URL, semaphore(5) -> download 1 file to `NN. Safe.flac` via Downloader (.part atomic), break on `singleTrack`, return `PristineAlbumResult{Downloaded=1}`.
4. Orchestrator collects result, delay 3s, no next code (single mode done), return list.
5. For `PASC552 PASC553` or `PASC552,553`: same loop sequentially — PASC553 page created only after PASC552 result recorded; 5-FLAC cap applies *within* each album (tier-0 URLs queued through semaphore).

## 8. Concurrency

- Albums: strictly sequential `await` in `foreach` — no `Task.WhenAll`, no channel. Guarantees Playwright page isolation and log order.
- Tracks: `SemaphoreSlim(5)` inside `PollService` for the download phase only. Implementation: `List<Task<bool>> inFlight`; for each selected `src`/`dest`, `await semaphore.WaitAsync(ct)` then `Task.Run(() => downloader.DownloadAsync(...))` with `finally semaphore.Release()`. When `singleTrack` true, skip semaphore and do single `await downloader.DownloadAsync`.
- Playwright `page.EvaluateAsync`/`WaitForFunctionAsync` remain single-threaded on the page.

## 9. 16-bit Guarantees

- Candidate URLs come from `page.Request` (`IsAudioUrl`) plus `body > audio[src]` fallback; each candidate logged.
- Scoring prefers explicit 16-bit indicators; if none found, no download — caller gets `Warn` + `AlbumNotFound` with candidate list rather than wrong bit depth.
- Downloader does not transcode; it fetches the probed URL as-is. 16-bit guarantee is by selection, not conversion.

## 10. Error Handling & Logging

- Every `catch` logs: `Telemetry.Debug` for expected/retryable (selector miss, nav timeout, eval miss), `Telemetry.Warn` for recoverable (resolve fail, empty tracklist, stall), `Telemetry.Error` for terminal (goto browse failed, playback failed, all download attempts failed).
- Method entries use `using var _ = Telemetry.ForService(ServiceName.Pristine)` or `Telemetry.Info("Pristine.X.Start ...")`.
- `ErrorOr` used for `ResolveAlbumIdAsync` and probe step; `DownloadSingleAlbumAsync` keeps `PristineAlbumResult` but logs `Error` before returning zero-download result.
- `.part` files cleaned on every failure path; `File.Move` overwrite true.
- Auth parse: `Warn` with truncated error (no cookie values logged).
- `HttpClient` via factory; disposed with `using`.

## 11. Code Style

- `.editorconfig` gates: `var` only for built-in types (`var x = "a"` ok, `string x = ...` otherwise), `new()` target-typed, `[]` collection expressions, primary constructors, `is null`/`is not null`, `is Type t`, `??`/`?.`, switch expressions, no braces where optional, expression-bodied one-liners, `using var` where applicable, file-scoped namespaces, `CA2016` forward `CancellationToken`, no `!`.
- One class per file, `private static readonly` inline constants at top, no `Constants.cs`/`Helpers.cs`, PascalCase fields, camelCase locals/params.
- Build verify every edit: `dotnet build` clean.

## 12. Verification

- `dotnet build` + `dotnet format --verify-no-changes` clean.
- Dry run (no network if site down): `pristine download PASC552 --single -H` exits with logged `ResolveFailed` + candidate list, no unhandled exception, `pristine.jsonl` has stall/candidate logs.
- Live run (requires `state/auth/pristine/auth.json` from `pristine login` and `PRISTINE_BASE_OUT_DIR`): single FLAC appears and `ffprobe -show_streams` reports `bits_per_sample=16` and `sample_rate=44100`. Then list `PASC552 PASC553` completes sequentially with at most 5 concurrent `.part` files observable.
- Existing `dotnet build` still 0 warnings; no new `Directory.Build.targets`.

## 13. Risks

- Site quality selector may not expose 16-bit explicitly — fallback is URL scoring; if neither, we fail loud with candidate dump rather than silent wrong-depth download.
- Persistent profile `pristine-playwright-profile` may retain stale storage; browser `Warn` on parse failure mitigates.
- Shopify `account.pristineclassical.com` cookies alone are insufficient for `pristinestreaming.com` — `pristine login` remains required; `dbcrust` extraction from `cookies.sqlite` is fallback only.

## 14. Implementation Order

1. PristineBrowser logging + null-safety
2. PristineAlbumService rewrite (all catches logged, `ErrorOr`, 16-bit-aware)
3. PristinePollService 16-bit probe + semaphore(5) + stall logging
4. PristineOrchestrator HttpClientFactory + singleTrack plumbing
5. PristineDownloadCommand variadic + --single + code normalization
6. Errors message + build gates + live single-FLAC proof
