namespace Toolbox.Music;

public enum MusicSource
{
    MusicBrainz,
    Discogs
}

public record SearchResult(
    MusicSource Source,
    string Id,
    string Title,
    string? Artist = null,
    int? Year = null,
    string? Format = null,
    string? Label = null,
    string? ReleaseType = null,
    int? Score = null,
    string? Country = null,
    string? CatalogNumber = null,
    string? Status = null,
    string? Disambiguation = null,
    IReadOnlyList<string>? Genres = null,
    IReadOnlyList<string>? Styles = null
);

public record ReleaseInfo(
    MusicSource Source,
    string Id,
    string Title,
    string? Artist,
    string? Label,
    string? CatalogNumber,
    int? Year,
    string? Notes,
    int DiscCount,
    int TrackCount,
    TimeSpan TotalDuration
);

public record TrackInfo(
    int DiscNumber,
    int TrackNumber,
    string Title,
    TimeSpan? Duration = null,
    int? RecordingYear = null,
    string? Composer = null,
    string? WorkName = null,
    string? Conductor = null,
    string? Orchestra = null,
    IReadOnlyList<string>? Soloists = null,
    string? Artist = null,
    string? RecordingVenue = null,
    string? RecordingId = null
);

public record ReleaseData(ReleaseInfo Info, IReadOnlyList<TrackInfo> Tracks);