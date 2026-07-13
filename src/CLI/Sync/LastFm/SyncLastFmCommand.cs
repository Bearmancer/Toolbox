using System.ComponentModel;
using Core;
using ErrorOr;
using Services.LastFm;
using Spectre.Console.Cli;

namespace CLI.Sync.LastFm;

[Description(
	"Sync Last.fm scrobble history to local JSON files. "
		+ "Fetches recent tracks from Last.fm API and stores them "
		+ "as structured JSON under state/lastfm/. Supports incremental sync "
		+ "and forced resync from a specific date."
)]
public class SyncLastFmCommand(LastFmSyncOrchestrator orchestrator)
	: AsyncCommand<SyncLastFmCommand.Settings>
{
	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.LastFm);

		DateTimeOffset? fetchAfter = null;
		if (s.Since is { } sinceStr)
		{
			if (!DateTimeOffset.TryParse(sinceStr, out DateTimeOffset sinceDate))
			{
				Telemetry.Error(
					"Invalid --since format: {Value}. Use ISO 8601 (e.g., 2024-01-01)",
					sinceStr
				);
				return 1;
			}
			fetchAfter = sinceDate;
			Telemetry.Info(
				"LastFm sync starting (forced from {Date})",
				sinceDate.ToString("yyyy-MM-dd HH:mm")
			);
		}
		else
		{
			Telemetry.Info("LastFm sync starting");
		}

		ErrorOr<LastFmSyncOrchestrator.SyncResult> result = await orchestrator.SyncAsync(
			fetchAfter,
			ct
		);

		if (result.IsError)
		{
			Telemetry.Error("Sync failed: {Error}", result.FirstError.Description);
			return 1;
		}

		return 0;
	}

	public sealed class Settings : CommandSettings
	{
		[Description(
			"Force resync from date (ISO 8601, e.g. 2024-01-01). Deletes existing data on/after this date."
		)]
		[CommandOption("--since")]
		public string? Since { get; init; }
	}
}
