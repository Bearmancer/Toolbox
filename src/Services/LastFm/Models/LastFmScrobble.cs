using System.Globalization;

namespace Services.LastFm.Models;

public sealed record LastFmScrobble
{
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30);
    public required string TrackTitle { get; init; }
    public required string Artist { get; init; }
    public required string Album { get; init; }
    public required DateTimeOffset PlayedAt { get; init; }

    public string Date =>
        PlayedAt.ToOffset(IstOffset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
