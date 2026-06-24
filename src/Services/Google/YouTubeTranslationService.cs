using Core;
using Services.Azure;
using Services.Google.Models;

namespace Services.Google;

public class YouTubeTranslationService(TranslateService translateService)
{
    private const int MaxTextsPerCall = 70;
    private const int MaxCharsPerCall = 30000;

    public async Task<(List<YouTubeVideo> Videos, int AzureChars)> TranslateVideosAsync(
        List<YouTubeVideo> videos,
        CancellationToken ct)
    {
        var toTranslate = videos
            .Select((v, i) => (Index: i, Video: v))
            .Where(x => x.Video.TranslatedTitle is null)
            .ToList();

        if (toTranslate.Count == 0)
            return (videos, 0);

        var texts = new List<string>(toTranslate.Count * 2);
        foreach (var (_, video) in toTranslate)
        {
            texts.Add(video.Title);
            texts.Add(video.Description.Length > 0 ? video.Description : " ");
        }

        var chunks = new List<string[]>();
        var currentChunk = new List<string>();
        var currentCharCount = 0;

        foreach (var text in texts)
        {
            if (currentChunk.Count >= MaxTextsPerCall || currentCharCount + text.Length > MaxCharsPerCall)
            {
                chunks.Add([.. currentChunk]);
                currentChunk.Clear();
                currentCharCount = 0;
            }

            currentChunk.Add(text);
            currentCharCount += text.Length;
        }

        if (currentChunk.Count > 0)
            chunks.Add([.. currentChunk]);

        var totalChars = texts.Sum(t => t.Length);
        var unchangedCount = videos.Count - toTranslate.Count;
        Telemetry.Info("  translate: {Need}/{Total} videos, {Unchanged} unchanged | {Chars:N0} chars ({Batches} {BatchWord})",
            toTranslate.Count, videos.Count, unchangedCount, totalChars, chunks.Count, chunks.Count == 1 ? "batch" : "batches");

        var allResults = new List<TranslationResult>(texts.Count);
        var chunkIndex = 0;
        foreach (var batch in chunks)
        {
            ct.ThrowIfCancellationRequested();
            chunkIndex++;

            if (chunks.Count > 1)
            {
                var batchChars = batch.Sum(t => t.Length);
                Telemetry.Info("  translate: [{Batch}/{TotalBatches}] → Azure ({Videos} videos, {Chars:N0} chars)",
                    chunkIndex, chunks.Count, batch.Length / 2, batchChars);
            }

            var batchResults = await translateService.TranslateBatchAsync(batch, "en", ct);
            allResults.AddRange(batchResults);
        }

        var actuallyTranslatedCount = 0;
        var englishCount = 0;
        var languages = new Dictionary<string, int>();
        foreach (var (resultIndex, (videoIndex, _)) in toTranslate.Index())
        {
            var titleResult = allResults[resultIndex * 2];
            var descResult = allResults[resultIndex * 2 + 1];
            var video = videos[videoIndex];
            var detectedLang = titleResult.DetectedLanguage;

            languages[detectedLang] = languages.GetValueOrDefault(detectedLang) + 1;

            var isEnglish = detectedLang == "en";
            var translatedTitle = isEnglish ? video.Title : titleResult.TranslatedText;
            var translatedDesc = isEnglish ? video.Description : descResult.TranslatedText;

            if (!isEnglish)
                actuallyTranslatedCount++;
            else
                englishCount++;

            videos[videoIndex] = video with
            {
                TranslatedTitle = translatedTitle,
                TranslatedDescription = translatedDesc,
                DetectedLanguage = detectedLang,
            };
        }

        var langSummary = string.Join(", ", languages.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}"));
        Telemetry.Info("  translate: done — {Translated} translated, {English} English | {LangSummary} | {Chars:N0} Azure chars",
            actuallyTranslatedCount, englishCount, langSummary, totalChars);

        return (videos, totalChars);
    }
}
