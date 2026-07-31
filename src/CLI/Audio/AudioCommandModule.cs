using Spectre.Console.Cli;

namespace CLI.Audio;

public static class AudioCommandModule
{
	public static void ConfigureCommands(IConfigurator cfg) =>
		cfg.AddBranch(
			"audio",
			b =>
			{
				b.SetDescription("Audio conversion: SACD ISO extraction and DSD→FLAC");
				b.AddCommand<SacdConvertCommand>("sacd-convert");
				b.AddCommand<DsdConvertCommand>("dsd-convert");
			}
		);
}
