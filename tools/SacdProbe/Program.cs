using System.Diagnostics;
using Microsoft.Win32;
using Services.Audio;

namespace SacdProbe;

internal static class Program
{
	public static int Main()
	{
		ProcessRunnerTests.Run().GetAwaiter().GetResult();
		Environment.Exit(0);
		if (!OperatingSystem.IsWindows())
		{
			Console.Error.WriteLine("PRECONDITION FAILED: SacdProbe requires Windows");
			return 3;
		}

		Console.WriteLine($"Identity: {System.Security.Principal.WindowsIdentity.GetCurrent().Name}");
		Console.WriteLine($"Session: {Environment.ProcessId} / {Process.GetCurrentProcess().SessionId}");
		Console.WriteLine($"ACP: {ReadAcp()}");
		return ProbeRunner.RunAll(ReadAcp());
	}

	private static int ReadAcp()
	{
		if (!OperatingSystem.IsWindows())
			return 0;

		return Registry.GetValue(
			@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Nls\CodePage",
			"ACP",
			"0"
		) is string value && int.TryParse(value, out var acp) ? acp : 0;
	}
}
