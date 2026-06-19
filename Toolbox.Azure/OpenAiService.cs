using OpenAI.Chat;
using Toolbox.Core;
using Toolbox.Core.Logging;

namespace Toolbox.Azure;

public static class OpenAiService
{
    public static async Task<string> ChatAsync(
        string prompt,
        string? deployment = null,
        CancellationToken ct = default
    )
    {
        using var session = Log.BeginSession(ServiceType.Azure);
        using var op = Log.BeginOperation("OpenAI.Chat");

        if (prompt.Length > Constants.OpenAiMaxChars)
            throw new ArgumentOutOfRangeException(nameof(prompt), $"Prompt length {prompt.Length} exceeds 512K");

        var modelDeployment = deployment ?? AppConfig.OpenAiDeployment ?? "gpt-4o-mini";
        var client = AzureClients.CreateOpenAiClient();
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