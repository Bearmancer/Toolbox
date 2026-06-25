# native-lastfm - Work Plan

## TL;DR (For humans)

**What you'll get:** Last.fm sync that always stores correct UTC timestamps by reading the `uts` field directly from the API, with no timezone conversion bugs. Includes robust error handling for Last.fm's non-standard API behavior, timestamp-based rate limiting, and correct incremental sync via the `from` query parameter.

**Why this approach:** Hqub.Lastfm converts UTC to system local time, which breaks when the system timezone differs from the user's actual timezone. Raw API calls using `uts` field are 100% reliable. Last.fm returns HTTP 200 with errors in JSON body, so we must parse response body for error codes. Timestamp-based rate limiting (not SemaphoreSlim) ensures 200ms spacing between requests. `IHttpClientFactory` prevents socket exhaustion.

**What it will NOT do:** No new NuGet packages. No test frameworks. No fancy abstractions. Just HTTP + JSON.

**Effort:** Short (3-5 hours) — slightly longer due to Last.fm API quirks
**Risk:** Medium - Last.fm API has non-standard error handling (HTTP 200 + error in body)
**Decisions to sanity-check:** Error handling strategy (switch expression for retryable/fatal/permanent), User-Agent header, HttpClient.Timeout, timestamp-based rate limiting

Your next move: answer 5 questions below, then approve. Full execution detail follows below.

---

> TL;DR (machine): Short, Medium risk — replace Hqub.Lastfm with raw HttpClient + System.Text.Json, ~200 LOC total. Must handle Last.fm's non-standard error pattern (HTTP 200 + error in body), single-track vs array response, now-playing tracks, and correctly wire `fetchAfter` to the `from` query parameter. Momus approved plan structure; Metis identified 6 critical API quirks to handle.

## Scope
### Must have
- Replace `LastFmService.cs` with raw HttpClient implementation
- Extract `uts` field directly from API response (no timezone conversion)
- Wire `fetchAfter` parameter to Last.fm `from` query parameter (Unix timestamp string)
- Rate limiting: 200ms between requests using `DateTimeOffset.UtcNow` timestamp tracking (NOT SemaphoreSlim)
- Retry logic: 3 attempts, exponential backoff (1s → 2s → 4s)
- Incremental sync: derive `fetchAfter` from newest scrobble in `scrobbles.json`
- `IHttpClientFactory` pattern in DI registration
- Remove `LastFmFetchState.cs` entirely
- Remove `Hqub.Lastfm` NuGet package from `LastFm.csproj`

### Must NOT have (guardrails, anti-slop, scope boundaries)
- No new NuGet packages
- No test frameworks (xUnit, NUnit, MSTest)
- No fancy abstractions (repositories, services layers)
- No `fetch-state.json` file
- No `DateTime.Now` or local time conversions
- No `SemaphoreSlim` for rate limiting (it controls concurrency, not rate)
- No comments (code is self-documenting)

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: none (solo-dev rules — no test NuGet packages; manual verification via build)
- Evidence: .omo/evidence/task-1-native-lastfm.txt (build output)

## Execution strategy
### Parallel execution waves
> Target 5-8 todos per wave. Fewer than 3 (except the final) means you under-split.

**Wave 1**: Create new `LastFmService.cs` with raw HTTP implementation
**Wave 2**: Update `SyncLastFmCommand.cs` and `LastFmSetup.cs`, delete `LastFmFetchState.cs`
**Wave 3**: Remove Hqub.Lastfm package and verify build

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 | none | 2, 3 | none |
| 2 | 1 | 3 | none |
| 3 | 2 | none | none |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [ ] 1. Create new `LastFmService.cs` with raw HTTP implementation
  What to do / Must NOT do:
    - Create `LastFmService.cs` with constructor: `(HttpClient httpClient, string apiKey, string username)`
    - Implement `FetchScrobblesAsync(fetchAfter, onPage, ct)` method
    - **CRITICAL: Wire `fetchAfter` to the `from` query parameter:**
      ```csharp
      var queryParams = new Dictionary<string, string>
      {
          ["method"] = "user.getrecenttracks",
          ["user"] = _username,
          ["api_key"] = _apiKey,
          ["format"] = "json",
          ["limit"] = limit.ToString(),
          ["page"] = page.ToString(),
      };
      if (fetchAfter.HasValue)
          queryParams["from"] = fetchAfter.Value.ToUnixTimeSeconds().ToString();
      ```
    - **CRITICAL: Timestamp-based rate limiting (NOT SemaphoreSlim):**
      ```csharp
      private DateTimeOffset _lastRequestTime = DateTimeOffset.MinValue;

      private async Task WaitForRateLimit(CancellationToken ct)
      {
          var elapsed = DateTimeOffset.UtcNow - _lastRequestTime;
          if (elapsed < TimeSpan.FromMilliseconds(200))
              await Task.Delay(TimeSpan.FromMilliseconds(200) - elapsed, ct);
          _lastRequestTime = DateTimeOffset.UtcNow;
      }
      ```
      Call `WaitForRateLimit(ct)` before EVERY HTTP request.
    - **Handle Last.fm API quirks:**
      - **Single-track vs array response:** `recenttracks.track` may be a single object or array:
        ```csharp
        var tracksElement = root.GetProperty("recenttracks").GetProperty("track");
        JsonElement[] tracks = tracksElement.ValueKind switch
        {
            JsonValueKind.Array => tracksElement.EnumerateArray().ToArray(),
            JsonValueKind.Object => [tracksElement],
            _ => []
        };
        ```
      - **Error codes at JSON root:** Last.fm returns `{ "error": 8, "message": "..." }` at root level, NOT nested under `recenttracks`. Check for root `error` property BEFORE accessing `recenttracks`:
        ```csharp
        if (root.TryGetProperty("error", out var errorElement))
        {
            var errorCode = errorElement.GetInt32();
            var errorMessage = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Unknown";
            var errorType = ClassifyError(errorCode);
            // handle based on errorType...
        }
        ```
      - **Now-playing tracks:** `uts == "0"` or missing `date` element — skip them:
        ```csharp
        if (!track.TryGetProperty("date", out var dateElement))
            continue;
        var uts = dateElement.GetProperty("uts").GetString();
        if (uts is "0" or null)
            continue;
        var playedAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(uts));
        ```
      - **Error classification switch expression:**
        ```csharp
        public static LastFmErrorType ClassifyError(int errorCode) => errorCode switch
        {
            8 or 11 or 16 => LastFmErrorType.Retryable,
            29 => LastFmErrorType.Retryable,
            4 or 9 or 10 or 13 or 14 or 17 or 26 => LastFmErrorType.Fatal,
            _ => LastFmErrorType.Permanent
        };
        ```
      - Add User-Agent header: `"AzureAI/1.0"`
      - Set HttpClient.Timeout to 30 seconds (set in DI registration, not here)
      - Respect Retry-After header on 429 responses
    - Implement retry: 3 attempts, exponential backoff (1s → 2s → 4s)
    - Keep `MergeScrobbles`, `LoadScrobblesAsync`, `SaveScrobblesAsync` static methods
    - No `DateTime.Now` or local time conversions
    - No `SemaphoreSlim`
    - No comments
  Parallelization: Wave 1 | Blocked by: none | Blocks: 2, 3
  References (executor has NO interview context - be exhaustive):
    - `src/Services/LastFm/LastFmService.cs` (current implementation to replace — note `from: null` on lines 98 and 114 that must be fixed)
    - `src/Services/LastFm/Models/LastFmScrobble.cs` (data model to keep)
  Acceptance criteria (agent-executable): `dotnet build src/Services/LastFm/LastFm.csproj` succeeds
  QA scenarios (name the exact tool + invocation):
    - happy: `dotnet build src/App/App.csproj` — must succeed
    - failure: `rg 'SemaphoreSlim' src/Services/LastFm/ --count` — must return zero
    - failure: `rg 'DateTime\.Now' src/Services/LastFm/ --count` — must return zero
    - failure: `rg 'TimeZoneInfo' src/Services/LastFm/ --count` — must return zero
    - failure: `rg 'from: null' src/Services/LastFm/LastFmService.cs --count` — must return zero (fetchAfter must be wired)
  Commit: Y | feat(lastfm): replace Hqub.Lastfm with raw HttpClient implementation

- [ ] 2. Update `SyncLastFmCommand.cs` and `LastFmSetup.cs`, delete `LastFmFetchState.cs`
  What to do / Must NOT do:
    - Update `SyncLastFmCommand.cs` to remove all `LastFmFetchState` references
    - Derive `fetchAfter` from `existing[0].PlayedAt` when `existing.Count > 0`:
      ```csharp
      DateTimeOffset? fetchAfter = null;
      if (s.Since is { } sinceStr)
      {
          // existing --since logic...
      }
      else if (existing.Count > 0)
      {
          fetchAfter = existing[0].PlayedAt;
          Telemetry.Info("Incremental sync after {Date}", fetchAfter.Value.ToString("yyyy-MM-dd HH:mm"));
      }
      ```
    - Keep `--since` flag logic for force resync
    - **Update `LastFmSetup.cs` to use `IHttpClientFactory`:**
      ```csharp
      public static IServiceCollection AddLastFmServices(this IServiceCollection services)
      {
          var apiKey = Environment.GetEnvironmentVariable("LASTFM_API_KEY")
              ?? throw new InvalidOperationException("LASTFM_API_KEY not set in .env");
          var username = Environment.GetEnvironmentVariable("LASTFM_USERNAME")
              ?? throw new InvalidOperationException("LASTFM_USERNAME not set in .env");

          services.AddHttpClient<LastFmService>(client =>
          {
              client.DefaultRequestHeaders.UserAgent.ParseAdd("AzureAI/1.0");
              client.Timeout = TimeSpan.FromSeconds(30);
          });
          services.AddSingleton(new LastFmService(/* resolve via factory */));
          return services;
      }
      ```
      Note: Constructor signature changes to `(HttpClient httpClient, string apiKey, string username)`. The `apiSecret` parameter is removed (not needed for `user.getrecenttracks`).
    - Delete `src/Services/LastFm/Models/LastFmFetchState.cs`
    - Remove `using Services.LastFm.Models` from `SyncLastFmCommand.cs` if only used for `LastFmFetchState`
    - Remove the `StatePath` (fetch-state.json) constant from `SyncLastFmCommand.cs`
  Parallelization: Wave 2 | Blocked by: 1 | Blocks: 3
  References (executor has NO interview context - be exhaustive):
    - `src/CLI/Sync/LastFm/SyncLastFmCommand.cs` (current implementation to update — note state.FetchComplete on line 40 and LastFmFetchState usage on lines 61-68)
    - `src/Services/LastFm/LastFmSetup.cs` (DI registration to update — currently registers as `AddSingleton` without HttpClient)
    - `src/Services/LastFm/Models/LastFmFetchState.cs` (file to delete)
  Acceptance criteria (agent-executable): `dotnet build src/CLI/CLI.csproj` succeeds
  QA scenarios (name the exact tool + invocation):
    - happy: `dotnet build src/App/App.csproj` — must succeed
    - failure: `rg 'LastFmFetchState' src/ --count` — must return zero matches
    - failure: `rg 'fetch-state\.json' src/ --count` — must return zero matches
  Commit: Y | refactor(lastfm): remove fetch-state.json dependency, delete LastFmFetchState

- [ ] 3. Remove Hqub.Lastfm package and verify build
  What to do / Must NOT do:
    - Remove `<PackageReference Include="Hqub.Last.fm" />` from `src/Services/LastFm/LastFm.csproj`
    - Remove any remaining `using Hqub.Lastfm` or `using Hqub.Lastfm.Entities` from all files
    - Run `dotnet build src/App/App.csproj` to verify clean build
    - Run `dotnet restore` to ensure no broken references
  Parallelization: Wave 3 | Blocked by: 2 | Blocks: none
  References (executor has NO interview context - be exhaustive):
    - `src/Services/LastFm/LastFm.csproj` (package reference to remove — line 6)
  Acceptance criteria (agent-executable): `dotnet build src/App/App.csproj` succeeds with zero errors
  QA scenarios (name the exact tool + invocation):
    - happy: `dotnet build src/App/App.csproj` — must succeed
    - failure: `rg 'Hqub' src/ --count` — must return zero matches
  Commit: Y | cleanup(lastfm): remove Hqub.Lastfm NuGet package

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. Plan compliance audit: `rg Hqub src/` returns zero matches
- [ ] F2. Code quality review: `dotnet build src/App/App.csproj` — zero warnings
- [ ] F3. Scope fidelity: no new files created beyond what's specified, no new NuGet packages
- [ ] F4. **Rate limiting correctness:** `rg 'SemaphoreSlim' src/Services/LastFm/ --count` returns zero
- [ ] F5. **Time correctness:** `rg 'DateTime\.Now|TimeZoneInfo' src/Services/LastFm/ --count` returns zero
- [ ] F6. **Incremental sync wiring:** `rg 'from: null|fetchAfter\.Value\.ToUnixTimeSeconds' src/Services/LastFm/LastFmService.cs` shows `ToUnixTimeSeconds` and zero `from: null`

## Commit strategy
1. `feat(lastfm): replace Hqub.Lastfm with raw HttpClient implementation` — LastFmService.cs
2. `refactor(lastfm): remove fetch-state.json dependency, delete LastFmFetchState` — SyncLastFmCommand.cs, LastFmSetup.cs, LastFmFetchState.cs
3. `cleanup(lastfm): remove Hqub.Lastfm NuGet package` — LastFm.csproj

## Success criteria
- `rg Hqub src/` returns zero matches
- `rg LastFmFetchState src/` returns zero matches
- `rg 'SemaphoreSlim' src/Services/LastFm/ --count` returns zero
- `rg 'DateTime\.Now' src/Services/LastFm/ --count` returns zero
- `rg 'TimeZoneInfo' src/Services/LastFm/ --count` returns zero
- `dotnet build src/App/App.csproj` succeeds with zero errors
- All timestamps in `scrobbles.json` are UTC (DateTimeOffset with offset +00:00)
- `fetchAfter` is correctly wired to `from` query parameter (Unix timestamp string)
- Rate limiting: 200ms between API requests via timestamp tracking
- Retry logic: 3 attempts with exponential backoff
- `--since` flag still works for force resync
