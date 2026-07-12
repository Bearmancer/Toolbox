using Spectre.Console.Cli;

namespace CLI.Dashboard;

public static class DashboardCommandModule
{
	public static void ConfigureCommands(IConfigurator cfg) =>
		cfg.AddBranch(
			"dashboard",
			b =>
			{
				b.SetDescription("Generate and manage the HTML dashboard");
				b.AddCommand<DashboardGenerateCommand>("generate");
			}
		);
}
