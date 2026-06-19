using Spectre.Console.Cli;
using Toolbox.Core;
using Toolbox.Core.Screen;
using Toolbox.Sync;

namespace Toolbox.Commands.Sync;

public class LastFmSyncCommand : CommandBase<LastFmSyncCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(AppConfig.LastFmApiKey))
        {
            Ui.Error("Last.fm API key not configured");
            return 1;
        }

        if (string.IsNullOrEmpty(settings.User))
        {
            Ui.Error("Username is required");
            return 1;
        }

        Ui.Info("Syncing Last.fm scrobbles...");
        var result = await LastFmService.SyncScrobblesAsync(
            AppConfig.LastFmApiKey,
            settings.User,
            ct
        );

        Ui.Info($"Total scrobbles: {result.TotalCount}");
        Ui.Info($"New scrobbles: {result.NewCount}");
        if (result.OldestTimestamp.HasValue)
            Ui.Info($"Oldest: {result.OldestTimestamp:yyyy-MM-dd HH:mm}");
        if (result.NewestTimestamp.HasValue)
            Ui.Info($"Newest: {result.NewestTimestamp:yyyy-MM-dd HH:mm}");

        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--user <USER>")] public string? User { get; init; }
    }
}