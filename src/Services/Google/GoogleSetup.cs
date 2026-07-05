using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.DependencyInjection;
using Services.Google.YouTube;

namespace Services.Google;

public sealed class GoogleCredentials
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }

    public static GoogleCredentials Read() =>
        new() { ClientId = Env("GOOGLE_CLIENT_ID"), ClientSecret = Env("GOOGLE_CLIENT_SECRET") };

    private static string Env(string key) =>
        Environment.GetEnvironmentVariable(key)
        ?? throw new InvalidOperationException($"Missing: {key}");
}

public static class GoogleSetup
{
    private static async Task<YouTubeService> BuildYouTubeServiceAsync(
        GoogleCredentials credentials
    )
    {
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets
            {
                ClientId = credentials.ClientId,
                ClientSecret = credentials.ClientSecret,
            },
            [YouTubeService.Scope.Youtube],
            "user",
            CancellationToken.None
        );

        return new YouTubeService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AzureAI",
            }
        );
    }

    extension(IServiceCollection services)
    {
        public async Task<IServiceCollection> AddGoogleServicesAsync()
        {
            var credentials = GoogleCredentials.Read();
            services.AddSingleton(credentials);

            var youtubeService = await BuildYouTubeServiceAsync(credentials);
            services.AddSingleton(youtubeService);
            services.AddSingleton<YouTubePlaylistService>();
            services.AddSingleton<YouTubeVideoService>();
            services.AddSingleton<YouTubeSortService>();
            services.AddSingleton<YouTubeTranslationService>();
            services.AddSingleton<YouTubePlaylistProcessor>();
            services.AddSingleton<YouTubeSyncProcessor>();
            services.AddSingleton<YouTubePlaylistOrchestrator>();

            return services;
        }
    }
}
