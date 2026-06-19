using Azure;
using Azure.AI.DocumentIntelligence;
using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Reader;

public static class OcrService
{
    public static async Task<OcrResult?> AnalyzeAsync(
        string filePath,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Reader);
        using var op = Log.BeginOperation("OcrService.Analyze");

        if (string.IsNullOrEmpty(AppConfig.Endpoint) || string.IsNullOrEmpty(AppConfig.Key))
        {
            Log.Emit(new ErrorOccurred("Azure Document Intelligence credentials not configured", "OcrService.Analyze"));
            return null;
        }

        if (!File.Exists(filePath))
        {
            Log.Emit(new ErrorOccurred($"File not found: {filePath}", "OcrService.Analyze"));
            return null;
        }

        var client = AzureClients.CreateDocumentIntelligenceClient();
        var bytes = await File.ReadAllBytesAsync(filePath, ct);

        Log.Emit(new ApiRequested("DocumentIntelligence", "Analyze", Path.GetFileName(filePath)));
        var startTime = DateTime.UtcNow;

        try
        {
            var operation = await client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                new AnalyzeDocumentOptions("prebuilt-read", BinaryData.FromBytes(bytes)),
                ct
            );

            Log.Emit(new ApiResponded("DocumentIntelligence", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

            var result = operation.Value;
            var blocks =
                result
                    .Pages?.SelectMany(p => p.Lines ?? [])
                    .Select(line => new OcrBlock(
                        line.Content,
                        OcrBlockType.Text,
                        line.Polygon is not null && line.Polygon.Count >= 4
                            ? new BoundingBox(
                                (int)line.Polygon[0],
                                (int)line.Polygon[1],
                                (int)(line.Polygon[2] - line.Polygon[0]),
                                (int)(line.Polygon[5] - line.Polygon[1])
                            )
                            : null
                    ))
                    .ToList()
                ?? [];

            var confidence = blocks.Count > 0 ? 95 : 0;

            op.Complete();
            return new OcrResult(result.Content, confidence, blocks);
        }
        catch (RequestFailedException ex)
        {
            Log.Emit(ErrorOccurred.From(ex, "OCR analysis failed"));
            op.Fail();
            return null;
        }
    }
}