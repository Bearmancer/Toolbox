using System.ComponentModel;
using App.Services.Azure;
using Core;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Synthesize text to speech using Azure Speech Service")]
public class SpeechTtsCommand(SpeechTtsService service) : CommandBase<SpeechTtsCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        var result = await service.SynthesizeAsync(settings.Text, settings.Voice, settings.Out, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The text to synthesize into speech.")]
        [CommandArgument(0, "<text>")]
        public required string Text { get; init; }

        [Description("The voice to use.")]
        [CommandOption("--voice <VOICE>")]
        [DefaultValue("en-US-JennyNeural")]
        public string Voice { get; init; } = "en-US-JennyNeural";

        [Description("The output file path.")]
        [CommandOption("--out <PATH>")]
        [DefaultValue("output.wav")]
        public string Out { get; init; } = "output.wav";
    }
}
