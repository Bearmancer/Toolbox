using System.ComponentModel;
using Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Translate text to a different language")]
public class TranslateCommand(TranslateService service) : AsyncCommand<TranslateCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var results = await service.TranslateBatchAsync([s.Text], s.To, ct);
        AnsiConsole.MarkupLine(results[0].TranslatedText);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The text to translate.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [Description("The language code to translate into (e.g., 'es').")]
        [CommandOption("--to <LANG>")]
        [DefaultValue("ja")]
        public string To { get; init; } = "ja";

        [Description("The language code to translate from (default: 'en').")]
        [CommandOption("--from <LANG>")]
        [DefaultValue("en")]
        public string From { get; init; } = "en";
    }
}
