namespace Core;

public static class ServiceNameExtensions
{
    public static string ToFileSlug(this ServiceName s) => s switch
    {
        ServiceName.LastFm        => "lastfm",
        ServiceName.Google        => "google",
        ServiceName.OpenAI        => "openai",
        ServiceName.Vision        => "vision",
        ServiceName.Translate     => "translate",
        ServiceName.TextAnalytics => "textanalytics",
        ServiceName.Speech        => "speech",
        ServiceName.DocIntel      => "docintel",
    };
}
