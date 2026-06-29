using System.ComponentModel;
using Core;
using Services.Azure;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description(
    "Extract key phrases from text using Azure AI Language. "
        + "Returns the most relevant phrases that summarize the main topics of the input."
)]
public class PhrasesCommand(TextAnalyticsService service) : AsyncCommand<PhrasesCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.KeyPhrasesAsync(s.Text, s.Lang ?? "en", ct);
        return result.Match(
            success => { Console.WriteLine(success); return 0; },
            errors => { Console.Error.WriteLine(errors[0].Description); return 1; }
        );
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The text to extract key phrases from.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [Description(
            "Language of the input text (BCP-47 format, e.g. 'en', 'es', 'fr', 'de'). "
                + "Key phrase extraction is language-aware; specifying improves results. (default: en)"
        )]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
