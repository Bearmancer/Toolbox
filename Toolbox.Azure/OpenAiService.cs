using OpenAI.Chat;
using Toolbox.Core;

namespace Toolbox.Azure;

public static class OpenAiService
{
    public static async Task<string> ChatAsync(
        string prompt,
        string? deployment = null,
        CancellationToken ct = default
    )
    {
        using var session = Logger.BeginSession(ServiceType.Azure);
        Logger.Starting("OpenAI.Chat");

        if (prompt.Length > Constants.OpenAiMaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(prompt),
                $"Prompt length {prompt.Length} exceeds 512K"
            );

        var modelDeployment = deployment ?? AppConfig.OpenAiDeployment ?? "gpt-4o-mini";
        var client = AzureClients.CreateOpenAiClient();
        var chat = client.GetChatClient(modelDeployment);
        var messages = new ChatMessage[] { new UserChatMessage(prompt) };

        Logger.ApiRequest("OpenAI", "CompleteChat", modelDeployment);
        var startTime = DateTime.UtcNow;
        var completion = await chat.CompleteChatAsync(
            messages,
            new ChatCompletionOptions(),
            ct
        );
        var elapsed = DateTime.UtcNow - startTime;
        Logger.ApiResponse("OpenAI", 200, elapsed);

        Logger.Complete("OpenAI.Chat");
        return $"Model: {modelDeployment}\n---\n{completion.Value.Content[0].Text}";
    }
}