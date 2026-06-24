using Services.Azure;
using Services.Google.Models;

namespace Services.Google;

public class YouTubeTranslationService(TranslateService translateService)
{
    private const int MaxTextsPerCall = 100;

    public async Task<List<YouTubeVideo>> TranslateVideosAsync(
        List<YouTubeVideo> videos,
        CancellationToken ct)
    {
        var toTranslate = videos
            .Select((v, i) => (Index: i, Video: v))
            .Where(x => x.Video.TranslatedTitle is null)
            .ToList();

        if (toTranslate.Count == 0)
            return videos;

        var texts = new List<string>(toTranslate.Count * 2);
        foreach (var (_, video) in toTranslate)
        {
            texts.Add(video.Title);
            texts.Add(video.Description.Length > 0 ? video.Description : " ");
        }

        var allResults = new List<TranslationResult>(texts.Count);
        foreach (var batch in texts.Chunk(MaxTextsPerCall))
        {
            var batchResults = await translateService.TranslateBatchAsync(batch, "en", ct);
            allResults.AddRange(batchResults);
        }

        foreach (var (resultIndex, (videoIndex, _)) in toTranslate.Index())
        {
            var titleResult = allResults[resultIndex * 2];
            var descResult = allResults[resultIndex * 2 + 1];
            var video = videos[videoIndex];

            // If translation unchanged, video was English — mark as-is
            var translatedTitle = titleResult.TranslatedText == video.Title
                ? video.Title
                : titleResult.TranslatedText;
            var translatedDesc = descResult.TranslatedText == video.Description
                ? video.Description
                : descResult.TranslatedText;

            videos[videoIndex] = video with
            {
                TranslatedTitle = translatedTitle,
                TranslatedDescription = translatedDesc,
            };
        }

        return videos;
    }
}
