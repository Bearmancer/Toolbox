using System.ComponentModel;
using App.Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Transcribe speech from audio files using Azure Speech Service")]
public class SpeechSttCommand(SpeechSttService service) : AsyncCommand<SpeechSttCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.TranscribeAsync(s.File, s.Lang, ct);
        AnsiConsole.MarkupLine(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The path to the audio file to transcribe.")]
        [CommandArgument(0, "<file>")]
        public required string File { get; init; }

        [Description("The language of the audio (default: en-US).")]
        [CommandOption("--lang <LANG>")]
        public string Lang { get; init; } = "en-US";
    }
}
