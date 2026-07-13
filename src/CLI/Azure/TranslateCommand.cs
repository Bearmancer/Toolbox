using System.ComponentModel;
using Core;
using ErrorOr;
using Services.Azure;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description(
	"Translate text from one language to another using Azure Translator. "
		+ "When --from is omitted the source language is auto-detected. "
		+ "When --to is omitted the output defaults to English."
)]
public class TranslateCommand(TranslateService service) : AsyncCommand<TranslateCommand.Settings>
{
	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken cancellationToken
	)
	{
		ErrorOr<List<TranslationResult>> result = await service.TranslateBatchAsync(
			[s.Text],
			s.To,
			cancellationToken
		);
		return result.Match(
			results =>
			{
				Telemetry.Info("{Result}", results[0].TranslatedText);
				return 0;
			},
			errors =>
			{
				Telemetry.Error("{Error}", errors[0].Description);
				return 1;
			}
		);
	}

	public sealed class Settings : CommandSettings
	{
		[Description("The text to translate.")]
		[CommandArgument(0, "<text>")]
		public required string Text { get; init; }

		[Description(
			"Target language code (BCP-47 format, e.g. 'es', 'fr', 'de', 'ja'). "
				+ "Default is 'ja'."
		)]
		[CommandOption("--to <LANG>")]
		[DefaultValue("ja")]
		public string To { get; init; } = "ja";

		[Description(
			"Source language code (BCP-47 format, e.g. 'en', 'es'). "
				+ "When omitted the language is auto-detected from the input text."
		)]
		[CommandOption("--from <LANG>")]
		public string? From { get; init; }
	}
}
