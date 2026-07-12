# Phase 7: Dashboard eval() Fix

## Task 28: Replace eval() with object lookup in DashboardHtmlGenerator

In `src/CLI/Dashboard/DashboardHtmlGenerator.cs`, replace the `eval()` pattern.

**Replace line 257:**
```javascript
data: eval(dataVarName),
```

**With:**
```javascript
data: allVideoDataMap[playlistId] || [],
```

**And add before the `document.querySelectorAll` block (after `allVideosTableInstance` setup):**
```javascript
var allVideoDataMap = {};
Object.entries(videoDataByPlaylist).forEach(([pid, videos]) => { allVideoDataMap[pid] = videos; });
```

**Also remove the per-playlist variable generation.** Replace the `videoDataJs` block (lines 93-98) with:

```csharp
var videoDataJs = new StringBuilder();
videoDataJs.AppendLine($"var videoDataByPlaylist = {System.Text.Json.JsonSerializer.Serialize(videoDataByPlaylist)};");
```

**Must NOT:**
- Change any other JavaScript in the template
- Remove search/filter functionality

**QA:**
```bash
dotnet build
dotnet run --project src/App -- dashboard generate
```
Open `dashboard.html` in browser. Verify playlists load, search works, no JS console errors.

**Commit:** `fix(dashboard): replace eval() with object lookup in DashboardHtmlGenerator`
