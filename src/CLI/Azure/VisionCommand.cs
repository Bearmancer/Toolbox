using App.Services.Azure;
using Core;
using Spectre.Console.Cli;

namespace CLI.Azure;

[System.ComponentModel.Description("Analyze images with Azure Computer Vision")]
public class VisionCommand(VisionService service) : CommandBase<VisionCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.AnalyzeAsync(
            s.File,
            s.Feature ?? "tags",
            language: s.Lang ?? "en",
            ct: ct
        );
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [System.ComponentModel.Description("The path to the image to analyze.")]
        [CommandArgument(0, "<file>")]
        public required string File { get; init; }

        [System.ComponentModel.Description(
            "The feature to extract (e.g., 'tags', 'objects', 'read')."
        )]
        [CommandOption("--feature <FEATURE>")]
        public string? Feature { get; init; }

        [System.ComponentModel.Description("The language for the tags/text (default: 'en').")]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
