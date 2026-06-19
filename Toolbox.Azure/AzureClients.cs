using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.AI.TextAnalytics;
using Azure.AI.Translation.Text;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.CognitiveServices.Speech;

namespace Toolbox.Core;

public static class AzureClients
{
    public static DocumentIntelligenceClient CreateDocumentIntelligenceClient()
    {
        var endpoint =
            AppConfig.Endpoint ?? throw new InvalidOperationException("Endpoint not configured");
        var key = AppConfig.Key;

        return !string.IsNullOrEmpty(key)
            ? new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key))
            : new DocumentIntelligenceClient(new Uri(endpoint), AppState.Credential);
    }

    public static TextAnalyticsClient CreateTextAnalyticsClient()
    {
        var endpoint =
            AppConfig.Endpoint ?? throw new InvalidOperationException("Endpoint not configured");
        var key = AppConfig.Key;

        return !string.IsNullOrEmpty(key)
            ? new TextAnalyticsClient(new Uri(endpoint), new AzureKeyCredential(key))
            : new TextAnalyticsClient(new Uri(endpoint), AppState.Credential);
    }

    public static ImageAnalysisClient CreateImageAnalysisClient()
    {
        var endpoint =
            AppConfig.Endpoint ?? throw new InvalidOperationException("Endpoint not configured");
        var key = AppConfig.Key;

        return !string.IsNullOrEmpty(key)
            ? new ImageAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key))
            : new ImageAnalysisClient(new Uri(endpoint), AppState.Credential);
    }

    public static TextTranslationClient CreateTranslationClient()
    {
        var key = AppConfig.Key;
        var region =
            AppConfig.TranslatorRegion
            ?? throw new InvalidOperationException("TranslatorRegion not configured");
        var options = new TextTranslationClientOptions();

        return !string.IsNullOrEmpty(key)
            ? new TextTranslationClient(
                new AzureKeyCredential(key),
                new Uri("https://api.cognitive.microsofttranslator.com"),
                region,
                options
            )
            : new TextTranslationClient(
                AppState.Credential,
                new Uri("https://api.cognitive.microsofttranslator.com"),
                region,
                options
            );
    }

    public static AzureOpenAIClient CreateOpenAiClient()
    {
        var openAiEndpoint = AppConfig.OpenAiEndpoint;
        var endpoint = !string.IsNullOrEmpty(openAiEndpoint)
            ? openAiEndpoint
            : AppConfig.Endpoint ?? throw new InvalidOperationException("Endpoint not configured");
        var clientOptions = new AzureOpenAIClientOptions();

        if (!string.IsNullOrEmpty(AppConfig.OpenAiKey))
            return new AzureOpenAIClient(
                new Uri(endpoint),
                new AzureKeyCredential(AppConfig.OpenAiKey),
                clientOptions
            );
        return !string.IsNullOrEmpty(AppConfig.Key)
            ? new AzureOpenAIClient(
                new Uri(endpoint),
                new AzureKeyCredential(AppConfig.Key),
                clientOptions
            )
            : new AzureOpenAIClient(new Uri(endpoint), AppState.Credential, clientOptions);
    }

    public static SpeechConfig CreateSpeechConfig()
    {
        var speechKey =
            AppConfig.SpeechKey ?? throw new InvalidOperationException("SpeechKey not configured");
        var speechRegion =
            AppConfig.SpeechRegion
            ?? throw new InvalidOperationException("SpeechRegion not configured");

        return SpeechConfig.FromSubscription(speechKey, speechRegion);
    }
}