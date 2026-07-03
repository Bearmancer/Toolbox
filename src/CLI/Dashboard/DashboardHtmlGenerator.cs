using System.Text;
using Services.Google.YouTube;

namespace CLI.Dashboard;

public static class DashboardHtmlGenerator
{
    public static string Generate(
        IReadOnlyList<PlaylistSnapshot> playlists,
        IReadOnlyList<YouTubeVideo> videos)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("<title>YouTube Dashboard</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;margin:2rem}");
        sb.AppendLine("table{border-collapse:collapse;width:100%}");
        sb.AppendLine("th,td{border:1px solid #ddd;padding:6px 10px;text-align:left}");
        sb.AppendLine("th{background:#f5f5f5}");
        sb.AppendLine("tr:nth-child(even){background:#fafafa}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"<h1>YouTube Dashboard</h1>");
        sb.AppendLine($"<p>{playlists.Count} playlists &middot; {videos.Count} videos</p>");

        sb.AppendLine("<h2>Playlists</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>#</th><th>Title</th><th>Playlist ID</th><th>Video Count</th><th>Last Updated</th></tr>");
        var idx = 0;
        foreach (var p in playlists)
        {
            idx++;
            sb.AppendLine($"<tr><td>{idx}</td><td>{Escape(p.Title)}</td><td>{p.PlaylistId}</td><td>{p.ReportedVideoCount}</td><td>{p.LastUpdated:yyyy-MM-dd}</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("<h2>Videos</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>#</th><th>Title</th><th>Translated Title</th><th>Channel</th><th>Duration</th><th>Language</th><th>Video ID</th></tr>");
        idx = 0;
        foreach (var v in videos)
        {
            idx++;
            sb.AppendLine($"<tr><td>{idx}</td><td>{Escape(v.Title)}</td><td>{Escape(v.TranslatedTitle ?? "")}</td><td>{Escape(v.ChannelName)}</td><td>{v.Duration:hh\\:mm\\:ss}</td><td>{v.DetectedLanguage ?? ""}</td><td>{v.VideoId}</td></tr>");
        }
        sb.AppendLine("</table>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
