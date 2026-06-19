using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Serilog;
using Spectre.Console.Cli;
using Toolbox.Commands;
using Toolbox.Commands.Azure;
using Toolbox.Commands.Music;
using Toolbox.Commands.Reader;
using Toolbox.Commands.Sync;
using Toolbox.Core;

Log.Logger = LoggerBootstrap.CreateLogger("toolbox");
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

LoadConfig();
return await app.RunAsync(args);

static void LoadConfig()
{
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", true)
        .AddEnvironmentVariables("TOOLBOX_")
        .Build();

    AppConfig.Endpoint = config["Endpoint"];
    AppConfig.Key = config["Key"];
    AppConfig.SpeechKey = config["SpeechKey"];
    AppConfig.SpeechRegion = config["SpeechRegion"];
    AppConfig.TranslatorRegion = config["TranslatorRegion"];
    AppConfig.OpenAiDeployment = config["OpenAIDeployment"];
    AppConfig.OpenAiEndpoint = config["OpenAIEndpoint"];
    AppConfig.OpenAiKey = config["OpenAIKey"];
    AppConfig.SpotifyClientId = config["SpotifyClientId"];
    AppConfig.SpotifyClientSecret = config["SpotifyClientSecret"];
    AppConfig.LastFmApiKey = config["LastFmApiKey"];
    AppConfig.LastFmApiSecret = config["LastFmApiSecret"];
    AppConfig.DiscogsUserToken = config["DiscogsUserToken"];
    AppConfig.YouTubeApiKey = config["YouTubeApiKey"];
    AppConfig.GoogleClientId = config["GoogleClientId"];
    AppConfig.GoogleClientSecret = config["GoogleClientSecret"];
    AppConfig.LogLevel = config["LogLevel"];
    AppConfig.OutputFormat = config["OutputFormat"];
}