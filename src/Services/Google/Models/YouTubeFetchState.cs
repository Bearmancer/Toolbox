using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services.Google.Models;

public sealed record PlaylistSnapshot
{
    public required string PlaylistId { get; init; }
    public required string Title { get; init; }
    public required string ETag { get; init; }
    public required long ReportedVideoCount { get; init; }
    public required DateTimeOffset LastChecked { get; init; }
    public required DateTimeOffset LastUpdated { get; init; }
}

public sealed record YouTubeFetchState
{
    public Dictionary<string, PlaylistSnapshot> PlaylistSnapshots { get; init; } = [];
    public required DateTimeOffset LastChecked { get; init; }
    public required DateTimeOffset? LastUpdated { get; init; }
    public required bool FetchComplete { get; init; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new HmsTimeSpanConverter() },
    };

    public static async Task<YouTubeFetchState> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return new YouTubeFetchState { LastChecked = DateTimeOffset.MinValue, LastUpdated = null, FetchComplete = false };

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<YouTubeFetchState>(stream, JsonOptions, ct)
                ?? new YouTubeFetchState { LastChecked = DateTimeOffset.MinValue, LastUpdated = null, FetchComplete = false };
        }
        catch (JsonException)
        {
            return new YouTubeFetchState { LastChecked = DateTimeOffset.MinValue, LastUpdated = null, FetchComplete = false };
        }
    }

    public static async Task SaveAsync(string path, YouTubeFetchState state, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct);
    }
}

public class HmsTimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => TimeSpan.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(@"hh\:mm\:ss"));
}
