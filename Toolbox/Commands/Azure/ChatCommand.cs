using System.ComponentModel;
using Spectre.Console.Cli;
using Toolbox.Azure;
using Toolbox.Core;
using Toolbox.Core.Screen;

namespace Toolbox.Commands.Azure;

[Description("Chat with Azure OpenAI models")]
public class ChatCommand : CommandBase<ChatCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await OpenAiService.ChatAsync(s.Prompt, s.Deployment, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<prompt>")]
        [Description("The prompt to send to the model")]
        public string Prompt { get; init; } = "";

        [CommandOption("--deployment <NAME>")]
        [Description("OpenAI deployment name (defaults to gpt-4o-mini)")]
        public string? Deployment { get; init; }
    }
}