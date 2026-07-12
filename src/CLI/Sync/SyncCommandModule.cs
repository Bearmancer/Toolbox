using CLI.Sync.LastFm;
using CLI.Sync.YouTube;
using Spectre.Console.Cli;

namespace CLI.Sync;

public static class SyncCommandModule
{
	public static void ConfigureCommands(IConfigurator cfg) =>
		cfg.AddBranch(
			"sync",
			b =>
			{
				b.SetDescription("Sync remote data to local JSON files");
				b.AddCommand<SyncYoutubeCommand>("youtube");
				b.AddCommand<SyncLastFmCommand>("lastfm");
			}
		);
}
