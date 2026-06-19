using System.Text;
using Azure.AI.Vision.ImageAnalysis;
using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Azure;

public static class VisionService
{
    public static async Task<string> AnalyzeAsync(
        string filePath,
        string feature,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Azure);
        using var op = Log.BeginOperation("Vision.Analyze");

        var path = FileHelpers.ResolvePath(filePath);
        var bytes = FileHelpers.ReadChecked(path, Constants.VisionMaxBytes, "ComputerVision");

        var features = feature switch
        {
            "objects" => VisualFeatures.Objects,
            "read"    => VisualFeatures.Read,
            "tags"    => VisualFeatures.Tags,
            _         => VisualFeatures.Tags,
        };

        var client = AzureClients.CreateImageAnalysisClient();

        Log.Emit(new ApiRequested("Vision", "Analyze", feature));
        var startTime = DateTime.UtcNow;
        var result = await client.AnalyzeAsync(
            BinaryData.FromBytes(bytes),
            features,
            new ImageAnalysisOptions { Language = "en" },
            ct
        );
        Log.Emit(new ApiResponded("Vision", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        var sb = new StringBuilder();
        if (feature is "tags" or "" or "all" && result.Value.Tags is { } tags)
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

        op.Complete();
        return sb.ToString();
    }
}