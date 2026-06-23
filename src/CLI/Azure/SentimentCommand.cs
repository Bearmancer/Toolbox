using System.ComponentModel;
using App.Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Analyze the sentiment of text")]
public class SentimentCommand(TextAnalyticsService service) : AsyncCommand<SentimentCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.SentimentAsync(s.Text, language: s.Lang ?? "en", ct);
        AnsiConsole.MarkupLine(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The text to analyze for sentiment.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [Description("The language of the text (e.g., 'en').")]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
