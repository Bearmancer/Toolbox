using Spectre.Console.Cli;
using Toolbox.Azure;
using Toolbox.Core;
using Toolbox.Core.Screen;

namespace Toolbox.Commands.Azure;

public class PiiCommand : CommandBase<PiiCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await TextAnalyticsService.PiiAsync(s.Text, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<text>")] public string Text { get; init; } = "";
    }
}