using System.Diagnostics;
using Core;

namespace CLI.Dashboard;

public static class OciDashboardDeployer
{
	private static readonly string SshHost = "oci";
	private static readonly string RemoteTmp = "/tmp";
	private static readonly string RemoteDest = "/opt/dashboard";

	public static async Task DeployAsync(string dashboardDir, CancellationToken ct)
	{
		var htmlFile = Path.Combine(dashboardDir, "dashboard.html");
		var dataFile = Path.Combine(dashboardDir, "dashboard-data.js");

		if (!await RunAsync("scp", [htmlFile, dataFile, $"{SshHost}:{RemoteTmp}/"], ct))
		{
			Telemetry.Warn("OCI deploy: scp failed — dashboard not pushed");
			return;
		}

		var remoteCmd =
			$"sudo cp {RemoteTmp}/dashboard.html {RemoteTmp}/dashboard-data.js {RemoteDest}/ "
			+ $"&& sudo chown -R www-data:www-data {RemoteDest}/ "
			+ $"&& rm {RemoteTmp}/dashboard.html {RemoteTmp}/dashboard-data.js";

		if (!await RunAsync("ssh", [SshHost, remoteCmd], ct))
			Telemetry.Warn("OCI deploy: ssh copy failed — files may be stale");
		else
			Telemetry.Info("OCI: dashboard live");
	}

	private static async Task<bool> RunAsync(string exe, string[] args, CancellationToken ct)
	{
		var psi = new ProcessStartInfo
		{
			FileName = exe,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		foreach (var arg in args)
			psi.ArgumentList.Add(arg);

		try
		{
			using var process =
				Process.Start(psi)
				?? throw new InvalidOperationException($"{exe} failed to start");

			var stderrTask = process.StandardError.ReadToEndAsync(ct);
			await process.WaitForExitAsync(ct);
			var stderr = await stderrTask;

			if (process.ExitCode != 0)
			{
				if (!string.IsNullOrWhiteSpace(stderr))
					Telemetry.Debug("{Exe} stderr: {Stderr}", exe, stderr.Trim());
				return false;
			}

			return true;
		}
		catch (Exception ex)
		{
			Telemetry.Debug("{Exe} could not start: {Error}", exe, ex.Message);
			return false;
		}
	}
}
