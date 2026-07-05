using System.Xml;
using Core;
using ErrorOr;
using Google.Apis.YouTube.v3;

namespace Services.Google.YouTube;

public sealed record YouTubeVideo
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required TimeSpan Duration { get; init; }
    public required string ChannelName { get; init; }
    public required string VideoId { get; init; }
    public required string ChannelId { get; init; }
    public string? TranslatedTitle { get; init; }
    public string? TranslatedDescription { get; init; }
    public string? DetectedLanguage { get; init; }
}

public class YouTubeVideoService(YouTubeService yt)
{
    public async Task<ErrorOr<Dictionary<string, TimeSpan>>> GetVideoDurationsAsync(
        List<string> videoIds,
        CancellationToken ct
    )
    {
        try
        {
            using var _ = Telemetry.ForService(ServiceName.YouTube);
            using var activity = Telemetry.StartActivity(messageTemplate: "YouTube.GetVideoDurations");

            if (videoIds.Count == 0)
                return ErrorOrFactory.From(new Dictionary<string, TimeSpan>());

            var result = new Dictionary<string, TimeSpan>();

            foreach (var batch in videoIds.Chunk(size: 50))
            {
                ct.ThrowIfCancellationRequested();

                var request = yt.Videos.List(part: "contentDetails");
                request.Id = string.Join(",", batch);
                var response = await request.ExecuteAsync(cancellationToken: ct);

                foreach (var video in response.Items ?? [])
                {
                    var duration = ParseIso8601Duration(iso: video.ContentDetails?.Duration);
                    result[key: video.Id] = duration;
                }
            }

            activity.Complete();
            Telemetry.Debug("YouTube.GetVideoDurations fetched {Count} durations", result.Count);
            return ErrorOrFactory.From(result);
        }
        catch (FormatException ex)
        {
            return Errors.YouTube.ApiError(ex.Message);
        }
    }

    private static TimeSpan ParseIso8601Duration(string? iso) =>
        string.IsNullOrEmpty(value: iso)
            ? throw new FormatException(message: "Duration is null or empty")
            : XmlConvert.ToTimeSpan(s: iso);
}
