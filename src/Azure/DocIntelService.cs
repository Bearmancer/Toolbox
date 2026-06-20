using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using Core.Logging;

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
        using var op = Log.BeginOperation("DocumentIntelligence.Analyze");

        var path = FileHelpers.ResolvePath(filePath);
        var bytes = FileHelpers.ReadChecked(
            path,
            Constants.DocIntelMaxBytes,
            "DocumentIntelligence"
        );

        Log.Emit(new ApiRequested("DocumentIntelligence", "AnalyzeDocument", modelId));
        var startTime = DateTime.UtcNow;
        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            new AnalyzeDocumentOptions(modelId, BinaryData.FromBytes(bytes)),
            ct
        );
        Log.Emit(
            new ApiResponded(
                "DocumentIntelligence",
                200,
                (DateTime.UtcNow - startTime).TotalMilliseconds
            )
        );

        var result = operation.Value;
        if (result.Pages.Count is 0)
        {
            op.Fail();
            throw new InvalidOperationException("Model returned no pages");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Pages: {result.Pages.Count}");
        sb.AppendLine($"Model: {modelId}");
        sb.AppendLine("---");
        sb.AppendLine(result.Content);

        op.Complete();
        return sb.ToString();
    }
}
