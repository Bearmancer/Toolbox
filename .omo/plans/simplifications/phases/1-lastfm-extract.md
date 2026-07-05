# Phase 1: Extract LastFm Sync Logic from CLI

## Task 2: Create LastFmSyncOrchestrator

Create `src/Services/LastFm/LastFmSyncOrchestrator.cs`:

```csharp
using Core;
using ErrorOr;

namespace Services.LastFm;

public sealed class LastFmSyncOrchestrator(LastFmService service)
{
    private static readonly string StateDir = Path.Combine(PathResolver.RepoRoot, "state", "lastfm");

    public async Task<ErrorOr<int>> SyncAsync(string? sinceRaw, CancellationToken ct)
    {
        Directory.CreateDirectory(StateDir);
        var existing = await LastFmState.LoadScrobblesAsync(StateDir);

        return await ParseSinceDate(sinceRaw)
            .ThenAsync(since => FetchAndMergeAsync(existing, since, ct))
            .ThenAsync(merged => PersistAsync(merged, existing.Count, ct));
    }

    private static ErrorOr<DateTimeOffset?> ParseSinceDate(string? sinceRaw)
    {
        if (sinceRaw is null) return (DateTimeOffset?)null;
        if (DateTimeOffset.TryParse(sinceRaw, out var sinceDate)) return sinceDate;
        Telemetry.Error("Invalid --since format: {Value}. Use ISO 8601 (e.g., 2024-01-01)", sinceRaw);
        return Errors.Validation.InvalidInput("since", "Must be ISO 8601 date");
    }

    private async Task<ErrorOr<(List<LastFmScrobble> merged, int existingCount)>> FetchAndMergeAsync(
        List<LastFmScrobble> existing,
        DateTimeOffset? since,
        CancellationToken ct)
    {
        var fetchAfter = ResolveFetchAfter(existing, since);
        if (since is { } s)
        {
            existing.RemoveAll(sc => sc.PlayedAt >= s);
            Telemetry.Info("Force resync from {Date}", s.ToString("yyyy-MM-dd HH:mm"));
        }
        else if (existing.Count > 0)
        {
            Telemetry.Info("Incremental sync after {Date}", fetchAfter!.Value.ToString("yyyy-MM-dd HH:mm"));
        }

        var newScrobbles = await service.FetchRecentTracksAsync(
            fetchAfter,
            (page, count) => Telemetry.Info("Page {Page}: {Count} tracks", page, count),
            ct);

        if (newScrobbles.Count == 0)
        {
            Telemetry.Info("No new scrobbles found.");
            return (existing, existing.Count);
        }

        var merged = LastFmState.MergeScrobbles(existing, newScrobbles);
        return (merged, existing.Count);
    }

    private static DateTimeOffset? ResolveFetchAfter(List<LastFmScrobble> existing, DateTimeOffset? since)
    {
        if (since.HasValue) return since.Value;
        return existing.Count > 0 ? existing[0].PlayedAt : null;
    }

    private static async Task<ErrorOr<int>> PersistAsync(
        (List<LastFmScrobble> merged, int existingCount) data,
        int existingCount,
        CancellationToken ct)
    {
        await LastFmState.SaveScrobblesAsync(StateDir, data.merged);
        Telemetry.Info("Sync complete. {Total} total scrobbles ({New} new)",
            data.merged.Count, data.merged.Count - existingCount);
        return data.merged.Count;
    }
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Import anything beyond `Core`, `ErrorOr`

**QA:**
```bash
dotnet build src/Services/LastFm/LastFm.csproj
```

**Commit:** `feat(lastfm): add LastFmSyncOrchestrator`

---

## Task 3: Register LastFmSyncOrchestrator in DI

In `src/Services/LastFm/LastFmSetup.cs`, add inside the `extension(IServiceCollection services)` block, before `return services;`:

```csharp
services.AddSingleton<LastFmSyncOrchestrator>();
```

**QA:**
```bash
dotnet build src/Services/LastFm/LastFm.csproj
```

**Commit:** `feat(lastfm): register LastFmSyncOrchestrator in DI`

---

## Task 4: Slim SyncLastFmCommand

Replace entire contents of `src/CLI/Sync/LastFm/SyncLastFmCommand.cs` with:

```csharp
using System.ComponentModel;
using Core;
using Services.LastFm;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Sync.LastFm;

[Description(
    "Sync Last.fm scrobble history to local JSON files. "
        + "Fetches recent tracks from Last.fm API and stores them "
        + "as structured JSON under state/lastfm/. Supports incremental sync "
        + "and forced resync from a specific date."
)]
public class SyncLastFmCommand(LastFmSyncOrchestrator orchestrator) : AsyncCommand<SyncLastFmCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await orchestrator.SyncAsync(s.Since, ct);
        return result.Match(
            _ => { AnsiConsole.MarkupLine("[green]Sync complete.[/]"); return 0; },
            errors => { AnsiConsole.MarkupLine($"[red]{errors[0].Description}[/]"); return 1; });
    }

    public sealed class Settings : CommandSettings
    {
        [Description("Force resync from date (ISO 8601, e.g. 2024-01-01). Deletes existing data on/after this date.")]
        [CommandOption("--since")]
        public string? Since { get; init; }
    }
}
```

**Must NOT:**
- Import `System.IO` or `Core.PathResolver` — orchestrator owns paths
- Keep `StateDir`, `LoadScrobblesAsync`, `MergeScrobbles`, `SaveScrobblesAsync` calls — all moved

**QA:**
```bash
dotnet build
```
Expected: Clean build. Command went from 91 lines → ~30 lines.

**Commit:** `refactor(lastfm): slim SyncLastFmCommand — delegate to orchestrator`
