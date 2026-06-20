using App.Services.Azure.Options;
using Azure.AI.OpenAI;
using Core.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace App.Services.Azure;

public class OpenAiService(AzureOpenAIClient client, IOptions<AzureOptions> opts)
{
    public async Task<string> ChatAsync(
        string prompt,
        string? deployment = null,
        CancellationToken ct = default
    )
    {
        using var op = Log.BeginOperation("OpenAI.Chat");

        if (prompt.Length > Constants.OpenAiMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(prompt),
                $"Prompt length {prompt.Length} exceeds 512K"
            );

        var modelDeployment = deployment ?? opts.Value.OpenAiDeployment ?? "gpt-4o-mini";
        var chat = client.GetChatClient(modelDeployment);
        var messages = new ChatMessage[] { new UserChatMessage(prompt) };

        Log.Emit(new ApiRequested("OpenAI", "CompleteChat", modelDeployment));
        var startTime = DateTime.UtcNow;
        var completion = await chat.CompleteChatAsync(messages, new ChatCompletionOptions(), ct);
        Log.Emit(new ApiResponded("OpenAI", 200, (DateTime.UtcNow - startTime).TotalMilliseconds));

        op.Complete();
        return $"Model: {modelDeployment}\n---\n{completion.Value.Content[0].Text}";
    }
}
