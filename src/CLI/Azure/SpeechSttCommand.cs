using System.ComponentModel;
using App.Services.Azure;
using Core;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Transcribe speech from audio files using Azure Speech Service")]
public class SpeechSttCommand(SpeechSttService service) : CommandBase<SpeechSttCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        var result = await service.TranscribeAsync(settings.File, settings.Lang, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [System.ComponentModel.Description("The path to the audio file to transcribe.")]
        [CommandArgument(0, "<file>")]
        public required string File { get; init; }

        [System.ComponentModel.Description("The language of the audio (default: en-US).")]
        [CommandOption("--lang <LANG>")]
        public string Lang { get; init; } = "en-US";
    }
}
