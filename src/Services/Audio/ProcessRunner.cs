using System.Diagnostics;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class ProcessRunner
{
	public async Task<ErrorOr<ProcessResult>> RunAsync(
		string binaryPath,
		string[] args,
		CancellationToken ct,
		string? workingDir = null
	)
	{
		if (!File.Exists(binaryPath) && !IsOnPath(binaryPath))
			return Errors.Audio.BinaryNotFound(Path.GetFileNameWithoutExtension(binaryPath));

		var psi = new ProcessStartInfo
		{
			FileName = binaryPath,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
			WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
		};

		foreach (var arg in args)
			psi.ArgumentList.Add(arg);

		try
		{
			using var process =
				Process.Start(psi)
				?? throw new InvalidOperationException($"Failed to start {binaryPath}");

			var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
			var stderrTask = process.StandardError.ReadToEndAsync(ct);
			await Task.WhenAll(stdoutTask, stderrTask);

			var stdout = await stdoutTask;
			var stderr = await stderrTask;

			await process.WaitForExitAsync(ct);

			return new ProcessResult(stdout, stderr, process.ExitCode);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			return Errors.Audio.ProcessFailed(binaryPath, ex.Message);
		}
	}

	public static bool IsOnPath(string binaryName)
	{
		if (Path.IsPathRooted(binaryName))
			return File.Exists(binaryName);

		var path = Environment.GetEnvironmentVariable("PATH");
		if (path is null)
			return false;

		var dirs = path.Split(Path.PathSeparator);
		return dirs.Any(d =>
			File.Exists(Path.Combine(d, binaryName))
			|| File.Exists(Path.Combine(d, binaryName + ".exe"))
		);
	}
}

public sealed record ProcessResult(string Stdout, string Stderr, int ExitCode);
