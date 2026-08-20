using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.TextAnalytics;
using Azure.AI.Translation.Text;
using Azure.AI.Vision.ImageAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Azure;

public static class AzureSetup
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddAzureServices()
		{
			AzureCredentials credentials = AzureCredentials.Read();
			services.AddSingleton(credentials);

			services.AddSingleton(
				new TextAnalyticsClient(
					new Uri(credentials.TextAnalyticsEndpoint),
					new AzureKeyCredential(credentials.TextAnalyticsKey)
				)
			);
			services.AddSingleton<TextAnalyticsService>();

			services.AddSingleton(
				new TextTranslationClient(
					new AzureKeyCredential(credentials.TranslatorKey),
					new Uri(credentials.TranslatorEndpoint),
					credentials.TranslatorRegion
				)
			);
			services.AddSingleton<TranslateService>();

			services.AddSingleton(
				new DocumentIntelligenceClient(
					new Uri(credentials.DocIntelEndpoint),
					new AzureKeyCredential(credentials.DocIntelKey)
				)
			);
			services.AddSingleton<DocIntelService>();

			services.AddSingleton(
				new ImageAnalysisClient(
					new Uri(credentials.VisionEndpoint),
					new AzureKeyCredential(credentials.VisionKey)
				)
			);
			services.AddSingleton<VisionService>();

			services.AddSingleton<SpeechService>();

			return services;
		}
	}
}
