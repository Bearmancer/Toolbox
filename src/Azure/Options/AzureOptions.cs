namespace Services.Azure.Options;

public class AzureOptions
{
    public required string TextAnalyticsEndpoint { get; set; }
    public required string TextAnalyticsKey { get; set; }
    public required string DocIntelEndpoint { get; set; }
    public required string DocIntelKey { get; set; }
    public required string VisionEndpoint { get; set; }
    public required string VisionKey { get; set; }
    public required string OpenAiEndpoint { get; set; }
    public required string OpenAiKey { get; set; }
    public required string OpenAiDeployment { get; set; }
    public required string SpeechEndpoint { get; set; }
    public required string SpeechKey { get; set; }
    public required string SpeechRegion { get; set; }
    public required string TranslatorEndpoint { get; set; }
    public required string TranslatorRegion { get; set; }
    public required string TranslatorKey { get; set; }
}
