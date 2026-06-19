using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Spectre.Console.Cli;
using Toolbox.Commands;
using Toolbox.Commands.Azure;
using Toolbox.Commands.Music;
using Toolbox.Commands.Reader;
using Toolbox.Commands.Sync;
using Toolbox.Core;
using Toolbox.Core.Logging;

LogPipeline.Configure("toolbox");
AppState.Credential = new DefaultAzureCredential();

var app = new CommandApp();

app.Configure(config =>
{
    config.SetApplicationName("toolbox");
    config.SetApplicationVersion("1.0.0");
    config.AddCommand<DocIntelCommand>("docintel");
    config.AddCommand<SpeechSttCommand>("speech-stt");
    config.AddCommand<SpeechTtsCommand>("speech-tts");
    config.AddCommand<SentimentCommand>("sentiment");
    config.AddCommand<NerCommand>("ner");
    config.AddCommand<PhrasesCommand>("phrases");
    config.AddCommand<LanguageCommand>("language");
    config.AddCommand<PiiCommand>("pii");
    config.AddCommand<VisionCommand>("vision");
    config.AddCommand<TranslateCommand>("translate");
    config.AddCommand<ChatCommand>("chat");
    config.AddCommand<ConfigCommand>("config");
    config.AddCommand<MusicSearchCommand>("music-search");
    config.AddCommand<MusicReleaseCommand>("music-release");
    config.AddCommand<YouTubeListCommand>("youtube-list");
    config.AddCommand<LastFmSyncCommand>("lastfm-sync");
    config.AddCommand<WebExtractCommand>("web-extract");
    config.AddCommand<OcrCommand>("ocr");
});

loadConfig();
return await app.RunAsync(args);

static void loadConfig()
{
    DotNetEnv.Env.Load();
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddEnvironmentVariables("TOOLBOX_")
        .Build();

    AppConfig.Endpoint = config["ENDPOINT"];
    AppConfig.SpeechRegion = config["SPEECH_REGION"];
    AppConfig.TranslatorRegion = config["TRANSLATOR_REGION"];
    AppConfig.OpenAiDeployment = config["OPENAI_DEPLOYMENT"];
    AppConfig.OpenAiEndpoint = config["OPENAI_ENDPOINT"];
    AppConfig.LogLevel = config["LOG_LEVEL"];
    AppConfig.OutputFormat = config["OUTPUT_FORMAT"];
}