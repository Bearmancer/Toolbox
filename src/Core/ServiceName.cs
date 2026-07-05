namespace Core;

public enum ServiceName
{
    LastFm,
    YouTube,
    OpenAI,
    Vision,
    Translate,
    TextAnalytics,
    Speech,
    DocIntel
}

public static class ServiceNameMethods
{
    public static string ToFileSlug(this ServiceName s) => s switch
    {
        ServiceName.LastFm        => "lastfm",
        ServiceName.YouTube       => "youtube",
        ServiceName.OpenAI        => "openai",
        ServiceName.Vision        => "vision",
        ServiceName.Translate     => "translate",
        ServiceName.TextAnalytics => "textanalytics",
        ServiceName.Speech        => "speech",
        ServiceName.DocIntel      => "docintel",
    };
}
