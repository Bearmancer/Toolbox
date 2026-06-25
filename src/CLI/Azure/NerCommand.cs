using System.ComponentModel;
using Core;
using Services.Azure;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description(
    "Extract named entities from text using Azure AI Language. "
    + "Identifies people, organizations, locations, dates, quantities, and more."
)]
public class NerCommand(TextAnalyticsService service) : AsyncCommand<NerCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.EntitiesAsync(s.Text, s.Lang ?? "en", ct);
        Telemetry.Info("{Result}", result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The text to extract named entities from.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [Description(
            "Language of the input text (BCP-47 format, e.g. 'en', 'es', 'fr', 'de'). "
            + "Specifying the language improves entity detection accuracy. (default: en)"
        )]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
