using System.ComponentModel;
using App.Services.Azure;
using Core;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Analyze documents using Azure Document Intelligence")]
public class DocIntelCommand(DocIntelService service) : CommandBase<DocIntelCommand.Settings>
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
                $"Invalid model. Allowed: {string.Join(", ", DocIntelService.Models.Keys)}"
            );
        var result = await service.AnalyzeAsync(settings.File, modelId, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [System.ComponentModel.Description("The path to the document file (e.g., PDF, image).")]
        [CommandArgument(0, "<file>")]
        public required string File { get; init; }

        [System.ComponentModel.Description("The model to use (default: prebuilt-read).")]
        [CommandOption("--model <MODEL>")]
        [DefaultValue("prebuilt-read")]
        public string Model { get; init; } = "prebuilt-read";
    }
}
