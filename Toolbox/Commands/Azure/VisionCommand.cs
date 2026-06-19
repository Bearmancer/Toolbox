using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Azure;
using Toolbox.Core;

namespace Toolbox.Commands.Azure;

public class VisionCommand : CommandBase<VisionCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await VisionService.AnalyzeAsync(s.File, s.Feature, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")] public string File { get; init; } = "";

        [CommandOption("--feature <FEATURE>")]
        [DefaultValue("tags")]
        public string Feature { get; init; } = "tags";
    }
}