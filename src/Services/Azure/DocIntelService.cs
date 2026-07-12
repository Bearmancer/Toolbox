using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using Core;
using ErrorOr;
using SerilogTracing;

namespace Services.Azure;

public class DocIntelService(DocumentIntelligenceClient client)
{
	private const int MaxBytes = 500_000_000;

	public async Task<ErrorOr<string>> AnalyzeAsync(
		string filePath,
		string modelId,
		CancellationToken ct
	)
	{
		var path = PathResolver.ResolveInput(filePath);
		var bytes = PathResolver.ReadChecked(path, MaxBytes, "DocumentIntelligence");

		using IDisposable _ = Telemetry.ForService(ServiceName.DocIntel);
		using LoggerActivity activity = Telemetry.StartActivity("DocumentIntelligence.Analyze");
		try
		{
			Operation<AnalyzeResult>? operation = await client.AnalyzeDocumentAsync(
				WaitUntil.Completed,
				new AnalyzeDocumentOptions(modelId, BinaryData.FromBytes(bytes)),
				ct
			);
			activity.Complete();

			AnalyzeResult result = operation.Value;
			if (result.Pages.Count is 0)
			{
				Telemetry.Error("Model returned no pages");
				return Errors.DocIntel.ApiError("Model returned no pages");
			}

			var sb = new StringBuilder();
			sb.AppendLine($"Pages: {result.Pages.Count}");
			sb.AppendLine($"Model: {modelId}");
			sb.AppendLine("---");
			sb.AppendLine(result.Content);

			return sb.ToString();
		}
		catch (Exception ex)
		{
			return Errors.DocIntel.ApiError(ex.Message);
		}
	}
}
