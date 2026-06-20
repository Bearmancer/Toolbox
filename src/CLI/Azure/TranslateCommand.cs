using System.ComponentModel;
using App.Services.Azure;
using Core;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Translate text to a different language")]
public class TranslateCommand(TranslateService service) : CommandBase<TranslateCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.TranslateAsync(s.Text, s.To, s.From, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [System.ComponentModel.Description("The text to translate.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [System.ComponentModel.Description("The language code to translate into (e.g., 'es').")]
        [CommandOption("--to <LANG>")]
        [DefaultValue("ja")]
        public string To { get; init; } = "ja";

        [System.ComponentModel.Description("The language code to translate from (default: 'en').")]
        [CommandOption("--from <LANG>")]
        [DefaultValue("en")]
        public string From { get; init; } = "en";
    }
}
