using System.Diagnostics.Tracing;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.AI.TextAnalytics;
using Azure.AI.Translation.Text;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Core.Diagnostics;
using Core;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.DependencyInjection;
using Services.Azure.Options;

namespace Services.Azure;

public static class AzureRegistration
{
    public static IServiceCollection AddAzureServices(this IServiceCollection services)
    {
        _ = new AzureEventSourceListener(
            (args, _) => Telemetry.Debug("{EventName}: {Payload}", args.EventName!, args.Payload!),
            EventLevel.Warning);

        var opts = ReadOptions();
        services.AddSingleton(opts);

        services.AddSingleton(new TextAnalyticsClient(
            new Uri(opts.TextAnalyticsEndpoint),
            new AzureKeyCredential(opts.TextAnalyticsKey)));
        services.AddSingleton<TextAnalyticsService>();

        services.AddSingleton(new TextTranslationClient(
            new AzureKeyCredential(opts.TranslatorKey),
            new Uri(opts.TranslatorEndpoint),
            opts.TranslatorRegion));
        services.AddSingleton<TranslateService>();

        services.AddSingleton(new DocumentIntelligenceClient(
            new Uri(opts.DocIntelEndpoint),
            new AzureKeyCredential(opts.DocIntelKey)));
        services.AddSingleton<DocIntelService>();

        services.AddSingleton(new ImageAnalysisClient(
            new Uri(opts.VisionEndpoint),
            new AzureKeyCredential(opts.VisionKey)));
        services.AddSingleton<VisionService>();

        services.AddSingleton(new AzureOpenAIClient(
            new Uri(opts.OpenAiEndpoint),
            new AzureKeyCredential(opts.OpenAiKey)));
        services.AddSingleton<OpenAiService>();

        services.AddSingleton<SpeechSttService>();
        services.AddSingleton<SpeechTtsService>();

        return services;
    }

    private static AzureOptions ReadOptions() => new()
    {
        TextAnalyticsEndpoint = Env("TEXT_ANALYTICS_ENDPOINT"),
        TextAnalyticsKey      = Env("TEXT_ANALYTICS_KEY"),
        TranslatorEndpoint    = Env("TRANSLATOR_ENDPOINT"),
        TranslatorKey         = Env("TRANSLATOR_KEY"),
        TranslatorRegion      = Env("TRANSLATOR_REGION"),
        DocIntelEndpoint      = Env("DOCINTEL_ENDPOINT"),
        DocIntelKey           = Env("DOCINTEL_KEY"),
        VisionEndpoint        = Env("VISION_ENDPOINT"),
        VisionKey             = Env("VISION_KEY"),
        OpenAiEndpoint        = Env("OPENAI_ENDPOINT"),
        OpenAiKey             = Env("OPENAI_KEY"),
        OpenAiDeployment      = Env("OPENAI_DEPLOYMENT"),
        SpeechEndpoint        = Env("SPEECH_ENDPOINT"),
        SpeechKey             = Env("SPEECH_KEY"),
        SpeechRegion          = Env("SPEECH_REGION"),
    };

    private static string Env(string key) =>
        Environment.GetEnvironmentVariable(key)
            ?? throw new InvalidOperationException($"Missing environment variable: {key}");
}
