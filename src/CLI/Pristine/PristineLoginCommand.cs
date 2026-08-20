using System.ComponentModel;
using Services.Pristine;
using Spectre.Console.Cli;

namespace CLI.Pristine;

[Description(
	"Log in to Pristine Classical in a browser and save the session to state/auth/pristine/auth.json."
)]
public sealed class PristineLoginCommand(PristineLoginService service) : AsyncCommand
{
	protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken ct)
	{
		var ok = await service.LoginAsync(ct);
		return ok ? 0 : 1;
	}
}
