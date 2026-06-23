using System.ComponentModel;
using Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Analyze documents using Azure Document Intelligence")]
public class DocIntelCommand(DocIntelService service) : AsyncCommand<DocIntelCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.AnalyzeAsync(s.File, s.Model, ct);
        AnsiConsole.MarkupLine(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The path to the document file (e.g., PDF, image).")]
        [CommandArgument(0, "<file>")]
        public required string File { get; init; }

        [Description("The model to use (default: prebuilt-read).")]
        [CommandOption("--model <MODEL>")]
        [DefaultValue("prebuilt-read")]
        public string Model { get; init; } = "prebuilt-read";
    }
}
