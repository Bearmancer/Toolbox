using System.ComponentModel;
using Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Detect Personally Identifiable Information (PII) in text")]
public class PiiCommand(TextAnalyticsService service) : AsyncCommand<PiiCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.PiiAsync(s.Text, language: s.Lang ?? "en", s.Domain, ct);
        AnsiConsole.MarkupLine(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The text to scan for Personally Identifiable Information.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [Description("The language of the text (e.g., 'en').")]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }

        [Description("Restrict PII detection to a specific domain (e.g., 'phi' for healthcare, 'none' for no domain filter).")]
        [CommandOption("--domain <DOMAIN>")]
        public string? Domain { get; init; }
    }
}
