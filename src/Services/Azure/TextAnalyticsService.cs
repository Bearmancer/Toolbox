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

	public Task<ErrorOr<string>> SentimentAsync(
		string text,
		string language,
		CancellationToken ct,
		bool opinionMining = false
	) =>
		ExecuteAsync(
			"Sentiment",
			text,
			language,
			async ct2 =>
			{
				Response<DocumentSentiment> r = await client.AnalyzeSentimentAsync(
					text,
					language,
					new AnalyzeSentimentOptions { IncludeOpinionMining = opinionMining },
					ct2
				);
				StringBuilder sb = new();
				sb.AppendLine($"Sentiment: {r.Value.Sentiment}");
				sb.AppendLine(
					$"Scores: positive={r.Value.ConfidenceScores.Positive:F2}, neutral={r.Value.ConfidenceScores.Neutral:F2}, negative={r.Value.ConfidenceScores.Negative:F2}"
				);
				if (opinionMining && r.Value.Sentences is { } sentences)
					foreach (SentenceSentiment sentence in sentences)
						if (sentence.Opinions is { Count: > 0 } opinions)
							foreach (SentenceOpinion opinion in opinions)
							{
								var aspect = string.IsNullOrWhiteSpace(opinion.Target.Text)
									? "(no aspect)"
									: opinion.Target.Text;
								var assessment = opinion.Assessments is { Count: > 0 }
									? string.Join(
										", ",
										opinion.Assessments.Select(a => $"{a.Text} ({a.Sentiment})")
									)
									: "(no assessment)";
								sb.AppendLine($"  Aspect: {aspect} -> {assessment}");
							}
				return sb.ToString();
			},
			ct
		);

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

	public Task<ErrorOr<string>> DetectLanguageAsync(
		string text,
		string countryHint,
		CancellationToken ct
	) =>
		ExecuteAsync(
			"DetectLanguage",
			text,
			countryHint,
			async ct2 =>
			{
				Response<DetectedLanguage> r = await client.DetectLanguageAsync(
					text,
					countryHint,
					ct2
				);
				return $"{r.Value.Name} ({r.Value.Iso6391Name}, confidence={r.Value.ConfidenceScore:F2})";
			},
			ct
		);

	public Task<ErrorOr<string>> PiiAsync(
		string text,
		string language,
		string? domain,
		CancellationToken ct
	)
	{
		if (!string.IsNullOrWhiteSpace(domain))
		{
			var normalized = domain.Trim().ToLowerInvariant();
			switch (normalized)
			{
				case "phi":
				case "none":
					break;
				default:
					return Task.FromResult<ErrorOr<string>>(
						Errors.Validation.InvalidInput(
							nameof(domain),
							$"Unknown PII domain '{domain}'. Valid values: phi, none"
						)
					);
			}
		}

		return ExecuteAsync(
			"Pii",
			text,
			language,
			async ct2 =>
			{
				RecognizePiiEntitiesOptions options = new();
				if (!string.IsNullOrWhiteSpace(domain))
				{
					var normalized = domain.Trim().ToLowerInvariant();
					switch (normalized)
					{
						case "phi":
							options.DomainFilter = PiiEntityDomain.ProtectedHealthInformation;
							break;
						case "none":
							options.DomainFilter = PiiEntityDomain.None;
							break;
					}
				}
				Response<PiiEntityCollection> r = await client.RecognizePiiEntitiesAsync(
					text,
					language,
					options,
					ct2
				);
				StringBuilder sb = new();
				if (domain is { })
					sb.AppendLine($"Domain: {domain}");
				foreach (PiiEntity e in r.Value)
					sb.AppendLine($"  [{e.Category}] {e.Text}");
				return sb.Length > 0 ? sb.ToString() : "(no PII detected)";
			},
			ct
		);
	}

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
