using System.ComponentModel;
using App.Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Detect the language of a given text")]
public class LanguageCommand(TextAnalyticsService service) : AsyncCommand<LanguageCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.DetectLanguageAsync(s.Text, countryHint: s.Lang ?? "us", ct);
        AnsiConsole.MarkupLine(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The text to analyze.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [Description("Country hint to improve detection (e.g., 'us').")]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
