using System.Text.Json;
using Core;
using ErrorOr;

namespace Services.Google.YouTube;

public class DashboardService
{
	private static readonly string StateRoot = Path.Combine(
		PathResolver.RepoRoot,
		"state",
		"youtube"
	);

	private static readonly string ManifestFile = Path.Combine(StateRoot, "manifest.json");

	private static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");

	public static async Task<ErrorOr<DashboardResult>> GenerateDashboardDataAsync(
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);

		ErrorOr<IReadOnlyList<PlaylistSnapshot>> playlistsResult = await LoadPlaylistsAsync(ct);
		if (playlistsResult.IsError)
			return playlistsResult.FirstError;

		IReadOnlyList<PlaylistSnapshot> playlists = playlistsResult.Value;
		Telemetry.Info("Loaded {Count} playlists from manifest", playlists.Count);

		Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist =
			await LoadVideosByPlaylistAsync(playlists, ct);
		var totalVideos = videosByPlaylist.Values.Sum(v => v.Count);
		Telemetry.Info(
			"Loaded {Count} videos across {PlaylistCount} playlists",
			totalVideos,
			videosByPlaylist.Count
		);

		return new DashboardResult(playlists, videosByPlaylist);
	}

	private static async Task<ErrorOr<IReadOnlyList<PlaylistSnapshot>>> LoadPlaylistsAsync(
		CancellationToken ct
	)
	{
		if (!File.Exists(ManifestFile))
			return (ErrorOr<IReadOnlyList<PlaylistSnapshot>>)new List<PlaylistSnapshot>();

		try
		{
			YouTubeFetchState state = await YouTubeFetchState.LoadAsync(ManifestFile, ct);
			List<PlaylistSnapshot> list = [.. state.PlaylistSnapshots.Values];
			return (ErrorOr<IReadOnlyList<PlaylistSnapshot>>)list;
		}
		catch (Exception ex) when (ex is JsonException or IOException)
		{
			return Errors.YouTube.ApiError($"Failed to load manifest: {ex.Message}");
		}
	}

	private static async Task<
		Dictionary<string, IReadOnlyList<YouTubeVideo>>
	> LoadVideosByPlaylistAsync(IReadOnlyList<PlaylistSnapshot> playlists, CancellationToken ct)
	{
		Dictionary<string, IReadOnlyList<YouTubeVideo>> result = [];

		if (!Directory.Exists(ProcessedDir))
			return result;

		Dictionary<string, string> reverseLookup = [];
		foreach (PlaylistSnapshot p in playlists)
			reverseLookup.TryAdd(Text.SanitizeFileName(p.Title), p.Title);

		foreach (var file in Directory.GetFiles(ProcessedDir, "*.json"))
		{
			ct.ThrowIfCancellationRequested();

			try
			{
				await using FileStream stream = File.OpenRead(file);
				List<YouTubeVideo>? videos = await JsonSerializer.DeserializeAsync<
					List<YouTubeVideo>
				>(stream, YouTubeFetchState.JsonOptions, ct);
				if (
					videos is { Count: > 0 }
					&& reverseLookup.TryGetValue(
						Path.GetFileNameWithoutExtension(file),
						out var title
					)
				)
					result[title] = videos;
			}
			catch (Exception ex) when (ex is JsonException or IOException)
			{
				Telemetry.Warn(
					"Skipping corrupt file {File}: {Error}",
					Path.GetFileName(file),
					ex.Message
				);
			}
		}

		return result;
	}

	public readonly record struct DashboardResult(
		IReadOnlyList<PlaylistSnapshot> Playlists,
		Dictionary<string, IReadOnlyList<YouTubeVideo>> VideosByPlaylist
	);
}
