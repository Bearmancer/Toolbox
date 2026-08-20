using Spectre.Console.Cli;

namespace CLI.Audio;

public static class AudioCommandModule
{
	public static void ConfigureCommands(IConfigurator cfg) =>
		cfg.AddBranch(
			"audio",
			b =>
			{
				b.SetDescription(
					"Audio conversion: SACD ISO extraction, DSD→FLAC, and FLAC bit-depth transcoding"
				);
				b.AddCommand<SacdConvertCommand>("sacd-convert");
				b.AddCommand<DsdConvertCommand>("dsd-convert");
				b.AddCommand<FlacTranscodeCommand>("transcode");
			}
		);
}
