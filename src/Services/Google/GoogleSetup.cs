using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Google;

public static class GoogleSetup
{
    public static IServiceCollection AddGoogleServices(this IServiceCollection services)
    {
        var credentials = GoogleCredentials.Read();
        services.AddSingleton(credentials);

        // Credential is user-interactive on first run; cached to %AppData% after.
        // Subsequent runs are silent (refresh token reused).
        services.AddSingleton(_ => BuildYouTubeService(credentials));
        services.AddSingleton<YoutubeService>();

        return services;
    }

    private static YouTubeService BuildYouTubeService(GoogleCredentials credentials)
    {
        var credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets
            {
                ClientId = credentials.ClientId,
                ClientSecret = credentials.ClientSecret,
            },
            [YouTubeService.Scope.Youtube],
            "user",
            CancellationToken.None
        ).GetAwaiter().GetResult();

        return new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AzureAI",
        });
    }


}
