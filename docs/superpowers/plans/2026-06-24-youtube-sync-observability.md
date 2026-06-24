# YouTube Sync Observability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make a 140-playlist sync produce meaningful live progress instead of 1-3 lines, and surface errors with enough specificity to act on them.

**Architecture:** All changes are pure log-level and error-handling additions to four existing files. No new abstractions, no new files. The principle: null `TranslatedTitle` IS the signal that translation is pending — don't assign nulls, don't discard work done.

**Tech Stack:** Serilog via `Telemetry.*`, `Google.GoogleApiException`, `Azure.RequestFailedException`

---

## Files

| File | Change |
|---|---|
| `src/Services/Google/YouTubePlaylistOrchestrator.cs` | Progress logging + video-level diff (fresh vs update) |
| `src/Services/Google/YouTubeTranslationService.cs` | Translation stage logging + completeness warning |
| `src/Services/Google/YoutubeService.cs` | API error categorization for all Google calls |
| `src/Services/Azure/TranslateService.cs` | API error categorization for Azure Translator calls |

---

## Task 1: Progress logging in the orchestrator

**Files:**
- Modify: `src/Services/Google/YouTubePlaylistOrchestrator.cs`

The current `ExecuteAsync` runs through all playlists silently. Fix: log a numbered progress line before each playlist starts, and promote the per-playlist completion line from Debug to Info.

- [ ] **Step 1: Add playlist index counter to `ExecuteAsync`**

Replace the `foreach (var snapshot in playlistsToProcess)` loop body (the block containing `if (ct.IsCancellationRequested)`):

```csharp
var playlistIndex = 0;
foreach (var snapshot in playlistsToProcess)
{
    if (ct.IsCancellationRequested)
        break;

    playlistIndex++;
    Telemetry.Info(
        "[{Index}/{Total}] {Title}",
        playlistIndex,
        playlistsToProcess.Count,
        snapshot.Title);

    var (videos, skipped) = await ProcessPlaylistAsync(snapshot, ct);
    totalVideos += videos;
    skippedVideos += skipped;
}
```

- [ ] **Step 2: Promote the per-playlist completion log from Debug to Info**

In `ProcessPlaylistAsync`, find the `Telemetry.Debug("Saved playlist: {Title}...")` call and change it to:

```csharp
Telemetry.Info(
    "  done — {Count} videos, {Skipped} skipped in {Elapsed:F1}s ({Quota} quota units)",
    videos.Count,
    skipped,
    playlistStopwatch.Elapsed.TotalSeconds,
    quotaUsed);
```

- [ ] **Step 3: Build and verify**

```powershell
dotnet build AzureAI.slnx
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```powershell
git add src/Services/Google/YouTubePlaylistOrchestrator.cs
git commit -m "feat: add per-playlist numbered progress logging to orchestrator"
```

---

## Task 2: Video-level diff — fresh vs update sync

**Files:**
- Modify: `src/Services/Google/YouTubePlaylistOrchestrator.cs`

For each playlist being processed, load the existing `processed/<Title>.json` if it exists. Compare `VideoId` sets against the freshly fetched video list to report new/deleted/net change. The comparison happens after the videos list is built but before the processed file is overwritten.

- [ ] **Step 1: Add a static helper to load existing video IDs**

Add this private static method to the `YouTubePlaylistOrchestrator` class:

```csharp
private static async Task<HashSet<string>> LoadExistingVideoIdsAsync(string processedPath, CancellationToken ct)
{
    if (!File.Exists(processedPath))
        return [];

    try
    {
        await using var stream = File.OpenRead(processedPath);
        var existing = await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(
            stream, YouTubeFetchState.JsonOptions, ct);
        return existing?.Select(v => v.VideoId).ToHashSet() ?? [];
    }
    catch (JsonException)
    {
        return [];
    }
}
```

- [ ] **Step 2: Call the helper and log the diff in `ProcessPlaylistAsync`**

In `ProcessPlaylistAsync`, after the `foreach (var item in allItems)` loop that builds the `videos` list (after `skipped++` / `videos.Add(...)` block), and before the `Telemetry.Debug("Translating {Count} videos...")` call, add:

```csharp
var existingIds = await LoadExistingVideoIdsAsync(playlistPath, ct);
var incomingIds = videos.Select(v => v.VideoId).ToHashSet();

if (existingIds.Count == 0)
{
    Telemetry.Info("  fresh sync: {Count} videos", videos.Count);
}
else
{
    var added = incomingIds.Except(existingIds).Count();
    var removed = existingIds.Except(incomingIds).Count();
    var net = added - removed;
    var netStr = net switch { > 0 => $"+{net}", 0 => "net 0", _ => $"{net}" };
    Telemetry.Info(
        "  update sync: {Added} added, {Removed} removed ({Net}), {Total} total",
        added, removed, netStr, videos.Count);
}
```

> **Note:** `playlistPath` is already declared earlier in `ProcessPlaylistAsync` as `Path.Combine(ProcessedDir, $"{sanitizedTitle}.json")`. The helper reads the file before `File.WriteAllTextAsync` overwrites it — ordering is safe.

- [ ] **Step 3: Build and verify**

```powershell
dotnet build AzureAI.slnx
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```powershell
git add src/Services/Google/YouTubePlaylistOrchestrator.cs
git commit -m "feat: log video-level diff (fresh vs update) per playlist"
```

---

## Task 3: Translation stage logging + completeness warning

**Files:**
- Modify: `src/Services/Google/YouTubeTranslationService.cs`

Log: how many need translating vs already done, per-batch progress when there are multiple batches, final count of translated vs English-skipped, and a Warn if any videos still have null TranslatedTitle after the call.

The English-skip logic is already correct: if `TranslatedText == original`, TranslatedTitle is set to the original (non-null). So null TranslatedTitle after the call means the API call failed or was not reached.

- [ ] **Step 1: Log translation stage summary at the top**

Replace the early-return block:
```csharp
if (toTranslate.Count == 0)
    return videos;
```

With:
```csharp
if (toTranslate.Count == 0)
{
    Telemetry.Info("  translation: all {Count} already translated (skipped)", videos.Count);
    return videos;
}

var alreadyDone = videos.Count - toTranslate.Count;
Telemetry.Info(
    "  translation: {NeedTranslation} to translate, {AlreadyDone} already done",
    toTranslate.Count,
    alreadyDone);
```

- [ ] **Step 2: Log per-batch progress**

Replace the `foreach (var batch in texts.Chunk(MaxTextsPerCall))` loop:

```csharp
var batchIndex = 0;
var totalBatches = (int)Math.Ceiling(texts.Count / (double)MaxTextsPerCall);
foreach (var batch in texts.Chunk(MaxTextsPerCall))
{
    batchIndex++;
    if (totalBatches > 1)
        Telemetry.Info(
            "  translating batch {Batch}/{Total} ({Count} texts)",
            batchIndex, totalBatches, batch.Length);

    var batchResults = await translateService.TranslateBatchAsync(batch, "en", ct);
    allResults.AddRange(batchResults);
}
```

- [ ] **Step 3: Count English-skipped during the write-back loop and log summary**

Replace the existing `foreach (var (resultIndex, (videoIndex, _)) in toTranslate.Index())` loop with:

```csharp
var englishSkipped = 0;
foreach (var (resultIndex, (videoIndex, _)) in toTranslate.Index())
{
    var titleResult = allResults[resultIndex * 2];
    var descResult = allResults[resultIndex * 2 + 1];
    var video = videos[videoIndex];

    var translatedTitle = titleResult.TranslatedText == video.Title
        ? video.Title
        : titleResult.TranslatedText;
    var translatedDesc = descResult.TranslatedText == video.Description
        ? video.Description
        : descResult.TranslatedText;

    if (titleResult.TranslatedText == video.Title)
        englishSkipped++;

    videos[videoIndex] = video with
    {
        TranslatedTitle = translatedTitle,
        TranslatedDescription = translatedDesc,
    };
}

Telemetry.Info(
    "  translation done: {Translated} translated, {Skipped} English (skipped)",
    toTranslate.Count - englishSkipped,
    englishSkipped);
```

- [ ] **Step 4: Warn on null TranslatedTitle after the call**

Immediately before `return videos;` at the end of `TranslateVideosAsync`, add:

```csharp
var missing = videos.Count(v => v.TranslatedTitle is null);
if (missing > 0)
    Telemetry.Warn(
        "  {Missing}/{Total} videos still have null TranslatedTitle — will retry on next sync",
        missing,
        videos.Count);

return videos;
```

- [ ] **Step 5: Build and verify**

```powershell
dotnet build AzureAI.slnx
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 6: Commit**

```powershell
git add src/Services/Google/YouTubeTranslationService.cs
git commit -m "feat: add translation stage logging and completeness warning"
```

---

## Task 4: API error categorization

**Files:**
- Modify: `src/Services/Google/YoutubeService.cs`
- Modify: `src/Services/Azure/TranslateService.cs`

Both SDKs handle backoff internally. We catch what they surface after retries exhaust, categorize by HTTP status, log an actionable message, and re-throw. Rate limit (429) = try later. Auth failure (401/403) = fix credentials.

### Part A — Google

- [ ] **Step 1: Add `using System.Net;` to `YoutubeService.cs`**

At the top of `src/Services/Google/YoutubeService.cs`, alongside existing usings, add:

```csharp
using System.Net;
using Google;
```

`Google.GoogleApiException` lives in the `Google` namespace from the `Google.Apis.Core` NuGet package (already a transitive dependency).

- [ ] **Step 2: Add `LogGoogleApiError` helper method to `YoutubeService`**

Add this private static method to the `YoutubeService` class:

```csharp
private static void LogGoogleApiError(GoogleApiException ex, string operation)
{
    switch (ex.HttpStatusCode)
    {
        case HttpStatusCode.TooManyRequests:
            Telemetry.Warn(
                "Google API rate limit hit during {Operation} — SDK retried and exhausted. Try again later.",
                operation);
            break;
        case HttpStatusCode.Forbidden:
            Telemetry.Error(
                "Google API quota exceeded or API not enabled during {Operation}. Check Google Cloud Console quota.",
                operation);
            break;
        case HttpStatusCode.Unauthorized:
            Telemetry.Error(
                "Google API authentication failed during {Operation}. OAuth token invalid or expired — delete token cache and re-auth.",
                operation);
            break;
        default:
            Telemetry.Error(
                "Google API error during {Operation}: HTTP {Status} — {Message}",
                operation,
                (int)ex.HttpStatusCode,
                ex.Error?.Message ?? ex.Message);
            break;
    }
}
```

- [ ] **Step 3: Wrap `GetPlaylistSummariesAsync` pagination loop**

In `GetPlaylistSummariesAsync`, wrap the `do { ... } while (pageToken is not null)` loop:

```csharp
try
{
    do
    {
        request.PageToken = pageToken;
        var response = await request.ExecuteAsync(ct);
        QuotaUsed++;

        foreach (var playlist in response.Items ?? [])
        {
            var publishedAt = DateTimeOffset.Parse(playlist.Snippet!.PublishedAtRaw!);
            snapshots.Add(new PlaylistSnapshot
            {
                PlaylistId = playlist.Id!,
                Title = playlist.Snippet!.Title!,
                LastUpdated = publishedAt,
                LastChecked = DateTimeOffset.UtcNow,
                ETag = playlist.ETag!,
                ReportedVideoCount = playlist.ContentDetails!.ItemCount!.Value,
            });
        }

        pageToken = response.NextPageToken;
    }
    while (pageToken is not null);
}
catch (GoogleApiException ex)
{
    LogGoogleApiError(ex, nameof(GetPlaylistSummariesAsync));
    throw;
}
```

- [ ] **Step 4: Wrap `GetPlaylistItemPagesRawAsync` pagination loop**

In `GetPlaylistItemPagesRawAsync`, wrap the `do { ... } while (pageToken is not null)` loop:

```csharp
try
{
    do
    {
        request.PageToken = pageToken;
        var response = await request.ExecuteAsync(ct);
        QuotaUsed++;
        pages.Add(response);
        pageToken = response.NextPageToken;
    }
    while (pageToken is not null);
}
catch (GoogleApiException ex)
{
    LogGoogleApiError(ex, nameof(GetPlaylistItemPagesRawAsync));
    throw;
}
```

- [ ] **Step 5: Wrap `GetVideoDurationsAsync` batch loop**

In `GetVideoDurationsAsync`, wrap the `foreach (var batch in videoIds.Chunk(50))` loop:

```csharp
try
{
    foreach (var batch in videoIds.Chunk(50))
    {
        var request = yt.Videos.List("contentDetails");
        request.Id = string.Join(",", batch);
        var response = await request.ExecuteAsync(ct);
        QuotaUsed++;

        foreach (var video in response.Items ?? [])
        {
            var duration = ParseIso8601Duration(video.ContentDetails?.Duration);
            result[video.Id] = duration;
        }
    }
}
catch (GoogleApiException ex)
{
    LogGoogleApiError(ex, nameof(GetVideoDurationsAsync));
    throw;
}
```

### Part B — Azure Translator

- [ ] **Step 6: Add `using Azure;` to `TranslateService.cs`**

Check existing usings in `src/Services/Azure/TranslateService.cs`. Add if missing:

```csharp
using Azure;
```

`RequestFailedException` is in the `Azure` namespace from the `Azure.Core` package (already a dependency via `Azure.AI.Translation.Text`).

- [ ] **Step 7: Wrap `client.TranslateAsync(...)` call in `TranslateBatchAsync`**

Replace the single `var response = await client.TranslateAsync(...)` line with:

```csharp
Response<IReadOnlyList<TranslatedTextItem>> response;
try
{
    response = await client.TranslateAsync(toLang, texts, cancellationToken: ct);
}
catch (RequestFailedException ex)
{
    switch (ex.Status)
    {
        case 429:
            Telemetry.Warn(
                "Azure Translator rate limit hit — SDK retried and exhausted. Batch of {Count} texts not translated.",
                texts.Count);
            break;
        case 401:
            Telemetry.Error(
                "Azure Translator authentication failed (401). Check AZURE_TRANSLATOR_KEY in .env.");
            break;
        case 403:
            Telemetry.Error(
                "Azure Translator access denied (403). Key valid but quota may be exhausted or endpoint restricted.");
            break;
        default:
            Telemetry.Error(
                "Azure Translator error: HTTP {Status} — {Message}",
                ex.Status,
                ex.Message);
            break;
    }
    throw;
}
```

- [ ] **Step 8: Build and verify**

```powershell
dotnet build AzureAI.slnx
```

Expected: 0 errors, 0 warnings. If `GoogleApiException` fails to resolve, run `dotnet build 2>&1 | Select-String "GoogleApiException"` to see the exact error and correct the namespace.

- [ ] **Step 9: Commit**

```powershell
git add src/Services/Google/YoutubeService.cs src/Services/Azure/TranslateService.cs
git commit -m "feat: categorize rate limit vs auth errors for Google and Azure APIs"
```
