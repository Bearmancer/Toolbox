using System.Text;
using Azure.AI.Vision.ImageAnalysis;
using Core;

namespace App.Services.Azure;

public class VisionService(ImageAnalysisClient client)
{
    public async Task<string> AnalyzeAsync(
        string filePath,
        string feature,
        string language = "en",
        CancellationToken ct = default
    )
    {
        using var activity = Telemetry.StartActivity("Vision.Analyze");

        var path = FileHelpers.ResolvePath(filePath);
        var bytes = FileHelpers.ReadChecked(path, Constants.VisionMaxBytes, "ComputerVision");

        var features = feature switch
        {
            "objects" => VisualFeatures.Objects,
            "read" => VisualFeatures.Read,
            "tags" => VisualFeatures.Tags,
            _ => VisualFeatures.Tags,
        };

        Telemetry.Debug("API request: {Service}.{Operation} {Detail}", "Vision", "Analyze", feature);
        var startTime = DateTime.UtcNow;
        var result = await client.AnalyzeAsync(
            BinaryData.FromBytes(bytes),
            features,
            new ImageAnalysisOptions { Language = language },
            ct
        );
        Telemetry.Debug("API response: {Service} {StatusCode} {ElapsedMs:F0}ms", "Vision", 200, (DateTime.UtcNow - startTime).TotalMilliseconds);

        var sb = new StringBuilder();
        if (feature is "tags" or "all" && result.Value.Tags is { } tags)
        {
            sb.AppendLine("Tags:");
            foreach (var t in tags.Values.Take(10))
                sb.AppendLine($"  {t.Name} ({t.Confidence:F2})");
        }

        if (feature is "objects" && result.Value.Objects is { } objs)
            sb.AppendLine($"Objects detected: {objs.Values.Count}");
        if (feature is "read" && result.Value.Read is { } read)
        {
            sb.AppendLine("Text (OCR):");
            foreach (var block in read.Blocks)
                foreach (var line in block.Lines)
                    sb.AppendLine($"  {line.Text}");
        }

        activity.Complete();
        return sb.ToString();
    }
}
