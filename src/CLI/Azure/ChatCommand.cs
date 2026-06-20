using System.ComponentModel;
using App.Services.Azure;
using Core;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Chat with Azure OpenAI models")]
public class ChatCommand(OpenAiService service) : CommandBase<ChatCommand.Settings>
{
    protected override async Task<int> ExecuteCommandAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.ChatAsync(s.Prompt, s.Deployment, ct);
        Ui.Info(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [System.ComponentModel.Description("The prompt to send to the model.")]
        [CommandArgument(0, "<prompt>")]
        public required string Prompt { get; init; }

        [System.ComponentModel.Description(
            "The OpenAI deployment name to use (overrides configuration)."
        )]
        [CommandOption("--deployment <NAME>")]
        public string? Deployment { get; init; }
    }
}
