using System.ComponentModel;
using Core;
using ErrorOr;
using Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description(
	"Synthesize speech from text using Azure Speech Service. "
		+ "Accepts inline text via --text or reads from a file via --file. "
		+ "Outputs audio to the path specified by --output."
)]
public class SpeechTtsCommand(SpeechService service) : AsyncCommand<SpeechTtsCommand.Settings>
{
	protected override async Task<int> ExecuteAsync(
		CommandContext context,
		Settings s,
		CancellationToken cancellationToken
	)
	{
		ErrorOr<string> result = !string.IsNullOrEmpty(s.File)
			? await service.SynthesizeFromFileAsync(s.File, s.Voice, s.Output, cancellationToken)
			: await service.SynthesizeAsync(s.Text ?? "", s.Voice, s.Output, cancellationToken);

		return result.Match(
			success =>
			{
				Telemetry.Info("{Result}", success);
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
		[Description("Text to synthesize. Mutually exclusive with --file.")]
		[CommandOption("--text <TEXT>")]
		public string? Text { get; init; }

		[Description("Path to a text file to synthesize. Mutually exclusive with --text.")]
		[CommandOption("--file <FILE>")]
		public string? File { get; init; }

		[Description(
			"Azure Speech voice name, e.g. 'en-GB-SoniaNeural', 'en-GB-EmmaNeural', 'en-US-AriaNeural'. "
				+ "Full list: https://learn.microsoft.com/azure/ai-services/speech-service/language-support"
		)]
		[CommandOption("--voice <VOICE>")]
		public string Voice { get; init; } = "en-GB-SoniaNeural";

		[Description("Output audio file path.")]
		[CommandOption("--output <PATH>")]
		public required string Output { get; init; }

		public override ValidationResult Validate()
		{
			var hasText = !string.IsNullOrEmpty(Text);
			var hasFile = !string.IsNullOrEmpty(File);

			if (hasText == hasFile)
				return ValidationResult.Error("Provide exactly one of --text or --file.");

			return ValidationResult.Success();
		}
	}
}
