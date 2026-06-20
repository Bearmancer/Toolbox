using App.Services.Azure;
using Core;
using Spectre.Console.Cli;

namespace CLI.Azure;

[System.ComponentModel.Description("Analyze the sentiment of text")]
public class SentimentCommand(TextAnalyticsService service) : CommandBase<SentimentCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.SentimentAsync(s.Text, language: s.Lang ?? "en", ct: ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [System.ComponentModel.Description("The text to analyze for sentiment.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [System.ComponentModel.Description("The language of the text (e.g., 'en').")]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
