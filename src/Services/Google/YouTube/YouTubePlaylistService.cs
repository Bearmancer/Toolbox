using Core;
using ErrorOr;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using SerilogTracing;

namespace Services.Google.YouTube;

public class YouTubePlaylistService(YouTubeService yt)
{
	private static async Task<List<T>> PaginateAsync<T>(
		string activityName,
		Func<string?, Task<(IList<T> Items, string? NextPageToken)>> fetch,
		CancellationToken ct
	)
	{
		var results = new List<T>();
		string? pageToken = null;
		string? previousToken = null;
		var pageNumber = 0;

		do
		{
			ct.ThrowIfCancellationRequested();
			pageNumber++;

			var (items, nextPageToken) = await fetch(pageToken);
			results.AddRange(items);

			Telemetry.Debug(
				"{Activity}: page {Page} fetched {Count} items (token={Token})",
				activityName,
				pageNumber,
				items.Count,
				pageToken ?? "first"
			);

			if (nextPageToken is { } && nextPageToken == previousToken)
			{
				Telemetry.Warn(
					"{Activity}: duplicate nextPageToken detected at page {Page} — breaking pagination to prevent infinite loop",
					activityName,
					pageNumber
				);
				break;
			}

			previousToken = pageToken;
			pageToken = nextPageToken;
		} while (pageToken is { });

		Telemetry.Debug(
			"{Activity}: pagination complete — {TotalPages} pages, {TotalItems} items",
			activityName,
			pageNumber,
			results.Count
		);

		return results;
	}

	public async Task<IList<Playlist>> GetPlaylistsAsync(
		CancellationToken ct,
		string parts = "snippet"
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.GetPlaylists"
		);

		PlaylistsResource.ListRequest request = yt.Playlists.List(part: parts);
		request.Mine = true;
		request.MaxResults = 50;

		List<Playlist> playlists = await PaginateAsync(
			"YouTube.GetPlaylists",
			async pageToken =>
			{
				request.PageToken = pageToken;
				PlaylistListResponse response = await request.ExecuteAsync(ct);
				return (response.Items ?? [], response.NextPageToken);
			},
			ct
		);

		activity.Complete(Serilog.Events.LogEventLevel.Debug);
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

		PlaylistItemsResource.ListRequest request = yt.PlaylistItems.List(part: parts);
		request.PlaylistId = playlistId;
		request.MaxResults = 50;

		List<PlaylistItem> items = await PaginateAsync(
			"YouTube.GetPlaylistItems",
			async pageToken =>
			{
				request.PageToken = pageToken;
				PlaylistItemListResponse response = await request.ExecuteAsync(ct);
				return (response.Items ?? [], response.NextPageToken);
			},
			ct
		);

		activity.Complete(Serilog.Events.LogEventLevel.Debug);
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

		List<PlaylistItemListResponse> pages = await PaginateAsync<PlaylistItemListResponse>(
			"YouTube.GetPlaylistItemPagesRaw",
			async pageToken =>
			{
				request.PageToken = pageToken;
				PlaylistItemListResponse response = await request.ExecuteAsync(
					cancellationToken: ct
				);
				return ((IList<PlaylistItemListResponse>)[response], response.NextPageToken);
			},
			ct
		);

		activity.Complete(Serilog.Events.LogEventLevel.Debug);
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

		PlaylistsResource.ListRequest request = yt.Playlists.List(part: "snippet,contentDetails");
		request.Mine = true;
		request.MaxResults = 50;

		List<PlaylistSnapshot> snapshots = await PaginateAsync(
			"YouTube.GetPlaylistSummaries",
			async pageToken =>
			{
				request.PageToken = pageToken;
				PlaylistListResponse response = await request.ExecuteAsync(ct);
				var mapped = (response.Items ?? [])
					.Select(playlist =>
					{
						DateTimeOffset publishedAt = ParsePublishedAt(
							playlist.Id,
							playlist.Snippet?.PublishedAtRaw
						);
						return new PlaylistSnapshot
						{
							PlaylistId = playlist.Id,
							Title = playlist.Snippet!.Title!,
							LastUpdated = publishedAt,
							LastChecked = DateTimeOffset.UtcNow,
							ETag = playlist.ETag,
							ReportedVideoCount = playlist.ContentDetails?.ItemCount ?? 0,
						};
					})
					.ToList();
				return ((IList<PlaylistSnapshot>)mapped, response.NextPageToken);
			},
			ct
		);

		activity.Complete(Serilog.Events.LogEventLevel.Debug);
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
			activity.Complete(Serilog.Events.LogEventLevel.Debug);
			return null;
		}

		DateTimeOffset publishedAt = ParsePublishedAt(playlistId, playlist.Snippet?.PublishedAtRaw);

		activity.Complete(Serilog.Events.LogEventLevel.Debug);
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

	public async Task<ErrorOr<string>> DeletePlaylistAsync(string playlistId, CancellationToken ct)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.DeletePlaylist"
		);

		try
		{
			await yt.Playlists.Delete(playlistId).ExecuteAsync(ct);
			activity.Complete(Serilog.Events.LogEventLevel.Debug);
			Telemetry.Info("Deleted playlist {Id}", playlistId);
			return playlistId;
		}
		catch (Exception ex)
		{
			activity.Complete(Serilog.Events.LogEventLevel.Debug);
			Telemetry.Error(
				"YouTube.DeletePlaylistFailed id={Id}: {Error}",
				playlistId,
				ex.Message
			);
			return Errors.YouTube.ApiError($"Failed to delete playlist {playlistId}: {ex.Message}");
		}
	}

	public async Task<ErrorOr<string>> InsertPlaylistItemAsync(
		string playlistId,
		string videoId,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.YouTube);
		using LoggerActivity activity = Telemetry.StartActivity(
			messageTemplate: "YouTube.InsertPlaylistItem"
		);

		try
		{
			PlaylistItem item = new()
			{
				Snippet = new PlaylistItemSnippet
				{
					PlaylistId = playlistId,
					ResourceId = new ResourceId { Kind = "youtube#video", VideoId = videoId },
				},
			};

			Telemetry.Debug(
				"YouTube.InsertPlaylistItem: inserting video {VideoId} into playlist {PlaylistId}",
				videoId,
				playlistId
			);

			PlaylistItem response = await yt.PlaylistItems.Insert(item, "snippet").ExecuteAsync(ct);

			activity.Complete(Serilog.Events.LogEventLevel.Debug);
			return response.Id!;
		}
		catch (Exception ex)
		{
			activity.Complete(Serilog.Events.LogEventLevel.Debug);
			Telemetry.Error(
				"YouTube.InsertItemFailed video={VideoId} playlist={PlaylistId}: {Error}",
				videoId,
				playlistId,
				ex.Message
			);
			return Errors.YouTube.ApiError(
				$"Failed to insert video {videoId} into playlist {playlistId}: {ex.Message}"
			);
		}
	}
}
