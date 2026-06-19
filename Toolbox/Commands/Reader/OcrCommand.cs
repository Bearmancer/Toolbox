using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Core;
using Toolbox.Reader;

namespace Toolbox.Commands.Reader;

[Description("Perform OCR on an image or PDF file using Azure Document Intelligence")]
public class OcrCommand : CommandBase<OcrCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        var result = await OcrService.AnalyzeAsync(settings.File, ct);

        if (result is null)
        {
            Ui.Error("OCR analysis failed");
            return 1;
        }

        Ui.Info($"Confidence: {result.Confidence}%");
        Ui.Info($"Blocks: {result.Blocks?.Count ?? 0}");
        Ui.NewLine();
        Ui.Info("Text:");
        Ui.Info(result.Text.Length > 1000 ? result.Text[..1000] + "..." : result.Text);

        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("Path to the image or PDF file to process")]
        public string File { get; init; } = "";
    }
}