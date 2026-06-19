using Spectre.Console.Cli;
using Toolbox.Core;

namespace Toolbox.Commands;

public class ConfigCommand : Command<ConfigCommand.Settings>
{
    protected override int Execute(CommandContext context, Settings settings, CancellationToken ct)
    {
        var mask = (string? s) =>
            settings.Reveal || string.IsNullOrEmpty(s) ? s ?? "(not set)" : $"{s[..4]}...{s[^4..]}";

        if (settings.Action is null or "show")
        {
            Ui.Info($"Endpoint:           {AppConfig.Endpoint}");
            Ui.Info($"Key:                {mask(AppConfig.Key)}");
            Ui.Info($"SpeechKey:          {mask(AppConfig.SpeechKey)}");
            Ui.Info($"SpeechRegion:       {AppConfig.SpeechRegion}");
            Ui.Info($"TranslatorRegion:   {AppConfig.TranslatorRegion}");
            Ui.Info($"OpenAIDeployment:   {AppConfig.OpenAiDeployment}");
            Ui.Info($"OpenAIEndpoint:     {AppConfig.OpenAiEndpoint}");
            Ui.Info($"OpenAIKey:          {mask(AppConfig.OpenAiKey)}");
            return 0;
        }

        if (settings.Action == "get" && settings.Key is not null)
        {
            var prop = typeof(AppConfig).GetProperty(settings.Key);
            if (prop is null)
            {
                Ui.Error($"Unknown key: {settings.Key}");
                return 2;
            }

            Ui.Debug(mask(prop.GetValue(null) as string));
            return 0;
        }

        Ui.Error("Usage: config show | config get <key> [--reveal]");
        return 2;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[action]")] public string? Action { get; init; }

        [CommandArgument(1, "[key]")] public string? Key { get; init; }

        [CommandOption("--reveal")] public bool Reveal { get; init; }
    }
}