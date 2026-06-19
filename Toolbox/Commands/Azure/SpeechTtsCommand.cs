using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Azure;
using Toolbox.Core;

namespace Toolbox.Commands.Azure;

public class SpeechTtsCommand : CommandBase<SpeechTtsCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext context,
        Settings settings,
        CancellationToken ct
    )
    {
        var result = await SpeechTtsService.SynthesizeAsync(
            settings.Text,
            settings.Voice,
            settings.Out
        );
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<text>")] public string Text { get; init; } = "";

        [CommandOption("--voice <VOICE>")]
        [DefaultValue("en-US-JennyNeural")]
        public string Voice { get; init; } = "en-US-JennyNeural";

        [CommandOption("--out <PATH>")]
        [DefaultValue("output.wav")]
        public string Out { get; init; } = "output.wav";
    }
}