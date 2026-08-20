using System.Text;
using Azure;
using Azure.AI.TextAnalytics;
using Core;
using ErrorOr;
using SerilogTracing;

namespace Services.Azure;

public class TextAnalyticsService(TextAnalyticsClient client)
{
	private const int MaxChars = 5_120;

	public Task<ErrorOr<string>> EntitiesAsync(
		string text,
		string language,
		CancellationToken ct
	) =>
		ExecuteAsync(
			"Entities",
			text,
			language,
			async ct2 =>
			{
				Response<CategorizedEntityCollection> r = await client.RecognizeEntitiesAsync(
					text,
					language,
					ct2
				);
				StringBuilder sb = new();
				foreach (CategorizedEntity e in r.Value)
					sb.AppendLine($"  [{e.Category}] {e.Text} (confidence={e.ConfidenceScore:F2})");
				return sb.Length > 0 ? sb.ToString() : "(no entities)";
			},
			ct
		);

	public Task<ErrorOr<string>> KeyPhrasesAsync(
		string text,
		string language,
		CancellationToken ct
	) =>
		ExecuteAsync(
			"KeyPhrases",
			text,
			language,
			async ct2 =>
			{
				Response<KeyPhraseCollection> r = await client.ExtractKeyPhrasesAsync(
					text,
					language,
					ct2
				);
				return string.Join(", ", r.Value);
			},
			ct
		);

	private async Task<ErrorOr<string>> ExecuteAsync(
		string operation,
		string text,
		string? hint,
		Func<CancellationToken, Task<string>> invoke,
		CancellationToken ct
	)
	{
		_ = hint;
		if (text.Length > MaxChars)
			return Errors.Validation.InvalidInput(
				nameof(text),
				$"Text length {text.Length} exceeds 5K"
			);

		using IDisposable scope = Telemetry.ForService(ServiceName.TextAnalytics);
		using LoggerActivity activity = Telemetry.StartActivity($"TextAnalytics.{operation}");
		try
		{
			var result = await invoke(ct);
			activity.Complete();
			return result;
		}
		catch (Exception ex)
		{
			Telemetry.Error(
				$"TextAnalytics: {operation.ToLowerInvariant()} error: {{Error}}",
				ex.Message
			);
			return Errors.TextAnalytics.ApiError(ex.Message);
		}
	}
}
