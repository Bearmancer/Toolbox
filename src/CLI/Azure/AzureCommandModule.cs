using Spectre.Console.Cli;

namespace CLI.Azure;

public static class AzureCommandModule
{
    public static void ConfigureCommands(IConfigurator cfg) =>
        cfg.AddBranch(
            "azure",
            b =>
            {
                b.SetDescription(
                    "Azure AI services — vision, translation, document intelligence, speech, and text analytics"
                );
                b.AddCommand<TranslateCommand>("translate");
                b.AddCommand<DocIntelCommand>("docintel");
                b.AddCommand<VisionCommand>("vision");
                b.AddCommand<SpeechSttCommand>("stt");
                b.AddCommand<NerCommand>("ner");
                b.AddCommand<PhrasesCommand>("phrases");
            }
        );
}
