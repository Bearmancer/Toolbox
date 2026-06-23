using System.ComponentModel;
using Services.Azure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description("Chat with Azure OpenAI models")]
public class ChatCommand(OpenAiService service) : AsyncCommand<ChatCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(CommandContext ctx, Settings s, CancellationToken ct)
    {
        var result = await service.ChatAsync(
            s.Prompt,
            ct,
            s.Deployment,
            s.SystemPrompt,
            s.Temperature,
            s.MaxTokens
        );
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

        [Description("System prompt that sets the behavior and context for the model.")]
        [CommandOption("--system-prompt <PROMPT>")]
        public string? SystemPrompt { get; init; }

        [Description("Sampling temperature between 0.0 and 2.0. Higher values increase randomness (default: 1.0).")]
        [CommandOption("--temperature <TEMP>")]
        [DefaultValue(1.0f)]
        public float Temperature { get; init; } = 1.0f;

        [Description("Maximum number of tokens to generate in the response.")]
        [CommandOption("--max-tokens <N>")]
        public int? MaxTokens { get; init; }
    }
}
