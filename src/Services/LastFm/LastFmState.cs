using System.Text.Json;
using Core;

namespace Services.LastFm;

public static class LastFmState
{
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	public static List<LastFmScrobble> MergeScrobbles(
		List<LastFmScrobble> existing,
		List<LastFmScrobble> newScrobbles
	) =>
		[
			.. existing
				.Concat(second: newScrobbles)
				.GroupBy(s => s.PlayedAt)
				.Select(g => g.First())
				.OrderByDescending(s => s.PlayedAt),
		];

	public static async Task<List<LastFmScrobble>> LoadScrobblesAsync(string stateDir)
	{
		var path = Path.Combine(stateDir, "scrobbles.json");

		if (!File.Exists(path: path))
			return [];

		try
		{
			await using FileStream stream = File.OpenRead(path: path);
			return await JsonSerializer.DeserializeAsync<List<LastFmScrobble>>(stream, JsonOptions)
				?? [];
		}
		catch (JsonException ex)
		{
			Telemetry.Warn("Corrupt scrobbles at {Path}, resetting: {Error}", path, ex.Message);
			return [];
		}
	}

	public static async Task SaveScrobblesAsync(string stateDir, List<LastFmScrobble> scrobbles)
	{
		if (!Directory.Exists(path: stateDir))
			Directory.CreateDirectory(path: stateDir);

		var path = Path.Combine(stateDir, "scrobbles.json");

		try
		{
			await using FileStream stream = File.Create(path: path);
			await JsonSerializer.SerializeAsync(stream, scrobbles, JsonOptions);
		}
		catch (IOException ex)
		{
			Telemetry.Error("Failed to save scrobbles to {Path}: {Error}", path, ex.Message);
			throw;
		}
		catch (UnauthorizedAccessException ex)
		{
			Telemetry.Error(
				"Permission denied saving scrobbles to {Path}: {Error}",
				path,
				ex.Message
			);
			throw;
		}
	}
}
