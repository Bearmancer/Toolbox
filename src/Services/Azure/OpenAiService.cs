using System.ClientModel;
using Azure.AI.OpenAI;
using Core;
using ErrorOr;
using OpenAI.Chat;
using SerilogTracing;

namespace Services.Azure;

public class OpenAiService(AzureOpenAIClient client, AzureCredentials opts)
{
	private const int MaxChars = 512_000;

	public async Task<ErrorOr<string>> ChatAsync(
		string prompt,
		CancellationToken ct,
		string? deployment = null,
		string? systemPrompt = null,
		float? temperature = null,
		int? maxTokens = null
	)
	{
		if (prompt.Length > MaxChars)
			return Errors.Validation.InvalidInput(
				nameof(prompt),
				$"Prompt length {prompt.Length} exceeds 512K"
			);

		if (temperature is < 0.0f or > 2.0f)
			return Errors.Validation.InvalidInput(
				nameof(temperature),
				$"Temperature {temperature} is out of range 0.0-2.0"
			);

		var modelDeployment = deployment ?? opts.OpenAiDeployment;
		if (string.IsNullOrWhiteSpace(modelDeployment))
			return Errors.OpenAi.ApiError(
				"OpenAI deployment not configured. Set OPENAI_DEPLOYMENT in .env"
			);

		ChatClient? chat = client.GetChatClient(modelDeployment);

		List<ChatMessage> messageList = [];
		if (!string.IsNullOrWhiteSpace(systemPrompt))
			messageList.Add(new SystemChatMessage(systemPrompt));
		messageList.Add(new UserChatMessage(prompt));

		ChatCompletionOptions options = new();
		if (temperature is { } t)
			options.Temperature = t;
		if (maxTokens is { } mt)
			options.MaxOutputTokenCount = mt;

		using IDisposable _ = Telemetry.ForService(ServiceName.OpenAi);
		using LoggerActivity activity = Telemetry.StartActivity(
			"OpenAI.Chat {Deployment}",
			deployment ?? "default"
		);
		try
		{
			ClientResult<ChatCompletion>? completion = await chat.CompleteChatAsync(
				messageList,
				options,
				ct
			);
			activity.Complete();

			return $"Model: {modelDeployment}\n---\n{completion.Value.Content[0].Text}";
		}
		catch (Exception ex)
		{
			Telemetry.Error(
				"OpenAI: API error deployment={Deployment}: {Error}",
				modelDeployment,
				ex.Message
			);
			return Errors.OpenAi.ApiError(ex.Message);
		}
	}
}
