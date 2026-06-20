using App.Services.Azure;
using Core;
using Spectre.Console.Cli;

namespace CLI.Azure;

[System.ComponentModel.Description("Extract key phrases from text")]
public class PhrasesCommand(TextAnalyticsService service) : CommandBase<PhrasesCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.KeyPhrasesAsync(s.Text, language: s.Lang ?? "en", ct: ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [System.ComponentModel.Description("The text to extract key phrases from.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [System.ComponentModel.Description("The language of the text (e.g., 'en').")]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
