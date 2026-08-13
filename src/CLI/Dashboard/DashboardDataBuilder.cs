using System.Text;
using System.Text.Json;
using Services.Google.YouTube;

namespace CLI.Dashboard;

public static class DashboardDataBuilder
{
	private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

	public static DashboardData Build(
		IReadOnlyList<PlaylistSnapshot> playlists,
		Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist
	)
	{
		List<PlaylistSnapshot> sorted = [.. playlists.OrderBy(p => p.Title)];
		List<object> videos = BuildVideoData(sorted, videosByPlaylist);
		List<object> playlistData = BuildPlaylistData(sorted);

		return new DashboardData(
			sorted.Count,
			videos.Count,
			BuildDropdownHtml(sorted),
			BuildVideoViewsHtml(sorted),
			BuildPlaylistFilterHtml(sorted),
			BuildDataJs(videos, playlistData)
		);
	}

	private static List<object> BuildVideoData(
		List<PlaylistSnapshot> sorted,
		Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist
	)
	{
		List<object> result = [];
		foreach (PlaylistSnapshot p in sorted)
		{
			if (!videosByPlaylist.TryGetValue(p.Title, out IReadOnlyList<YouTubeVideo>? videos))
				continue;

			foreach (YouTubeVideo v in videos)
				result.Add(
					new
					{
						videoId = v.VideoId,
						title = v.TranslatedTitle ?? v.Title,
						description = v.TranslatedDescription ?? v.Description,
						duration = v.Duration.ToString(@"hh\:mm\:ss"),
						channelId = v.ChannelId,
						channelName = v.ChannelName,
						playlistId = p.PlaylistId,
						playlistName = p.Title,
					}
				);
		}

		return result;
	}

	private static List<object> BuildPlaylistData(List<PlaylistSnapshot> sorted) =>
		[
			.. sorted.Select(p => new
			{
				playlistId = p.PlaylistId,
				title = p.Title,
				videoCount = p.ReportedVideoCount,
				lastUpdated = p.LastUpdated.ToString("yyyy-MM-dd"),
			}),
		];

	private static string BuildDropdownHtml(List<PlaylistSnapshot> sorted)
	{
		StringBuilder sb = new();
		sb.AppendLine("<select id=\"playlist-dropdown\" onchange=\"switchView(this.value)\">");
		sb.AppendLine("<option value=\"all\">Playlist Overview</option>");
		sb.AppendLine("<option value=\"all-videos\">All Videos</option>");
		foreach (PlaylistSnapshot p in sorted)
			sb.AppendLine($"<option value=\"{p.PlaylistId}\">{Escape(p.Title)}</option>");
		sb.AppendLine("</select>");
		return sb.ToString();
	}

	private static string BuildVideoViewsHtml(List<PlaylistSnapshot> sorted)
	{
		StringBuilder sb = new();
		foreach (PlaylistSnapshot p in sorted)
		{
			sb.AppendLine($"<div id=\"view-{p.PlaylistId}\" class=\"view\">");
			sb.AppendLine($"<h2>{Escape(p.Title)}</h2>");
			sb.AppendLine(
				$"<input type=\"text\" class=\"per-search\" "
					+ $"placeholder=\"Search in {Escape(p.Title)}...\" "
					+ $"oninput=\"onPlaylistSearch('{p.PlaylistId}', this.value)\">"
			);
			sb.AppendLine($"<div id=\"video-table-{p.PlaylistId}\"></div>");
			sb.AppendLine("</div>");
		}

		return sb.ToString();
	}

	private static string BuildPlaylistFilterHtml(List<PlaylistSnapshot> sorted)
	{
		StringBuilder sb = new();
		foreach (PlaylistSnapshot p in sorted)
		{
			sb.AppendLine(
				$"<label><input type=\"checkbox\" id=\"pl-cb-{p.PlaylistId}\" checked "
					+ $"onchange=\"onTogglePlaylistIncluded('{p.PlaylistId}', this.checked)\"> "
					+ Escape(p.Title)
					+ "</label>"
			);
		}
		return sb.ToString();
	}

	private static string BuildDataJs(List<object> videos, List<object> playlists)
	{
		StringBuilder sb = new();
		sb.Append("window.allVideos=");
		sb.Append(JsonSerializer.Serialize(videos, CompactJson));
		sb.AppendLine(";");
		sb.Append("window.allPlaylists=");
		sb.Append(JsonSerializer.Serialize(playlists, CompactJson));
		sb.AppendLine(";");
		return sb.ToString();
	}

	private static string Escape(string text) =>
		text.Replace("&", "&amp;")
			.Replace("<", "&lt;")
			.Replace(">", "&gt;")
			.Replace("\"", "&quot;")
			.Replace("'", "&#39;");
}

public record DashboardData(
	int PlaylistCount,
	int VideoCount,
	string DropdownHtml,
	string VideoViewsHtml,
	string PlaylistFilterHtml,
	string DataJs
);
