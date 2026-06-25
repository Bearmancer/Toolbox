using Microsoft.Extensions.DependencyInjection;

namespace Services.LastFm;

public static class LastFmSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddLastFmServices()
        {
            var apiKey =
                Environment.GetEnvironmentVariable("LASTFM_API_KEY")
                ?? throw new InvalidOperationException("LASTFM_API_KEY not set in .env");
            var username =
                Environment.GetEnvironmentVariable("LASTFM_USERNAME")
                ?? throw new InvalidOperationException("LASTFM_USERNAME not set in .env");

            services.AddHttpClient(
                "LastFm",
                client =>
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("AzureAI/1.0");
                    client.Timeout = TimeSpan.FromSeconds(30);
                }
            );
            services.AddSingleton(sp =>
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                var client = factory.CreateClient("LastFm");
                return new LastFmService(client, apiKey, username);
            });
            return services;
        }
    }
}
