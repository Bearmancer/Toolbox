using Services.Azure.Options;
using Azure.AI.OpenAI;
using Core;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Services.Azure;

public class OpenAiService(AzureOpenAIClient client, IOptions<AzureOptions> opts)
{
    public async Task<string> ChatAsync(
        string prompt,
        CancellationToken ct,
        string? deployment = null
    )
    {
        using var activity = Telemetry.StartActivity("OpenAI.Chat {Deployment}", deployment ?? "default");

        if (prompt.Length > Constants.OpenAiMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(prompt),
                $"Prompt length {prompt.Length} exceeds 512K"
            );

        var modelDeployment = deployment ?? opts.Value.OpenAiDeployment ?? "gpt-4o-mini";
        var chat = client.GetChatClient(modelDeployment);
        var messages = new ChatMessage[] { new UserChatMessage(prompt) };

        var completion = await chat.CompleteChatAsync(messages, new ChatCompletionOptions(), ct);
        activity.Complete();
        return $"Model: {modelDeployment}\n---\n{completion.Value.Content[0].Text}";
    }
}
