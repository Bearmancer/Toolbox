using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using Core;

namespace Services.Azure;

public class DocIntelService(DocumentIntelligenceClient client)
{
    private const int MaxBytes = 500_000_000;

    public async Task<string> AnalyzeAsync(string filePath, string modelId, CancellationToken ct)
    {
        var path = PathResolver.ResolveInput(filePath);
        var bytes = PathResolver.ReadChecked(path, MaxBytes, "DocumentIntelligence");

        using var _ = Telemetry.ForService(ServiceName.DocIntel);
        using var activity = Telemetry.StartActivity("DocumentIntelligence.Analyze");
        var operation = await client
            .AnalyzeDocumentAsync(
                WaitUntil.Completed,
                new AnalyzeDocumentOptions(modelId, BinaryData.FromBytes(bytes)),
                ct
            );
        activity.Complete();

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

        return sb.ToString();
    }
}
