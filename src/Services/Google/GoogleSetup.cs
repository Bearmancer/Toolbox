using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Google;

public static class GoogleSetup
{
    private static YouTubeService BuildYouTubeService(GoogleCredentials credentials)
    {
        var credential = GoogleWebAuthorizationBroker
            .AuthorizeAsync(
                new ClientSecrets
                {
                    ClientId = credentials.ClientId,
                    ClientSecret = credentials.ClientSecret,
                },
                [YouTubeService.Scope.Youtube],
                "user",
                CancellationToken.None
            )
            .GetAwaiter()
            .GetResult();

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
        public IServiceCollection AddGoogleServices()
        {
            var credentials = GoogleCredentials.Read();
            services.AddSingleton(credentials);

            services.AddSingleton(_ => BuildYouTubeService(credentials));
            services.AddSingleton<YoutubeService>();
            services.AddSingleton<YouTubeTranslationService>();
            services.AddSingleton<YouTubePlaylistProcessor>();
            services.AddSingleton<YouTubePlaylistOrchestrator>();

            return services;
        }
    }
}
