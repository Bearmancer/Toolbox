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

	public async Task<ErrorOr<string>> SentimentAsync(
		string text,
		string language,
		CancellationToken ct,
		bool opinionMining = false
	)
	{
		if (text.Length > MaxChars)
			return Errors.Validation.InvalidInput(
				nameof(text),
				$"Text length {text.Length} exceeds 5K"
			);

		using IDisposable _ = Telemetry.ForService(ServiceName.TextAnalytics);
		using LoggerActivity activity = Telemetry.StartActivity("TextAnalytics.Sentiment");
		try
		{
			Response<DocumentSentiment>? result = await client.AnalyzeSentimentAsync(
				text,
				language,
				new AnalyzeSentimentOptions { IncludeOpinionMining = opinionMining },
				ct
			);
			activity.Complete();

			var sb = new StringBuilder();
			sb.AppendLine($"Sentiment: {result.Value.Sentiment}");
			sb.AppendLine(
				$"Scores: positive={result.Value.ConfidenceScores.Positive:F2}, neutral={result.Value.ConfidenceScores.Neutral:F2}, negative={result.Value.ConfidenceScores.Negative:F2}"
			);

			if (opinionMining && result.Value.Sentences is { } sentences)
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
		}
		catch (Exception ex)
		{
			Telemetry.Error("TextAnalytics: sentiment error for text length={Length} lang={Language}: {Error}", text.Length, language, ex.Message);
			return Errors.TextAnalytics.ApiError(ex.Message);
		}
	}

	public async Task<ErrorOr<string>> EntitiesAsync(
		string text,
		string language,
		CancellationToken ct
	)
	{
		if (text.Length > MaxChars)
			return Errors.Validation.InvalidInput(
				nameof(text),
				$"Text length {text.Length} exceeds 5K"
			);

		using IDisposable _ = Telemetry.ForService(ServiceName.TextAnalytics);
		using LoggerActivity activity = Telemetry.StartActivity("TextAnalytics.Entities");
		try
		{
			Response<CategorizedEntityCollection>? result = await client.RecognizeEntitiesAsync(
				text,
				language,
				ct
			);
			activity.Complete();

			var sb = new StringBuilder();
			foreach (CategorizedEntity e in result.Value)
				sb.AppendLine($"  [{e.Category}] {e.Text} (confidence={e.ConfidenceScore:F2})");

			return sb.Length > 0 ? sb.ToString() : "(no entities)";
		}
		catch (Exception ex)
		{
			Telemetry.Error("TextAnalytics: entities error for text length={Length} lang={Language}: {Error}", text.Length, language, ex.Message);
			return Errors.TextAnalytics.ApiError(ex.Message);
		}
	}

	public async Task<ErrorOr<string>> KeyPhrasesAsync(
		string text,
		string language,
		CancellationToken ct
	)
	{
		if (text.Length > MaxChars)
			return Errors.Validation.InvalidInput(
				nameof(text),
				$"Text length {text.Length} exceeds 5K"
			);

		using IDisposable _ = Telemetry.ForService(ServiceName.TextAnalytics);
		using LoggerActivity activity = Telemetry.StartActivity("TextAnalytics.KeyPhrases");
		try
		{
			Response<KeyPhraseCollection>? result = await client.ExtractKeyPhrasesAsync(
				text,
				language,
				ct
			);
			activity.Complete();

			return string.Join(", ", result.Value);
		}
		catch (Exception ex)
		{
			Telemetry.Error("TextAnalytics: key phrases error for text length={Length} lang={Language}: {Error}", text.Length, language, ex.Message);
			return Errors.TextAnalytics.ApiError(ex.Message);
		}
	}

	public async Task<ErrorOr<string>> DetectLanguageAsync(
		string text,
		string countryHint,
		CancellationToken ct
	)
	{
		if (text.Length > MaxChars)
			return Errors.Validation.InvalidInput(
				nameof(text),
				$"Text length {text.Length} exceeds 5K"
			);

		using IDisposable _ = Telemetry.ForService(ServiceName.TextAnalytics);
		using LoggerActivity activity = Telemetry.StartActivity("TextAnalytics.DetectLanguage");
		try
		{
			Response<DetectedLanguage>? result = await client.DetectLanguageAsync(
				text,
				countryHint,
				ct
			);
			activity.Complete();

			return $"{result.Value.Name} ({result.Value.Iso6391Name}, confidence={result.Value.ConfidenceScore:F2})";
		}
		catch (Exception ex)
		{
			Telemetry.Error("TextAnalytics: detect language error for countryHint={CountryHint}: {Error}", countryHint, ex.Message);
			return Errors.TextAnalytics.ApiError(ex.Message);
		}
	}

	public async Task<ErrorOr<string>> PiiAsync(
		string text,
		string language,
		string? domain,
		CancellationToken ct
	)
	{
		if (text.Length > MaxChars)
			return Errors.Validation.InvalidInput(
				nameof(text),
				$"Text length {text.Length} exceeds 5K"
			);

		var options = new RecognizePiiEntitiesOptions();
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
				default:
					return Errors.Validation.InvalidInput(
						nameof(domain),
						$"Unknown PII domain '{domain}'. Valid values: phi, none"
					);
			}
		}

		using IDisposable _ = Telemetry.ForService(ServiceName.TextAnalytics);
		using LoggerActivity activity = Telemetry.StartActivity("TextAnalytics.Pii");
		try
		{
			Response<PiiEntityCollection>? result = await client.RecognizePiiEntitiesAsync(
				text,
				language,
				options,
				ct
			);
			activity.Complete();

			var sb = new StringBuilder();
			if (domain is { })
				sb.AppendLine($"Domain: {domain}");

			foreach (PiiEntity e in result.Value)
				sb.AppendLine($"  [{e.Category}] {e.Text}");

			return sb.Length > 0 ? sb.ToString() : "(no PII detected)";
		}
		catch (Exception ex)
		{
			Telemetry.Error("TextAnalytics: PII error for text length={Length} lang={Language}: {Error}", text.Length, language, ex.Message);
			return Errors.TextAnalytics.ApiError(ex.Message);
		}
	}
}
