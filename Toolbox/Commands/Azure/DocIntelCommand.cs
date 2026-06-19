using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Azure;
using Toolbox.Core;
using Toolbox.Core.Screen;

namespace Toolbox.Commands.Azure;

[Description("Analyze documents using Azure Document Intelligence")]
public class DocIntelCommand : CommandBase<DocIntelCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        var modelId = settings.Model;
        if (!DocIntelService.Models.ContainsKey(modelId))
            throw new ArgumentException(
                $"Unknown model '{modelId}'. Valid: {string.Join(", ", DocIntelService.Models.Keys)}"
            );
        var result = await DocIntelService.AnalyzeAsync(settings.File, modelId, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("Path to the document file to analyze")]
        public string File { get; init; } = "";

        [CommandOption("--model <MODEL>")]
        [DefaultValue("prebuilt-read")]
        [Description(
            "Document Intelligence model to use (prebuilt-read, prebuilt-layout, prebuilt-invoice, prebuilt-receipt)"
        )]
        public string Model { get; init; } = "prebuilt-read";
    }
}