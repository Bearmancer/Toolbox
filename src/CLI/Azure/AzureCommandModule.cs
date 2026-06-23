using Spectre.Console.Cli;

namespace CLI.Azure;

public static class AzureCommandModule
{
    public static void ConfigureCommands(IConfigurator cfg) =>
        cfg.AddBranch("azure", b =>
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
