using System.ComponentModel;
using ErrorOr;
using Services.Azure;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description(
	"Extract named entities from text using Azure AI Language. "
		+ "Identifies people, organizations, locations, dates, quantities, and more."
)]
public class NerCommand(TextAnalyticsService service) : AsyncCommand<NerCommand.Settings>
{
	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken cancellationToken
	)
	{
		ErrorOr<string> result = await service.EntitiesAsync(
			s.Text,
			s.Lang ?? "en",
			cancellationToken
		);
		return result.Match(
			success =>
			{
				Console.WriteLine(success);
				return 0;
			},
			errors =>
			{
				Console.Error.WriteLine(errors[0].Description);
				return 1;
			}
		);
	}

	public sealed class Settings : CommandSettings
	{
		[Description("The text to extract named entities from.")]
		[CommandArgument(0, "<text>")]
		public required string Text { get; init; }

		[Description(
			"Language of the input text (BCP-47 format, e.g. 'en', 'es', 'fr', 'de'). "
				+ "Specifying the language improves entity detection accuracy. (default: en)"
		)]
		[CommandOption("--lang <LANG>")]
		public string? Lang { get; init; }
	}
}
