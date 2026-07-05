using System.Text;
using Services.Google.YouTube;

namespace CLI.Dashboard;

public static class DashboardDataBuilder
{
    public static DashboardData Build(
        IReadOnlyList<PlaylistSnapshot> playlists,
        Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist)
    {
        var sortedPlaylists = playlists.OrderBy(p => p.Title).ToList();

        var playlistData = BuildPlaylistData(sortedPlaylists);
        var videoDataByPlaylist = BuildVideoDataByPlaylist(sortedPlaylists, videosByPlaylist);
        var allVideosData = BuildAllVideosData(sortedPlaylists, videosByPlaylist);
        var dropdownHtml = BuildDropdownHtml(sortedPlaylists);
        var playlistViewHtml = BuildPlaylistViewHtml();
        var videoViewsHtml = BuildVideoViewsHtml(sortedPlaylists);
        var videoDataJs = BuildVideoDataJs(videoDataByPlaylist, allVideosData);

        return new DashboardData(
            sortedPlaylists.Count,
            videoDataByPlaylist.Values.Sum(v => v.Count),
            playlistData,
            videoDataByPlaylist,
            allVideosData,
            dropdownHtml,
            playlistViewHtml,
            videoViewsHtml,
            videoDataJs
        );
    }

    private static List<object> BuildPlaylistData(List<PlaylistSnapshot> sortedPlaylists) =>
        sortedPlaylists.Select(p => (object)new
        {
            sortKey = p.Title,
            title = $"<a href=\"https://www.youtube.com/playlist?list={p.PlaylistId}\" target=\"_blank\">{Escape(p.Title)}</a>",
            videoCount = p.ReportedVideoCount,
            lastUpdated = p.LastUpdated.ToString("yyyy-MM-dd")
        }).ToList();

    private static Dictionary<string, List<object>> BuildVideoDataByPlaylist(
        List<PlaylistSnapshot> sortedPlaylists,
        Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist)
    {
        var result = new Dictionary<string, List<object>>();
        foreach (var p in sortedPlaylists)
        {
            if (videosByPlaylist.TryGetValue(p.Title, out var videos))
            {
                result[p.PlaylistId] = videos
                    .OrderBy(v => v.TranslatedTitle ?? v.Title)
                    .Select(v => (object)new
                    {
                        sortKey = v.TranslatedTitle ?? v.Title,
                        title = $"<a href=\"https://www.youtube.com/watch?v={v.VideoId}\" target=\"_blank\">{Escape(v.TranslatedTitle ?? v.Title)}</a>",
                        description = Escape(v.TranslatedDescription ?? v.Description ?? ""),
                        duration = v.Duration.ToString(@"hh\:mm\:ss"),
                        channel = $"<a href=\"https://www.youtube.com/channel/{v.ChannelId}\" target=\"_blank\">{Escape(v.ChannelName)}</a>"
                    }).ToList();
            }
        }
        return result;
    }

    private static List<object> BuildAllVideosData(
        List<PlaylistSnapshot> sortedPlaylists,
        Dictionary<string, IReadOnlyList<YouTubeVideo>> videosByPlaylist)
    {
        var result = new List<object>();
        foreach (var p in sortedPlaylists)
        {
            if (videosByPlaylist.TryGetValue(p.Title, out var videos))
            {
                foreach (var v in videos)
                {
                    result.Add((object)new
                    {
                        sortKey = v.TranslatedTitle ?? v.Title,
                        title = $"<a href=\"https://www.youtube.com/watch?v={v.VideoId}\" target=\"_blank\">{Escape(v.TranslatedTitle ?? v.Title)}</a>",
                        description = Escape(v.TranslatedDescription ?? v.Description ?? ""),
                        duration = v.Duration.ToString(@"hh\:mm\:ss"),
                        channel = $"<a href=\"https://www.youtube.com/channel/{v.ChannelId}\" target=\"_blank\">{Escape(v.ChannelName)}</a>",
                        playlist = Escape(p.Title)
                    });
                }
            }
        }
        return result;
    }

    private static string BuildDropdownHtml(List<PlaylistSnapshot> sortedPlaylists)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<select id=\"playlist-dropdown\" onchange=\"switchPlaylist(this.value)\">");
        sb.AppendLine("<option value=\"playlist-list\">All Playlists</option>");
        foreach (var p in sortedPlaylists)
            sb.AppendLine($"<option value=\"playlist-{p.PlaylistId}\">{Escape(p.Title)}</option>");
        sb.AppendLine("</select>");
        return sb.ToString();
    }

    private static string BuildPlaylistViewHtml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div id=\"playlist-list\" class=\"view active\">");
        sb.AppendLine("<div class=\"search-row\">");
        sb.AppendLine("<input type=\"text\" id=\"playlist-search\" class=\"search-box\" placeholder=\"Search playlist names...\" oninput=\"filterPlaylistList(this.value)\">");
        sb.AppendLine("<input type=\"text\" id=\"all-videos-search\" class=\"search-box search-box-right\" placeholder=\"Search all playlists...\" oninput=\"filterAllVideos(this.value)\">");
        sb.AppendLine("</div>");
        sb.AppendLine("<div id=\"playlist-table\"></div>");
        sb.AppendLine("<div id=\"all-videos-table\" style=\"display:none\"></div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string BuildVideoViewsHtml(List<PlaylistSnapshot> sortedPlaylists)
    {
        var sb = new StringBuilder();
        foreach (var p in sortedPlaylists)
        {
            sb.AppendLine($"<div id=\"playlist-{p.PlaylistId}\" class=\"view\">");
            sb.AppendLine($"<h2>{Escape(p.Title)}</h2>");
            sb.AppendLine($"<div class=\"search-row\">");
            sb.AppendLine($"<input type=\"text\" id=\"video-search-{p.PlaylistId}\" class=\"search-box\" placeholder=\"Search in this playlist...\" oninput=\"filterPlaylistTable('{p.PlaylistId}', this.value)\">");
            sb.AppendLine($"<input type=\"text\" id=\"global-search-{p.PlaylistId}\" class=\"search-box search-box-right\" placeholder=\"Search all playlists...\" oninput=\"filterAllPlaylists(this.value)\">");
            sb.AppendLine($"</div>");
            sb.AppendLine($"<div id=\"video-table-{p.PlaylistId}\"></div>");
            sb.AppendLine($"</div>");
        }
        return sb.ToString();
    }

    private static string BuildVideoDataJs(
        Dictionary<string, List<object>> videoDataByPlaylist,
        List<object> allVideosData)
    {
        var sb = new StringBuilder();
        foreach (var kvp in videoDataByPlaylist)
            sb.AppendLine($"var videoData_{kvp.Key.Replace("-", "_")} = {System.Text.Json.JsonSerializer.Serialize(kvp.Value)};");
        sb.AppendLine($"var allVideosData = {System.Text.Json.JsonSerializer.Serialize(allVideosData)};");
        return sb.ToString();
    }

    public static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

public record DashboardData(
    int PlaylistCount,
    int VideoCount,
    List<object> PlaylistData,
    Dictionary<string, List<object>> VideoDataByPlaylist,
    List<object> AllVideosData,
    string DropdownHtml,
    string PlaylistViewHtml,
    string VideoViewsHtml,
    string VideoDataJs
);
