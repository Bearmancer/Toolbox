using System.Text.RegularExpressions;
using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Reader;

public static class WebExtractor
{
    public static async Task<ExtractedContent?> ExtractAsync(
        string url,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Reader);
        using var op = Log.BeginOperation("WebExtractor.Extract");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Log.Emit(new ErrorOccurred("Invalid URL", "WebExtractor.Extract"));
            return null;
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
        );

        Log.Emit(new ApiRequested("WebExtractor", "Extract", url));
        var startTime = DateTime.UtcNow;

        try
        {
            var response = await client.GetAsync(uri, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);
            Log.Emit(new ApiResponded("WebExtractor", (int)response.StatusCode, (DateTime.UtcNow - startTime).TotalMilliseconds));

            var content = ExtractContent(html, uri);

            op.Complete();
            return content;
        }
        catch (Exception ex)
        {
            Log.Emit(ErrorOccurred.From(ex, "Web extraction failed"));
            op.Fail();
            return null;
        }
    }

    private static ExtractedContent ExtractContent(string html, Uri sourceUrl)
    {
        var title = ExtractTitle(html);
        var content = ExtractMainContent(html);
        var wordCount = CountWords(content);

        return new ExtractedContent(title, null, content, html, sourceUrl, null, wordCount, null);
    }

    private static string ExtractTitle(string html)
    {
        var startTag = "<title>";
        var endTag = "</title>";
        var startIndex = html.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
            return "Untitled";

        startIndex += startTag.Length;
        var endIndex = html.IndexOf(endTag, startIndex, StringComparison.OrdinalIgnoreCase);
        return endIndex < 0
            ? "Untitled"
            : html[startIndex..endIndex].Trim();
    }

    private static string ExtractMainContent(string html)
    {
        var contentStart = html.IndexOf("<article", StringComparison.OrdinalIgnoreCase);
        if (contentStart < 0)
            contentStart = html.IndexOf("<main", StringComparison.OrdinalIgnoreCase);

        if (contentStart < 0)
        {
            var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyStart < 0)
                return html;
            contentStart = bodyStart;
        }

        var contentEnd = html.IndexOf("</body>", contentStart, StringComparison.OrdinalIgnoreCase);
        if (contentEnd < 0)
            contentEnd = html.Length;

        var content = html[contentStart..contentEnd];
        return StripHtmlTags(content);
    }

    private static string StripHtmlTags(string html)
    {
        var result = Regex.Replace(html, "<[^>]+>", " ");
        result = Regex.Replace(result, @"\s+", " ");
        return result.Trim();
    }

    private static int CountWords(string text) =>
        text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
}