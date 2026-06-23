using System.ComponentModel;
using Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Analyze images with Azure Computer Vision")]
public class VisionCommand(VisionService service) : AsyncCommand<VisionCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.AnalyzeAsync(
            s.File,
            s.Feature ?? "tags",
            language: s.Lang ?? "en",
            ct
        );
        AnsiConsole.MarkupLine(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The path to the image to analyze.")]
        [CommandArgument(0, "<file>")]
        public required string File { get; init; }

        [Description("The feature to extract (e.g., 'tags', 'objects', 'read').")]
        [CommandOption("--feature <FEATURE>")]
        public string? Feature { get; init; }

        [Description("The language for the tags/text (default: 'en').")]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
