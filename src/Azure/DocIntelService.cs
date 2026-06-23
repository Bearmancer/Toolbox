using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using Core;

namespace App.Services.Azure;

public class DocIntelService(DocumentIntelligenceClient client)
{
    public static IReadOnlyDictionary<string, string> Models =>
        new Dictionary<string, string>
        {
            ["prebuilt-read"] = "prebuilt-read",
            ["prebuilt-layout"] = "prebuilt-layout",
            ["prebuilt-invoice"] = "prebuilt-invoice",
            ["prebuilt-receipt"] = "prebuilt-receipt",
        };

    public async Task<string> AnalyzeAsync(
        string filePath,
        string modelId,
        CancellationToken ct = default
    )
    {
        using var activity = Telemetry.StartActivity("DocumentIntelligence.Analyze");

        var path = FileHelpers.ResolvePath(filePath);
        var bytes = FileHelpers.ReadChecked(
            path,
            Constants.DocIntelMaxBytes,
            "DocumentIntelligence"
        );

        Telemetry.Debug("API request: {Service}.{Operation} {Detail}", "DocumentIntelligence", "AnalyzeDocument", modelId);
        var startTime = DateTime.UtcNow;
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            new AnalyzeDocumentOptions(modelId, BinaryData.FromBytes(bytes)),
            ct
        );
        Telemetry.Debug(
            "API response: {Service} {StatusCode} {ElapsedMs:F0}ms",
            "DocumentIntelligence",
            200,
            (DateTime.UtcNow - startTime).TotalMilliseconds
        );

        var result = operation.Value;
        if (result.Pages.Count is 0)
        {
            Telemetry.Error("Model returned no pages");
            throw new InvalidOperationException("Model returned no pages");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Pages: {result.Pages.Count}");
        sb.AppendLine($"Model: {modelId}");
        sb.AppendLine("---");
        sb.AppendLine(result.Content);

        activity.Complete();
        return sb.ToString();
    }
}
