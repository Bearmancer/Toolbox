namespace Toolbox.Sync;

public record YouTubePlaylist(
    string Id,
    string Title,
    string? Description,
    int VideoCount,
    string? ChannelId,
    string? ChannelTitle
);

public record YouTubeVideo(
    string Id,
    string Title,
    string? Description,
    string? ChannelId,
    string? ChannelTitle,
    TimeSpan? Duration,
    DateTimeOffset? PublishedAt
);

public record LastFmScrobble(
    string Artist,
    string Track,
    string? Album,
    DateTimeOffset? PlayedAt,
    string? MusicBrainzId
);

public record SyncResult(
    int TotalCount,
    int NewCount,
    int UpdatedCount,
    DateTimeOffset? OldestTimestamp,
    DateTimeOffset? NewestTimestamp,
    TimeSpan Duration
);