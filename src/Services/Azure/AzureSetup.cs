using System.Diagnostics.Tracing;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.AI.TextAnalytics;
using Azure.AI.Translation.Text;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Core.Diagnostics;
using Core;
using Microsoft.Extensions.DependencyInjection;


namespace Services.Azure;

public static class AzureSetup
{
    private static readonly AzureEventSourceListener EventListener = new(
        (args, _) => Telemetry.Debug("{EventName}: {Payload}", args.EventName!, args.Payload!),
        EventLevel.Warning);

    public static IServiceCollection AddAzureServices(this IServiceCollection services)
    {
        var credentials = AzureCredentials.Read();
        services.AddSingleton(credentials);

        services.AddSingleton(new TextAnalyticsClient(
            new Uri(credentials.TextAnalyticsEndpoint),
            new AzureKeyCredential(credentials.TextAnalyticsKey)));
        services.AddSingleton<TextAnalyticsService>();

        services.AddSingleton(new TextTranslationClient(
            new AzureKeyCredential(credentials.TranslatorKey),
            new Uri(credentials.TranslatorEndpoint),
            credentials.TranslatorRegion));
        services.AddSingleton<TranslateService>();

        services.AddSingleton(new DocumentIntelligenceClient(
            new Uri(credentials.DocIntelEndpoint),
            new AzureKeyCredential(credentials.DocIntelKey)));
        services.AddSingleton<DocIntelService>();

        services.AddSingleton(new ImageAnalysisClient(
            new Uri(credentials.VisionEndpoint),
            new AzureKeyCredential(credentials.VisionKey)));
        services.AddSingleton<VisionService>();

        services.AddSingleton(new AzureOpenAIClient(
            new Uri(credentials.OpenAiEndpoint),
            new AzureKeyCredential(credentials.OpenAiKey)));
        services.AddSingleton<OpenAiService>();

        services.AddSingleton<SpeechSttService>();
        services.AddSingleton<SpeechTtsService>();

        return services;
    }


}
