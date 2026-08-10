using System.Diagnostics;
using System.Linq;
using Core;

namespace Services.Audio;

using ErrorOr;

public sealed class ProcessRunner
{
	public async Task<ErrorOr<ProcessResult>> RunAsync(
		string binaryPath,
		string[] args,
		CancellationToken ct,
		string? workingDir = null,
		TimeSpan? timeout = null,
		TimeSpan? inactivityTimeout = null,
		Action<string>? onOutputLine = null
	)
	{
		if (!File.Exists(binaryPath) && !IsOnPath(binaryPath))
			return Errors.Audio.BinaryNotFound(Path.GetFileNameWithoutExtension(binaryPath));

		var binaryName = Path.GetFileNameWithoutExtension(binaryPath);
		Telemetry.Debug("ProcessRunner.Start binary={Binary} args={Args} workingDir={WorkingDir} timeout={Timeout}",
			binaryName, string.Join(" ", args.Select(EscapeArg)) ?? string.Empty, workingDir ?? ".", (double?)timeout?.TotalSeconds ?? 0);

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

		var sw = System.Diagnostics.Stopwatch.StartNew();
		try
		{
			using var process =
				Process.Start(psi)
				?? throw new InvalidOperationException($"Failed to start {binaryPath}");

			var stdoutSb = new System.Text.StringBuilder();
			var stderrSb = new System.Text.StringBuilder();

			var inactivityCts = new CancellationTokenSource();
			if (inactivityTimeout.HasValue)
			{
				inactivityCts.CancelAfter(inactivityTimeout.Value);
			}

			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, inactivityCts.Token);
			var linkedToken = linkedCts.Token;

			process.OutputDataReceived += (sender, e) =>
			{
				if (e.Data != null)
				{
					if (inactivityTimeout.HasValue)
					{
						inactivityCts.CancelAfter(inactivityTimeout.Value);
					}
					stdoutSb.AppendLine(e.Data);
					onOutputLine?.Invoke(e.Data);
				}
			};

			process.ErrorDataReceived += (sender, e) =>
			{
				if (e.Data != null)
				{
					if (inactivityTimeout.HasValue)
					{
						inactivityCts.CancelAfter(inactivityTimeout.Value);
					}
					stderrSb.AppendLine(e.Data);
					onOutputLine?.Invoke(e.Data);
				}
			};

			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			var exitTask = process.WaitForExitAsync(linkedToken);
			if (timeout is { } t)
			{
				var timeoutTask = Task.Delay(t, ct);
				var completed = await Task.WhenAny(exitTask, timeoutTask);
				if (completed == timeoutTask)
				{
					sw.Stop();
					process.Kill(entireProcessTree: true);
					Telemetry.Warn("ProcessRunner.Timeout binary={Binary} elapsed={ElapsedMs}ms",
						binaryName, sw.ElapsedMilliseconds);
					return Errors.Audio.ProcessFailed(
						binaryPath,
						$"Timed out after {t.TotalSeconds}s"
					);
				}
			}
			else
			{
				try
				{
					await exitTask;
				}
				catch (OperationCanceledException) when (inactivityCts.IsCancellationRequested)
				{
					sw.Stop();
					process.Kill(entireProcessTree: true);
					Telemetry.Warn("ProcessRunner.InactivityTimeout binary={Binary} elapsed={ElapsedMs}ms",
						binaryName, sw.ElapsedMilliseconds);
					return Errors.Audio.ProcessFailed(
						binaryPath,
						$"Timed out due to inactivity after {inactivityTimeout!.Value.TotalSeconds}s"
					);
				}
			}

			sw.Stop();

			var stdout = stdoutSb.ToString();
			var stderr = stderrSb.ToString();

			Telemetry.Debug("ProcessRunner.Complete binary={Binary} exitCode={ExitCode} elapsed={ElapsedMs}ms stdoutLen={StdoutLen} stderrLen={StderrLen}",
				binaryName, process.ExitCode, sw.ElapsedMilliseconds, stdout.Length, stderr.Length);

			if (stderr.Length > 0)
				Telemetry.Debug("ProcessRunner.Stderr binary={Binary} stderr={Stderr}",
					binaryName, stderr[..Math.Min(stderr.Length, 1000)]);

			return new ProcessResult(stdout, stderr, process.ExitCode);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			sw.Stop();
			Telemetry.Error("ProcessRunner.Failed binary={Binary} elapsed={ElapsedMs}ms error={Error}",
				binaryName, sw.ElapsedMilliseconds, ex.Message);
			return Errors.Audio.ProcessFailed(binaryPath, ex.Message);
		}
	}

	private static string EscapeArg(string arg) =>
		arg.Contains(' ') ? $"\"{arg}\"" : arg;

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
