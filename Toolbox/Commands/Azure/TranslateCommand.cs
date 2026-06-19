using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Azure;
using Toolbox.Core;

namespace Toolbox.Commands.Azure;

public class TranslateCommand : CommandBase<TranslateCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await TranslateService.TranslateAsync(s.Text, s.To, s.From, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<text>")] public string Text { get; init; } = "";

        [CommandOption("--to <LANG>")]
        [DefaultValue("ja")]
        public string To { get; init; } = "ja";

        [CommandOption("--from <LANG>")]
        [DefaultValue("en")]
        public string From { get; init; } = "en";
    }
}