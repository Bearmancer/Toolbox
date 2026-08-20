using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using ErrorOr;
using Services.Pristine;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Pristine;

[Description(
	"Download PASC albums from Pristine Classical: resolves via the API, downloads and verifies "
		+ "each FLAC, transcodes 24-bit to 16-bit. Falls back to browser automation per-album on "
		+ "API failure."
)]
public sealed class PristineDownloadCommand(PristineOrchestrator orchestrator)
	: AsyncCommand<PristineDownloadCommand.Settings>
{
	private static readonly Regex SplitCodes = new(@"[,\s;]+", RegexOptions.Compiled);
	private static readonly Regex ValidCodeFormat = new(
		@"^[A-Z]{2,6}\d{2,5}$",
		RegexOptions.Compiled
	);

	public sealed class Settings : CommandSettings
	{
		[Description("PASC codes, space- or comma-separated (e.g. PASC552 PASC553).")]
		[CommandArgument(0, "[codes]")]
		public string[] Codes { get; init; } = [];

		[Description("Output directory (default: Desktop/Pristine).")]
		[CommandOption("-o|--out-dir")]
		public string? OutDir { get; init; }

		[Description("Additional codes; merged with the positional argument.")]
		[CommandOption("-c|--codes")]
		public string[]? CodesOption { get; init; }

		[Description("Run browser fallback headless.")]
		[CommandOption("-H|--headless")]
		public bool Headless { get; init; }
	}

	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings settings,
		CancellationToken ct
	)
	{
		if (settings.Headless)
			Environment.SetEnvironmentVariable("PRISTINE_HEADLESS", "1");

		var merged = settings.Codes.Concat(settings.CodesOption ?? []).ToArray();
		var normalized = NormalizeCodes(merged);

		var invalid = normalized.Where(c => ValidCodeFormat.IsMatch(c) is false).ToArray();
		if (invalid.Length > 0)
		{
			foreach (var bad in invalid)
				AnsiConsole.MarkupLine(
					$"[yellow]Skipping invalid code format: {bad} (expected letters followed by digits, e.g. PASC552)[/]"
				);
			normalized = [.. normalized.Except(invalid, StringComparer.OrdinalIgnoreCase)];
		}

		if (normalized.Length == 0)
		{
			AnsiConsole.MarkupLine(
				"[red]No PASC codes provided. Pass at least one, e.g. `pristine download PASC552`.[/]"
			);
			return 1;
		}

		ErrorOr<List<PristineAlbumResult>> result = await orchestrator.DownloadAsync(
			normalized,
			settings.OutDir,
			settings.Headless,
			ct
		);
		return result.Match(
			results =>
			{
				var anyAlbumFailed = false;
				foreach (PristineAlbumResult r in results)
				{
					var albumFailed = r.Expected <= 0 || r.Downloaded < r.Expected;
					anyAlbumFailed |= albumFailed;
					var color = albumFailed ? "red" : "green";
					AnsiConsole.MarkupLine(
						$"[{color}]{r.Code}[/] {r.Title} {r.Downloaded}/{r.Expected} -> {r.OutPath}"
					);
				}

				if (anyAlbumFailed)
				{
					AnsiConsole.MarkupLine(
						"[red]One or more albums did not fully download and verify.[/]"
					);
					return 1;
				}

				return 0;
			},
			errors =>
			{
				var msg =
					errors.Count > 0 && errors[0].Description is string desc
						? desc
						: "Unknown error";
				AnsiConsole.MarkupLine($"[red]{msg}[/]");
				return 1;
			}
		);
	}

	private static string[] NormalizeCodes(string[] raw)
	{
		List<string> list = [];
#pragma warning disable IDE0028
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028
		foreach (var entry in raw)
		{
			if (string.IsNullOrWhiteSpace(entry))
				continue;
			var parts = SplitCodes.Split(entry.Trim());
			foreach (var part in parts)
			{
				var code = part.Trim().ToUpperInvariant();
				if (string.IsNullOrWhiteSpace(code))
					continue;
				if (seen.Add(code))
					list.Add(code);
			}
		}

		return [.. list];
	}
}
