using System.ComponentModel;
using Core;
using Services.Azure;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description(
    "Transcribe speech to text from audio files using Azure Speech Service. "
    + "Supports WAV (PCM 16-bit mono 16kHz recommended) and other common formats. "
    + "Returns plain transcript text."
)]
public class SpeechSttCommand(SpeechSttService service) : AsyncCommand<SpeechSttCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.TranscribeAsync(s.File, s.Lang, ct);
        Telemetry.Info("{Result}", result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description(
            "Path to the audio file to transcribe. "
            + "WAV (PCM 16-bit, mono, 16kHz) gives best results. "
            + "MP3 and other formats are supported via Azure's container handling."
        )]
        [CommandArgument(0, "<file>")]
        public required string File { get; init; }

        [Description(
            "Expected language of the audio in BCP-47 format, e.g. 'en-US', 'ja-JP', 'es-ES'. "
            + "Setting the correct locale improves recognition accuracy significantly. "
            + "(default: en-US)"
        )]
        [CommandOption("--lang <LANG>")]
        public string Lang { get; init; } = "en-US";
    }
}
