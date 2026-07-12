using System.ComponentModel;
using Core;
using Services.Google.YouTube;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Sync.YouTube;

[Description(
	"Sync YouTube playlist metadata to local JSON files then optionally sort "
		+ "all items alphabetically by title. Syncing downloads video titles, "
		+ "durations, channel info, and auto-translated titles. "
		+ "Change detection uses YouTube ETags to avoid re-fetching unchanged playlists. "
		+ "Sorting uses LIS-based differential repositioning — only items whose position "
		+ "actually changed are sent to the API, minimizing quota usage. "
		+ "The stored ETag is updated after each sync so subsequent runs skip "
		+ "playlists that haven't changed on YouTube."
)]
public class SyncYoutubeCommand(YouTubePlaylistOrchestrator orchestrator)
	: AsyncCommand<SyncYoutubeCommand.Settings>
{
	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken cancellationToken
	)
	{
		try
		{
			using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);

			if (!string.IsNullOrEmpty(s.Playlist))
			{
				var id = s.NoSort
					? await orchestrator.ExecuteForPlaylistTitleAsync(s.Playlist, cancellationToken)
					: await orchestrator.ExecuteForPlaylistTitleWithSortAsync(
						s.Playlist,
						cancellationToken
					);

				if (id is null)
					return 1;
			}
			else
			{
				IReadOnlyList<string> syncedIds = s.NoSort
					? await orchestrator.ExecuteAsync(cancellationToken)
					: await orchestrator.ExecuteWithSortAsync(cancellationToken);

				if (syncedIds.Count == 0)
					Telemetry.Info("No playlists needed syncing");
			}
		}
		catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
		{
			AnsiConsole.MarkupLine($"[bold red]API ERROR:[/] {ex.Message}");
			return 1;
		}

		AnsiConsole.MarkupLine("[green]Sync complete.[/]");
		return 0;
	}

	public sealed class Settings : CommandSettings
	{
		[Description(
			"Optional playlist title (partial match) to sync. "
				+ "If omitted, all playlists that changed since the last run are synced. "
				+ "If the title matches multiple playlists the first hit is used."
		)]
		[CommandArgument(0, "[playlist]")]
		public string? Playlist { get; init; }

		[Description(
			"Skip sorting playlist items alphabetically by title on YouTube. "
				+ "By default the synced playlist is sorted after metadata is fetched. "
				+ "Sorting uses LIS optimization to minimize API calls."
		)]
		[CommandOption("--no-sort")]
		public bool NoSort { get; init; }
	}
}
