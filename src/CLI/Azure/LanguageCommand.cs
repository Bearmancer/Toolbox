using App.Services.Azure;
using Core;
using Spectre.Console.Cli;

namespace CLI.Azure;

[System.ComponentModel.Description("Detect the language of a given text")]
public class LanguageCommand(TextAnalyticsService service) : CommandBase<LanguageCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.DetectLanguageAsync(s.Text, countryHint: s.Lang ?? "us", ct: ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [System.ComponentModel.Description("The text to analyze.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [System.ComponentModel.Description("Country hint to improve detection (e.g., 'us').")]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
