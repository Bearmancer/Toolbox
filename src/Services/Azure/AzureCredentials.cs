namespace Services.Azure;

public sealed class AzureCredentials
{
    public required string TextAnalyticsEndpoint { get; init; }
    public required string TextAnalyticsKey { get; init; }
    public required string TranslatorEndpoint { get; init; }
    public required string TranslatorKey { get; init; }
    public required string TranslatorRegion { get; init; }
    public required string DocIntelEndpoint { get; init; }
    public required string DocIntelKey { get; init; }
    public required string VisionEndpoint { get; init; }
    public required string VisionKey { get; init; }
    public required string OpenAiEndpoint { get; init; }
    public required string OpenAiKey { get; init; }
    public required string OpenAiDeployment { get; init; }
    public required string SpeechEndpoint { get; init; }
    public required string SpeechKey { get; init; }
    public required string SpeechRegion { get; init; }

    public static AzureCredentials Read() => new()
    {
        TextAnalyticsEndpoint = Env("TEXT_ANALYTICS_ENDPOINT"),
        TextAnalyticsKey = Env("TEXT_ANALYTICS_KEY"),
        TranslatorEndpoint = Env("TRANSLATOR_ENDPOINT"),
        TranslatorKey = Env("TRANSLATOR_KEY"),
        TranslatorRegion = Env("TRANSLATOR_REGION"),
        DocIntelEndpoint = Env("DOCINTEL_ENDPOINT"),
        DocIntelKey = Env("DOCINTEL_KEY"),
        VisionEndpoint = Env("VISION_ENDPOINT"),
        VisionKey = Env("VISION_KEY"),
        OpenAiEndpoint = Env("OPENAI_ENDPOINT"),
        OpenAiKey = Env("OPENAI_KEY"),
        OpenAiDeployment = Env("OPENAI_DEPLOYMENT"),
        SpeechEndpoint = Env("SPEECH_ENDPOINT"),
        SpeechKey = Env("SPEECH_KEY"),
        SpeechRegion = Env("SPEECH_REGION"),
    };

    private static string Env(string key) =>
        Environment.GetEnvironmentVariable(key)
            ?? throw new InvalidOperationException($"Missing: {key}");
}
