using System.ComponentModel;
using App.Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Chat with Azure OpenAI models")]
public class ChatCommand(OpenAiService service) : AsyncCommand<ChatCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.ChatAsync(s.Prompt, s.Deployment, ct);
        AnsiConsole.MarkupLine(result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description("The prompt to send to the model.")]
        [CommandArgument(0, "<prompt>")]
        public required string Prompt { get; init; }

        [Description("The OpenAI deployment name to use (overrides configuration).")]
        [CommandOption("--deployment <NAME>")]
        public string? Deployment { get; init; }
    }
}
