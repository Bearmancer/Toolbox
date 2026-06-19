using Spectre.Console.Cli;
using Toolbox.Azure;
using Toolbox.Core;

namespace Toolbox.Commands.Azure;

public class PhrasesCommand : CommandBase<PhrasesCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await TextAnalyticsService.KeyPhrasesAsync(s.Text, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<text>")] public string Text { get; init; } = "";
    }
}