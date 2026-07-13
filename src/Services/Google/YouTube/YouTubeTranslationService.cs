using Core;
using ErrorOr;
using Services.Azure;

namespace Services.Google.YouTube;

public class YouTubeTranslationService(TranslateService translateService)
{
	private const int MaxTextsPerCall = 70;
	private const int MaxCharsPerCall = 30000;

	public async Task<ErrorOr<TranslateResult>> TranslateVideosAsync(
		List<YouTubeVideo> videos,
		CancellationToken ct,
		Func<IReadOnlyList<YouTubeVideo>, CancellationToken, Task>? checkpointAsync = null
	)
	{
		List<TranslationTarget> targets = CollectTranslationTargets(videos);
		if (targets.Count == 0)
			return new TranslateResult(videos, 0);

		var totalChars = targets.Sum(t => t.Text.Length);

		return await ErrorOrFactory
			.From(targets)
			.Then(BuildTranslationBatches)
			.Then(batches =>
			{
				var unchangedCount =
					videos.Count
					- videos.Count(v =>
						v.TranslatedTitle is null || v.TranslatedDescription is null
					);
				Telemetry.Debug(
					"Translate: {Need}/{Total} videos need text completion, {Unchanged} already complete | {Chars:N0} chars ({Batches} {BatchWord})",
					videos.Count - unchangedCount,
					videos.Count,
					unchangedCount,
					totalChars,
					batches.Count,
					batches.Count == 1 ? "batch" : "batches"
				);
				return batches;
			})
			.ThenAsync(batchPlan => ExecuteTranslationBatchesAsync(batchPlan, translateService, ct))
			.Then(execResult => ApplyTranslationResults(videos, execResult, totalChars))
			.ThenAsync(async result =>
			{
				if (checkpointAsync is { })
					await checkpointAsync(result.Videos, ct);
				return result;
			});
	}

	private static List<TranslationTarget> CollectTranslationTargets(List<YouTubeVideo> videos)
	{
		var targets = new List<TranslationTarget>();
		foreach (
			(var videoIndex, YouTubeVideo video) in videos
				.Select((video, index) => (index, video))
				.ToList()
		)
		{
			if (video.TranslatedTitle is null)
				targets.Add(new TranslationTarget(videoIndex, TranslationField.Title, video.Title));

			if (video.TranslatedDescription is null)
			{
				if (video.Description.Length > 0)
					targets.Add(
						new TranslationTarget(
							videoIndex,
							TranslationField.Description,
							video.Description
						)
					);
				else
					videos[videoIndex] = video with { TranslatedDescription = "" };
			}
		}

		return targets;
	}

	private static List<List<TranslationTarget>> BuildTranslationBatches(
		List<TranslationTarget> targets
	)
	{
		var batches = new List<List<TranslationTarget>>();
		var currentBatch = new List<TranslationTarget>();
		var currentCharCount = 0;

		foreach (TranslationTarget target in targets)
		{
			if (
				currentBatch.Count > 0
				&& (
					currentBatch.Count >= MaxTextsPerCall
					|| currentCharCount + target.Text.Length > MaxCharsPerCall
				)
			)
			{
				batches.Add([.. currentBatch]);
				currentBatch.Clear();
				currentCharCount = 0;
			}

			currentBatch.Add(target);
			currentCharCount += target.Text.Length;
		}

		if (currentBatch.Count > 0)
			batches.Add([.. currentBatch]);

		return batches;
	}

	private static async Task<ErrorOr<List<BatchApiResult>>> ExecuteTranslationBatchesAsync(
		List<List<TranslationTarget>> batches,
		TranslateService translateService,
		CancellationToken ct
	)
	{
		var allResults = new List<BatchApiResult>();
		var batchIndex = 0;

		foreach (List<TranslationTarget> batch in batches)
		{
			ct.ThrowIfCancellationRequested();
			batchIndex++;

			var batchChars = batch.Sum(t => t.Text.Length);
			Telemetry.Debug(
				"Translate: [{Batch}/{TotalBatches}] → Azure ({Targets} fields, {Chars:N0} chars)",
				batchIndex,
				batches.Count,
				batch.Count,
				batchChars
			);

			ErrorOr<List<TranslationResult>> batchResult =
				await translateService.TranslateBatchAsync(
					[.. batch.Select(t => t.Text)],
					"en",
					ct
				);

			if (batchResult.IsError)
				return batchResult.FirstError;

			allResults.AddRange(batch.Zip(batchResult.Value, (t, r) => new BatchApiResult(t, r)));
		}

		return allResults;
	}

	private static TranslateResult ApplyTranslationResults(
		List<YouTubeVideo> videos,
		List<BatchApiResult> results,
		int totalChars
	)
	{
		var translatedCount = 0;
		var languages = new Dictionary<string, int>();

		foreach ((TranslationTarget target, TranslationResult result) in results)
		{
			YouTubeVideo video = videos[target.VideoIndex];
			var detectedLang = result.DetectedLanguage;

			languages[detectedLang] = languages.GetValueOrDefault(detectedLang) + 1;

			var translated = detectedLang is not "en" and not "unknown";
			if (translated)
				translatedCount++;

			videos[target.VideoIndex] = target.Field switch
			{
				TranslationField.Title => video with
				{
					TranslatedTitle = translated ? result.TranslatedText : video.Title,
					DetectedLanguage = video.DetectedLanguage ?? detectedLang,
				},
				TranslationField.Description => video with
				{
					TranslatedDescription = translated ? result.TranslatedText : video.Description,
					DetectedLanguage = video.DetectedLanguage ?? detectedLang,
				},
				_ => throw new InvalidOperationException(
					$"Unknown translation field {target.Field}"
				),
			};
		}

		var langSummary = string.Join(
			", ",
			languages.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}")
		);
		Telemetry.Debug(
			"Translate: done — {Translated} translated fields | {LangSummary} | {Chars:N0} Azure chars",
			translatedCount,
			langSummary,
			totalChars
		);

		return new TranslateResult(videos, totalChars);
	}

	public readonly record struct TranslateResult(List<YouTubeVideo> Videos, int AzureChars);

	private readonly record struct BatchApiResult(
		TranslationTarget Target,
		TranslationResult Result
	);

	private enum TranslationField
	{
		Title,
		Description,
	}

	private readonly record struct TranslationTarget(
		int VideoIndex,
		TranslationField Field,
		string Text
	);
}
