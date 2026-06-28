using System.Text.Json;
using System.Text.Json.Serialization;
using Core;

namespace Services.Google.YouTube;

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
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new HmsTimeSpanConverter() },
    };

    public Dictionary<string, PlaylistSnapshot> PlaylistSnapshots { get; init; } = [];
    public required DateTimeOffset LastChecked { get; init; }
    public required DateTimeOffset? LastUpdated { get; init; }
    public int AzureCharsUsed { get; init; }
    public int AzureCharsMonth { get; init; }

    public static async Task<YouTubeFetchState> LoadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return new YouTubeFetchState
            {
                LastChecked = DateTimeOffset.MinValue,
                LastUpdated = null,
            };

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<YouTubeFetchState>(stream, JsonOptions, ct)
                ?? new YouTubeFetchState
                {
                    LastChecked = DateTimeOffset.MinValue,
                    LastUpdated = null,
                };
        }
        catch (JsonException ex)
        {
            Telemetry.Error(
                "Corrupt manifest at {Path}, resetting to empty state: {Error}",
                path,
                ex.Message
            );
            return new YouTubeFetchState
            {
                LastChecked = DateTimeOffset.MinValue,
                LastUpdated = null,
            };
        }
    }

    public static async Task SaveAsync(string path, YouTubeFetchState state, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is { } && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        try
        {
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct);
        }
        catch (IOException ex)
        {
            Telemetry.Error("Failed to save manifest to {Path}: {Error}", path, ex.Message);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Telemetry.Error(
                "Permission denied saving manifest to {Path}: {Error}",
                path,
                ex.Message
            );
            throw;
        }
    }
}

public class HmsTimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => TimeSpan.Parse(reader.GetString()!);

    public override void Write(
        Utf8JsonWriter writer,
        TimeSpan value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(value.ToString(@"hh\:mm\:ss"));
}
