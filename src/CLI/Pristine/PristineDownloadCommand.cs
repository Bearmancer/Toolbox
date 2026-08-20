using System.Linq;
using System.Text.RegularExpressions;
using ErrorOr;
using Services.Pristine;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Pristine;

public sealed class PristineDownloadCommand(PristineOrchestrator orchestrator) : AsyncCommand<PristineDownloadCommand.Settings>
{
	private static readonly Regex SplitCodes = new(@"[,\s;]+", RegexOptions.Compiled);

	public sealed class Settings : CommandSettings
	{
		[CommandArgument(0, "[codes]")]
		public string[] Codes { get; init; } = [];

		[CommandOption("-o|--out-dir")]
		public string? OutDir { get; init; }

		[CommandOption("-c|--codes")]
		public string[]? CodesOption { get; init; }

		[CommandOption("-H|--headless")]
		public bool Headless { get; init; }

		[CommandOption("--single")]
		public bool Single { get; init; }
	}

	protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken ct)
	{
		if (settings.Headless)
			Environment.SetEnvironmentVariable("PRISTINE_HEADLESS", "1");

		var merged = settings.Codes.Concat(settings.CodesOption ?? []).ToArray();
		var normalized = NormalizeCodes(merged);
		var codes = normalized.Length > 0 ? normalized : null;
		ErrorOr<List<PristineAlbumResult>> result = await orchestrator.DownloadAsync(codes, settings.OutDir, settings.Headless, settings.Single, ct);
		return result.Match(
			results =>
			{
				foreach (PristineAlbumResult r in results)
					AnsiConsole.MarkupLine($"[green]{r.Code}[/] {r.Title} {r.Downloaded}/{r.Expected} -> {r.OutPath}");

				return 0;
			},
			errors =>
			{
				var msg = errors.Count > 0 && errors[0].Description is string desc ? desc : "Unknown error";
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
