using CLI.Google.YouTube;
using Spectre.Console.Cli;

namespace CLI.Google;

public static class GoogleCommandModule
{
    public static void ConfigureCommands(IConfigurator cfg) =>
        cfg.AddBranch("google", b =>
        {
            b.AddCommand<SortPlaylistCommand>("sort-playlist");
            b.AddCommand<SyncYouTubeCommand>("sync");
        });
}
