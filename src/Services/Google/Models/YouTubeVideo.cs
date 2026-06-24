namespace Services.Google.Models;

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
}
