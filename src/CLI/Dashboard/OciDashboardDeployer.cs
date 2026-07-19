using Core;
using Renci.SshNet;

namespace CLI.Dashboard;

public static class OciDashboardDeployer
{
	private const string RemoteTmp = "/tmp";
	private const string RemoteDest = "/opt/dashboard";

	public static async Task DeployAsync(string dashboardDir, CancellationToken ct)
	{
		PrivateKeyFile key;
		try
		{
			key = new(OciConfig.KeyPath);
		}
		catch (Exception ex)
		{
			Telemetry.Warn(
				"OCI deploy: key not found at {Path} — {Error}",
				OciConfig.KeyPath,
				ex.Message
			);
			return;
		}

		PrivateKeyAuthenticationMethod auth = new(OciConfig.User, key);
		ConnectionInfo connInfo = new(OciConfig.Host, OciConfig.User, auth);

		var htmlFile = Path.Combine(dashboardDir, "dashboard.html");
		var dataFile = Path.Combine(dashboardDir, "dashboard-data.js");

		try
		{
			using SftpClient sftp = new(connInfo);
			sftp.Connect();

			await using var htmlStream = File.OpenRead(htmlFile);
			await sftp.UploadFileAsync(htmlStream, $"{RemoteTmp}/dashboard.html", ct);

			await using var dataStream = File.OpenRead(dataFile);
			await sftp.UploadFileAsync(dataStream, $"{RemoteTmp}/dashboard-data.js", ct);

			sftp.Disconnect();
		}
		catch (Exception ex)
		{
			Telemetry.Warn("OCI deploy: SFTP upload failed — {Error}", ex.Message);
			return;
		}

		var remoteCmd =
			$"sudo cp {RemoteTmp}/dashboard.html {RemoteTmp}/dashboard-data.js {RemoteDest}/ "
			+ $"&& sudo chown -R www-data:www-data {RemoteDest}/ "
			+ $"&& rm {RemoteTmp}/dashboard.html {RemoteTmp}/dashboard-data.js";

		try
		{
			using SshClient ssh = new(connInfo);
			ssh.Connect();
			using SshCommand cmd = ssh.RunCommand(remoteCmd);
			ssh.Disconnect();

			if (cmd.ExitStatus != 0)
			{
				Telemetry.Warn("OCI deploy: remote command failed — {Error}", cmd.Error.Trim());
				return;
			}
		}
		catch (Exception ex)
		{
			Telemetry.Warn("OCI deploy: SSH command failed — {Error}", ex.Message);
			return;
		}

		Telemetry.Info("OCI: dashboard live");
	}
}
