using Core;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using SerilogTracing;

namespace Services.Google.YouTube;

public class YouTubePlaylistService(YouTubeService yt)
{
	public async Task<IList<Playlist>> GetPlaylistsAsync(
		CancellationToken ct,
		string parts = "snippet"
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.GetPlaylists"
		);

		PlaylistsResource.ListRequest? request = yt.Playlists.List(part: parts);
		request.Mine = true;
		request.MaxResults = 50;

		var playlists = new List<Playlist>();
		string? pageToken = null;

		do
		{
			ct.ThrowIfCancellationRequested();

			request.PageToken = pageToken;
			PlaylistListResponse? response = await request.ExecuteAsync(cancellationToken: ct);
			playlists.AddRange(response.Items ?? []);
			pageToken = response.NextPageToken;
		} while (pageToken is { });

		activity.Complete();
		Telemetry.Debug("YouTube.GetPlaylists returned {Count} playlists", playlists.Count);
		return playlists;
	}

	public async Task<IList<PlaylistItem>> GetPlaylistItemsAsync(
		string playlistId,
		CancellationToken ct,
		string parts = "snippet"
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.GetPlaylistItems"
		);

		PlaylistItemsResource.ListRequest? request = yt.PlaylistItems.List(part: parts);
		request.PlaylistId = playlistId;
		request.MaxResults = 50;

		var items = new List<PlaylistItem>();
		string? pageToken = null;

		do
		{
			ct.ThrowIfCancellationRequested();

			request.PageToken = pageToken;
			PlaylistItemListResponse? response = await request.ExecuteAsync(cancellationToken: ct);
			items.AddRange(response.Items ?? []);
			pageToken = response.NextPageToken;
		} while (pageToken is { });

		activity.Complete();
		Telemetry.Debug(
			"YouTube.GetPlaylistItems returned {Count} items for playlist {Id}",
			items.Count,
			playlistId
		);
		return items;
	}

	public async Task UpdateItemPositionAsync(PlaylistItem item, int position, CancellationToken ct)
	{
		item.Snippet.Position = position;
		PlaylistItemsResource.UpdateRequest? request = yt.PlaylistItems.Update(item, "snippet");
		await request.ExecuteAsync(cancellationToken: ct);
	}

	public async Task<IReadOnlyList<PlaylistItemListResponse>> GetPlaylistItemPagesRawAsync(
		string playlistId,
		string parts,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.GetPlaylistItemPagesRaw"
		);

		PlaylistItemsResource.ListRequest? request = yt.PlaylistItems.List(part: parts);
		request.PlaylistId = playlistId;
		request.MaxResults = 50;

		var pages = new List<PlaylistItemListResponse>();
		string? pageToken = null;

		do
		{
			ct.ThrowIfCancellationRequested();

			request.PageToken = pageToken;
			PlaylistItemListResponse? response = await request.ExecuteAsync(cancellationToken: ct);
			pages.Add(item: response);
			pageToken = response.NextPageToken;
		} while (pageToken is { });

		activity.Complete();
		Telemetry.Debug(
			"YouTube.GetPlaylistItemPagesRaw returned {Count} pages for {Id}",
			pages.Count,
			playlistId
		);
		return pages;
	}

	public async Task<IReadOnlyList<PlaylistSnapshot>> GetPlaylistSummariesAsync(
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.GetPlaylistSummaries"
		);

		PlaylistsResource.ListRequest? request = yt.Playlists.List(part: "snippet,contentDetails");
		request.Mine = true;
		request.MaxResults = 50;

		var snapshots = new List<PlaylistSnapshot>();
		string? pageToken = null;

		do
		{
			ct.ThrowIfCancellationRequested();

			request.PageToken = pageToken;
			PlaylistListResponse? response = await request.ExecuteAsync(cancellationToken: ct);

			snapshots.AddRange(
				from playlist in response.Items ?? []
				let publishedAt = ParsePublishedAt(playlist.Id, playlist.Snippet?.PublishedAtRaw)
				select new PlaylistSnapshot
				{
					PlaylistId = playlist.Id,
					Title = playlist.Snippet.Title,
					LastUpdated = publishedAt,
					LastChecked = DateTimeOffset.UtcNow,
					ETag = playlist.ETag,
					ReportedVideoCount = playlist.ContentDetails?.ItemCount ?? 0,
				}
			);

			pageToken = response.NextPageToken;
		} while (pageToken is { });

		activity.Complete();
		return snapshots;
	}

	public async Task<PlaylistSnapshot?> GetPlaylistSummaryAsync(
		string playlistId,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.GetPlaylistSummary"
		);

		PlaylistsResource.ListRequest? request = yt.Playlists.List(part: "snippet,contentDetails");
		request.Id = playlistId;
		PlaylistListResponse? response = await request.ExecuteAsync(cancellationToken: ct);

		Playlist? playlist = response.Items?.FirstOrDefault();
		if (playlist is null)
		{
			activity.Complete();
			return null;
		}

		DateTimeOffset publishedAt = ParsePublishedAt(playlistId, playlist.Snippet?.PublishedAtRaw);

		activity.Complete();
		return new PlaylistSnapshot
		{
			PlaylistId = playlist.Id!,
			Title = playlist.Snippet!.Title!,
			LastUpdated = publishedAt,
			LastChecked = DateTimeOffset.UtcNow,
			ETag = playlist.ETag!,
			ReportedVideoCount = playlist.ContentDetails?.ItemCount ?? 0,
		};
	}

	private static DateTimeOffset ParsePublishedAt(string playlistId, string? raw)
	{
		if (!string.IsNullOrEmpty(raw) && DateTimeOffset.TryParse(raw, out DateTimeOffset parsed))
			return parsed;

		Telemetry.Warn(
			"YouTube.GetPlaylistSummary: Playlist {Id} has missing or unparseable publishedAt '{Raw}' — using fallback",
			playlistId,
			raw ?? "null"
		);
		return DateTimeOffset.UtcNow;
	}
}
