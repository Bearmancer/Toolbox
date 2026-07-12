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
		var sorted = playlists.OrderBy(p => p.Title).ToList();
		List<object> videos = BuildVideoData(sorted, videosByPlaylist);
		List<object> playlistData = BuildPlaylistData(sorted);

		return new DashboardData(
			sorted.Count,
			videos.Count,
			BuildDropdownHtml(sorted),
			BuildVideoViewsHtml(sorted),
			BuildDataJs(videos, playlistData)
		);
	}

	private static List<object> BuildVideoData(
		List<PlaylistSnapshot> sorted,
		Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist
	)
	{
		var result = new List<object>();
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
		var sb = new StringBuilder();
		sb.AppendLine("<select id=\"playlist-dropdown\" onchange=\"switchView(this.value)\">");
		sb.AppendLine("<option value=\"all\">All Playlists</option>");
		foreach (PlaylistSnapshot p in sorted)
			sb.AppendLine($"<option value=\"{p.PlaylistId}\">{Escape(p.Title)}</option>");
		sb.AppendLine("</select>");
		return sb.ToString();
	}

	private static string BuildVideoViewsHtml(List<PlaylistSnapshot> sorted)
	{
		var sb = new StringBuilder();
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

	private static string BuildDataJs(List<object> videos, List<object> playlists)
	{
		var sb = new StringBuilder();
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
	string DataJs
);
