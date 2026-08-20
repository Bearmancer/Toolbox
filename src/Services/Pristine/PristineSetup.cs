using Microsoft.Extensions.DependencyInjection;

namespace Services.Pristine;

public static class PristineSetup
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddPristineServices()
		{
			services.AddSingleton<PristineDownloader>();
			services.AddSingleton<PristineAudioVerifier>();
			services.AddSingleton<PristineBrowser>();
			services.AddSingleton<PristineAlbumService>();
			services.AddSingleton<PristinePollService>();
			services.AddSingleton<PristineLoginService>();
			services.AddSingleton<PristineOrchestrator>();
			services.AddHttpClient();
			return services;
		}
	}
}
