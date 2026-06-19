using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Azure;
using Toolbox.Core;
using Toolbox.Core.Screen;

namespace Toolbox.Commands.Azure;

[Description("Transcribe speech from audio files using Azure Speech Service")]
public class SpeechSttCommand : CommandBase<SpeechSttCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        var result = await SpeechSttService.TranscribeAsync(settings.File, settings.Lang, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<file>")]
        [Description("Path to the audio file to transcribe")]
        public string File { get; init; } = "";

        [CommandOption("--lang <LANG>")]
        [DefaultValue("en-US")]
        [Description("Language code for speech recognition (e.g., en-US, es-ES, fr-FR)")]
        public string Lang { get; init; } = "en-US";
    }
}