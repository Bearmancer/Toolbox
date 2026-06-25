using Azure.AI.OpenAI;
using Core;
using OpenAI.Chat;

namespace Services.Azure;

public class OpenAiService(AzureOpenAIClient client, AzureCredentials opts)
{
    private const int MaxChars = 512_000;

    public async Task<string> ChatAsync(
        string prompt,
        CancellationToken ct,
        string? deployment = null,
        string? systemPrompt = null,
        float? temperature = null,
        int? maxTokens = null
    )
    {
        if (prompt.Length > MaxChars)
            throw new ArgumentOutOfRangeException(
                nameof(prompt),
                $"Prompt length {prompt.Length} exceeds 512K"
            );

        if (temperature is < 0.0f or > 2.0f)
            throw new ArgumentOutOfRangeException(
                nameof(temperature),
                $"Temperature {temperature} is out of range 0.0-2.0"
            );

        var modelDeployment = deployment ?? opts.OpenAiDeployment;
        if (string.IsNullOrWhiteSpace(modelDeployment))
            throw new InvalidOperationException(
                "OpenAI deployment not configured. Set OPENAI_DEPLOYMENT in .env"
            );

        var chat = client.GetChatClient(modelDeployment);

        var messageList = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messageList.Add(new SystemChatMessage(systemPrompt));
        messageList.Add(new UserChatMessage(prompt));

        var options = new ChatCompletionOptions();
        if (temperature is { } t)
            options.Temperature = t;
        if (maxTokens is { } mt)
            options.MaxOutputTokenCount = mt;

        var completion = await chat.CompleteChatAsync(messageList, options, ct)
            .WithTelemetry("OpenAI", "OpenAI.Chat {Deployment}", deployment ?? "default");

        return $"Model: {modelDeployment}\n---\n{completion.Value.Content[0].Text}";
    }
}
