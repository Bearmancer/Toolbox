using System.Diagnostics.Tracing;
using App.Services.Azure;
using App.Services.Azure.Options;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using Azure.AI.TextAnalytics;
using Azure.AI.Translation.Text;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Core.Diagnostics;
using Core;
using Microsoft.CognitiveServices.Speech;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console.Cli;

namespace CLI.Azure;

public class AzureCommandModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        _ = new AzureEventSourceListener((args, level) => Telemetry.Debug("{EventName}: {Payload}", args.EventName!, args.Payload!), EventLevel.Warning);

        services.Configure<AzureOptions>(o =>
        {
            o.TextAnalyticsEndpoint = configuration["TEXT_ANALYTICS_ENDPOINT"] ?? throw new InvalidOperationException("Missing TEXT_ANALYTICS_ENDPOINT");
            o.TextAnalyticsKey = configuration["TEXT_ANALYTICS_KEY"] ?? throw new InvalidOperationException("Missing TEXT_ANALYTICS_KEY");
            o.TranslatorEndpoint = configuration["TRANSLATOR_ENDPOINT"] ?? throw new InvalidOperationException("Missing TRANSLATOR_ENDPOINT");
            o.TranslatorKey = configuration["TRANSLATOR_KEY"] ?? throw new InvalidOperationException("Missing TRANSLATOR_KEY");
            o.TranslatorRegion = configuration["TRANSLATOR_REGION"] ?? throw new InvalidOperationException("Missing TRANSLATOR_REGION");
            o.DocIntelEndpoint = configuration["DOCINTEL_ENDPOINT"] ?? throw new InvalidOperationException("Missing DOCINTEL_ENDPOINT");
            o.DocIntelKey = configuration["DOCINTEL_KEY"] ?? throw new InvalidOperationException("Missing DOCINTEL_KEY");
            o.VisionEndpoint = configuration["VISION_ENDPOINT"] ?? throw new InvalidOperationException("Missing VISION_ENDPOINT");
            o.VisionKey = configuration["VISION_KEY"] ?? throw new InvalidOperationException("Missing VISION_KEY");
            o.OpenAiEndpoint = configuration["OPENAI_ENDPOINT"] ?? throw new InvalidOperationException("Missing OPENAI_ENDPOINT");
            o.OpenAiKey = configuration["OPENAI_KEY"] ?? throw new InvalidOperationException("Missing OPENAI_KEY");
            o.OpenAiDeployment = configuration["OPENAI_DEPLOYMENT"] ?? throw new InvalidOperationException("Missing OPENAI_DEPLOYMENT");
            o.SpeechEndpoint = configuration["SPEECH_ENDPOINT"] ?? throw new InvalidOperationException("Missing SPEECH_ENDPOINT");
            o.SpeechKey = configuration["SPEECH_KEY"] ?? throw new InvalidOperationException("Missing SPEECH_KEY");
            o.SpeechRegion = configuration["SPEECH_REGION"] ?? throw new InvalidOperationException("Missing SPEECH_REGION");
        });

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AzureOptions>>().Value;
            var cred = new AzureKeyCredential(o.TextAnalyticsKey!);
            return new TextAnalyticsClient(new Uri(o.TextAnalyticsEndpoint), cred);
        });
        services.AddSingleton<TextAnalyticsService>();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AzureOptions>>().Value;
            var cred = new AzureKeyCredential(o.TranslatorKey!);
            return new TextTranslationClient(cred, new Uri(o.TranslatorEndpoint!), o.TranslatorRegion!);
        });
        services.AddSingleton<TranslateService>();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AzureOptions>>().Value;
            var cred = new AzureKeyCredential(o.DocIntelKey!);
            return new DocumentIntelligenceClient(new Uri(o.DocIntelEndpoint!), cred);
        });
        services.AddSingleton<DocIntelService>();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AzureOptions>>().Value;
            var cred = new AzureKeyCredential(o.VisionKey!);
            return new ImageAnalysisClient(new Uri(o.VisionEndpoint!), cred);
        });
        services.AddSingleton<VisionService>();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AzureOptions>>().Value;
            var cred = new AzureKeyCredential(o.OpenAiKey!);
            return new AzureOpenAIClient(new Uri(o.OpenAiEndpoint!), cred);
        });
        services.AddSingleton<OpenAiService>();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<AzureOptions>>().Value;
            return SpeechConfig.FromEndpoint(new Uri(o.SpeechEndpoint!), o.SpeechKey!);
        });
        services.AddSingleton<SpeechSttService>();
        services.AddSingleton<SpeechTtsService>();

        services.AddTransient<SentimentCommand>();
        services.AddTransient<LanguageCommand>();
        services.AddTransient<NerCommand>();
        services.AddTransient<PhrasesCommand>();
        services.AddTransient<PiiCommand>();
        services.AddTransient<TranslateCommand>();
        services.AddTransient<DocIntelCommand>();
        services.AddTransient<VisionCommand>();
        services.AddTransient<ChatCommand>();
        services.AddTransient<SpeechSttCommand>();
        services.AddTransient<SpeechTtsCommand>();
    }

    public void ConfigureCommands(IConfigurator config)
    {
        config.AddBranch("azure", b =>
        {
            b.AddCommand<SentimentCommand>("sentiment");
            b.AddCommand<LanguageCommand>("language");
            b.AddCommand<NerCommand>("ner");
            b.AddCommand<PhrasesCommand>("phrases");
            b.AddCommand<PiiCommand>("pii");
            b.AddCommand<TranslateCommand>("translate");
            b.AddCommand<DocIntelCommand>("docintel");
            b.AddCommand<VisionCommand>("vision");
            b.AddCommand<ChatCommand>("chat");
            b.AddCommand<SpeechSttCommand>("stt");
            b.AddCommand<SpeechTtsCommand>("tts");
        });
    }
}
