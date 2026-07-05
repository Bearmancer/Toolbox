# Phase 2: Extract Dashboard from CLI into Services.Google

## Task 5: Create DashboardService

Create `src/Services/Google/YouTube/DashboardService.cs`:

```csharp
using System.Text.Json;
using Core;
using ErrorOr;

namespace Services.Google.YouTube;

public sealed class DashboardService
{
    private static readonly string ManifestFile = Path.Combine(YouTubePaths.StateRoot, "manifest.json");
    private static readonly string ProcessedDir = YouTubePaths.ProcessedDir;

    public async Task<ErrorOr<string>> GenerateHtmlAsync(CancellationToken ct)
    {
        return await LoadPlaylistsAsync(ct)
            .ThenAsync(playlists => LoadVideosByPlaylistAsync(playlists, ct))
            .ThenAsync(ctx => BuildHtml(ctx.playlists, ctx.videosByPlaylist));
    }

    private static async Task<ErrorOr<IReadOnlyList<PlaylistSnapshot>>> LoadPlaylistsAsync(CancellationToken ct)
    {
        if (!File.Exists(ManifestFile)) return (IReadOnlyList<PlaylistSnapshot>)[];
        try
        {
            var state = await YouTubeFetchState.LoadAsync(ManifestFile, ct);
            return (IReadOnlyList<PlaylistSnapshot>)[.. state.PlaylistSnapshots.Values];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Telemetry.Error("Failed to load manifest: {Error}", ex.Message);
            return (IReadOnlyList<PlaylistSnapshot>)[];
        }
    }

    private static async Task<ErrorOr<(IReadOnlyList<PlaylistSnapshot> playlists, Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist)>> LoadVideosByPlaylistAsync(
        IReadOnlyList<PlaylistSnapshot> playlists, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<YouTubeVideo>>();
        if (!Directory.Exists(ProcessedDir)) return (playlists, result);

        foreach (var file in Directory.GetFiles(ProcessedDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(file);
                var videos = await JsonSerializer.DeserializeAsync<List<YouTubeVideo>>(stream, YouTubeFetchState.JsonOptions, ct);
                if (videos is { Count: > 0 })
                    result[Path.GetFileNameWithoutExtension(file)] = videos;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Telemetry.Warn("Skipping corrupt file {File}: {Error}", Path.GetFileName(file), ex.Message);
            }
        }
        return (playlists, result);
    }

    private static ErrorOr<string> BuildHtml(
        IReadOnlyList<PlaylistSnapshot> playlists,
        Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist)
        => DashboardHtmlGenerator.Generate(playlists, videosByPlaylist);
}
```

**Must NOT:**
- Use block-scoped namespaces
- Add comments
- Reference `PathResolver` directly — use `YouTubePaths` constants

**QA:**
```bash
dotnet build src/Services/Google/Google.csproj
```

**Commit:** `feat(youtube): add DashboardService for HTML generation`

---

## Task 6: Register DashboardService in DI

In `src/Services/Google/GoogleSetup.cs`, add inside the `extension(IServiceCollection services)` block, before `return services;`:

```csharp
services.AddSingleton<DashboardService>();
```

**QA:**
```bash
dotnet build src/Services/Google/Google.csproj
```

**Commit:** `feat(youtube): register DashboardService in DI`

---

## Task 7: Slim DashboardGenerateCommand

Replace entire contents of `src/CLI/Dashboard/DashboardGenerateCommand.cs` with:

```csharp
using System.ComponentModel;
using Core;
using Services.Google.YouTube;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Dashboard;

[Description(
    "Generate an HTML dashboard from locally synced YouTube playlist data. "
        + "Loads all playlists from the manifest and all videos from processed JSON files."
)]
public class DashboardGenerateCommand(DashboardService service) : AsyncCommand<DashboardGenerateCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        using var _ = Telemetry.ForService(ServiceName.Google);

        var result = await service.GenerateHtmlAsync(ct);
        return result.Match(
            html => WriteOutput(html, s.Output, ct),
            errors => { Console.Error.WriteLine(errors[0].Description); return 1; });
    }

    private static async Task<int> WriteOutput(string html, string? outputOverride, CancellationToken ct)
    {
        var outputPath = outputOverride ?? Path.Combine(Directory.GetCurrentDirectory(), "dashboard.html");
        await File.WriteAllTextAsync(outputPath, html, ct);
        var size = new FileInfo(outputPath).Length;
        AnsiConsole.MarkupLine($"[green]Dashboard generated:[/] {outputPath} ({size / 1024.0:F1} KB)");
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("Output file path for the generated HTML dashboard. (default: dashboard.html in current directory)")]
        [CommandOption("--output <PATH>")]
        public string? Output { get; init; }
    }
}
```

**Must NOT:**
- Keep `LoadPlaylistsAsync`, `LoadVideosByPlaylistAsync` — moved to DashboardService
- Import `System.Text.Json` — no longer needed here
- Reference `YouTubeFetchState` directly

**QA:**
```bash
dotnet build
```
Expected: Clean build. Command went from 123 lines → ~40 lines.

**Commit:** `refactor(dashboard): slim DashboardGenerateCommand — delegate to DashboardService`
