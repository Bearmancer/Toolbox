using System.Text;
using Azure.AI.Vision.ImageAnalysis;
using Core;

namespace Services.Azure;

public class VisionService(ImageAnalysisClient client)
{
    private const int MaxBytes = 20_000_000;

    public async Task<string> AnalyzeAsync(
        string filePath,
        string feature,
        string language,
        CancellationToken ct
    )
    {
        var path = InputFile.ResolvePath(filePath);
        var bytes = InputFile.ReadChecked(path, MaxBytes, "ComputerVision");

        var features = feature switch
        {
            "objects" => VisualFeatures.Objects,
            "read" => VisualFeatures.Read,
            "tags" => VisualFeatures.Tags,
            "all" => VisualFeatures.Tags | VisualFeatures.Objects | VisualFeatures.Read,
            _ => VisualFeatures.Tags,
        };

        var result = await client
            .AnalyzeAsync(
                BinaryData.FromBytes(bytes),
                features,
                new ImageAnalysisOptions { Language = language },
                ct
            )
            .WithTelemetry("Vision", "Vision.Analyze");

        var sb = new StringBuilder();
        if (feature is "tags" or "all" && result.Value.Tags is { } tags)
        {
            sb.AppendLine("Tags:");
            foreach (var t in tags.Values.Take(10))
                sb.AppendLine($"  {t.Name} ({t.Confidence:F2})");
        }

        if (feature is "objects" or "all" && result.Value.Objects is { } objs)
            sb.AppendLine($"Objects detected: {objs.Values.Count}");
        if (feature is "read" or "all" && result.Value.Read is { } read)
        {
            sb.AppendLine("Text (OCR):");
            foreach (var block in read.Blocks)
            foreach (var line in block.Lines)
                sb.AppendLine($"  {line.Text}");
        }

        return sb.ToString();
    }
}
