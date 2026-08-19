using Services.Pristine;
using Spectre.Console.Cli;

namespace CLI.Pristine;

public sealed class PristineLoginCommand(PristineLoginService service) : AsyncCommand
{
	protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken ct)
	{
		var ok = await service.LoginAsync(ct);
		return ok ? 0 : 1;
	}
}
