using Microsoft.Extensions.DependencyInjection;
using Services.Google.Dashboard;
using Services.Google.YouTube;

namespace Services.Google;

public static class DashboardSetup
{
	extension(IServiceCollection services)
	{
		public IServiceCollection AddDashboardServices()
		{
			services.AddSingleton<DashboardService>();
			services.AddSingleton<DashboardOrchestrator>();
			return services;
		}
	}
}
