using Core;
using Services.Azure;

namespace Services.Google.YouTube;

public class YouTubeTranslationService(TranslateService translateService)
{
    private const int MaxTextsPerCall = 70;
    private const int MaxCharsPerCall = 30000;

    public async Task<(List<YouTubeVideo> Videos, int AzureChars)> TranslateVideosAsync(
        List<YouTubeVideo> videos,
        CancellationToken ct,
        Func<IReadOnlyList<YouTubeVideo>, CancellationToken, Task>? checkpointAsync = null
    )
    {
        var targets = new List<TranslationTarget>();
        foreach (
            var (videoIndex, video) in videos.Select((video, index) => (index, video)).ToList()
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

        if (targets.Count == 0)
            return (videos, 0);

        var batches = new List<List<TranslationTarget>>();
        var currentBatch = new List<TranslationTarget>();
        var currentCharCount = 0;

        foreach (var target in targets)
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

        var totalChars = targets.Sum(t => t.Text.Length);
        var unchangedCount =
            videos.Count
            - videos.Count(v => v.TranslatedTitle is null || v.TranslatedDescription is null);
        Telemetry.Info(
            "Translate: {Need}/{Total} videos need text completion, {Unchanged} already complete | {Chars:N0} chars ({Batches} {BatchWord})",
            videos.Count - unchangedCount,
            videos.Count,
            unchangedCount,
            totalChars,
            batches.Count,
            batches.Count == 1 ? "batch" : "batches"
        );

        var translatedCount = 0;
        var languages = new Dictionary<string, int>();
        var batchIndex = 0;
        foreach (var batch in batches)
        {
            ct.ThrowIfCancellationRequested();
            batchIndex++;

            var batchChars = batch.Sum(t => t.Text.Length);
            Telemetry.Info(
                "Translate: [{Batch}/{TotalBatches}] → Azure ({Targets} fields, {Chars:N0} chars)",
                batchIndex,
                batches.Count,
                batch.Count,
                batchChars
            );

            var batchResults = await translateService.TranslateBatchAsync(
                [.. batch.Select(t => t.Text)],
                "en",
                ct
            );

            foreach (var (target, result) in batch.Zip(batchResults))
            {
                var video = videos[target.VideoIndex];
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
                        TranslatedDescription = translated
                            ? result.TranslatedText
                            : video.Description,
                        DetectedLanguage = video.DetectedLanguage ?? detectedLang,
                    },
                    _ => throw new InvalidOperationException(
                        $"Unknown translation field {target.Field}"
                    ),
                };
            }

            if (checkpointAsync is { })
                await checkpointAsync(videos, ct);
        }

        var langSummary = string.Join(
            ", ",
            languages.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}")
        );
        Telemetry.Info(
            "Translate: done — {Translated} translated fields | {LangSummary} | {Chars:N0} Azure chars",
            translatedCount,
            langSummary,
            totalChars
        );

        return (videos, totalChars);
    }

    private enum TranslationField
    {
        Title,
        Description,
    }

    private sealed record TranslationTarget(int VideoIndex, TranslationField Field, string Text);
}
