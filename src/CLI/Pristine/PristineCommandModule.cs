using Spectre.Console.Cli;

namespace CLI.Pristine;

public static class PristineCommandModule
{
	public static void ConfigureCommands(IConfigurator cfg)
	{
		cfg.AddBranch(
			"pristine",
			branch =>
			{
				branch.SetDescription("Pristine Classical PASC downloader");
				branch.AddCommand<PristineLoginCommand>("login");
				branch.AddCommand<PristineDownloadCommand>("download");
			}
		);
	}
}
