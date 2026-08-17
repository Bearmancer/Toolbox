using System.Diagnostics;
using Core;

namespace Services.Audio;

using System.Text;
using ErrorOr;

public enum TerminationReason
{
	Exited,
	CallerCanceled,
	Timeout,
	InactivityTimeout,
	KilledAfterCompletionMarker,
	StartFailed,
}

public sealed class ProcessRunner
{
	public async Task<ErrorOr<ProcessResult>> RunAsync(
		string binaryPath,
		string[] args,
		CancellationToken ct,
		string? workingDir = null,
		TimeSpan? timeout = null,
		TimeSpan? inactivityTimeout = null,
		Action<string>? onOutputLine = null,
		string? completionPattern = null,
		TimeSpan? completionTimeout = null
	)
	{
		if (!File.Exists(binaryPath) && !IsOnPath(binaryPath))
			return Errors.Audio.BinaryNotFound(Path.GetFileNameWithoutExtension(binaryPath));

		var binaryName = Path.GetFileNameWithoutExtension(binaryPath);
		Telemetry.Debug(
			"ProcessRunner.Start binary={Binary} args={Args} workingDir={WorkingDir} timeout={Timeout}",
			binaryName,
			LogPaths.FormatText(string.Join(" ", args.Select(EscapeArg))),
			LogPaths.Format(workingDir ?? "."),
			(double?)timeout?.TotalSeconds ?? 0
		);

		ProcessStartInfo psi = new()
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

		Stopwatch sw = Stopwatch.StartNew();
		Process? process = null;
		TaskCompletionSource<bool>? stdoutDrainTcs = null;
		TaskCompletionSource<bool>? stderrDrainTcs = null;
		try
		{
			process = Process.Start(psi);
			if (process is null)
				return new ProcessResult(
					string.Empty,
					string.Empty,
					-1,
					TerminationReason.StartFailed
				);

				StringBuilder stdoutSb = new();
				StringBuilder stderrSb = new();
				stdoutDrainTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
				stderrDrainTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
				TaskCompletionSource<bool> completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
			var completionDetected = false;

			using CancellationTokenSource inactivityCts = new();
			if (inactivityTimeout.HasValue)
				inactivityCts.CancelAfter(inactivityTimeout.Value);

			using CancellationTokenSource linkedCts =
				CancellationTokenSource.CreateLinkedTokenSource(ct, inactivityCts.Token);
			CancellationToken linkedToken = linkedCts.Token;

			process.OutputDataReceived += (sender, e) =>
			{
					if (e.Data is null)
					{
						stdoutDrainTcs.TrySetResult(true);
						return;
					}
				if (inactivityTimeout.HasValue)
					inactivityCts.CancelAfter(inactivityTimeout.Value);
				stdoutSb.AppendLine(e.Data);
				onOutputLine?.Invoke(e.Data);
				if (completionPattern is not null && !completionDetected && e.Data.Contains(completionPattern))
				{
					completionDetected = true;
					completionTcs.TrySetResult(true);
					Telemetry.Debug(
						"ProcessRunner.CompletionDetected binary={Binary} pattern={Pattern}",
						binaryName,
						completionPattern
					);
				}
			};

			process.ErrorDataReceived += (sender, e) =>
			{
					if (e.Data is null)
					{
						stderrDrainTcs.TrySetResult(true);
						return;
					}
				if (inactivityTimeout.HasValue)
					inactivityCts.CancelAfter(inactivityTimeout.Value);
				stderrSb.AppendLine(e.Data);
				onOutputLine?.Invoke(e.Data);
			};

			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			Task exitTask = process.WaitForExitAsync(linkedToken);
			Task? timeoutTask = timeout.HasValue ? Task.Delay(timeout.Value) : null;
			Task? inactivityTask = inactivityTimeout.HasValue
				? Task.Delay(Timeout.InfiniteTimeSpan, inactivityCts.Token)
				: null;
				Task? graceTask = null;
				TerminationReason terminationReason = TerminationReason.Exited;
				async Task<ProcessResult> stopAndBuildAsync(TerminationReason reason)
				{
					await KillAndReapAsync(process, stdoutDrainTcs.Task, stderrDrainTcs.Task);
					return new ProcessResult(stdoutSb.ToString(), stderrSb.ToString(), process.ExitCode, reason);
				}

				while (true)
			{
				if (exitTask.IsCompleted)
				{
					if (process.HasExited)
						break;

					if (ct.IsCancellationRequested)
					{
						terminationReason = TerminationReason.CallerCanceled;
						throw new ProcessRunnerCanceledException(
							await stopAndBuildAsync(terminationReason),
							ct
						);
					}

					if (inactivityCts.IsCancellationRequested)
					{
						terminationReason = TerminationReason.InactivityTimeout;
						return await stopAndBuildAsync(terminationReason);
					}

					break;
				}

				List<Task> waitTasks = [exitTask];
				if (timeoutTask is not null)
					waitTasks.Add(timeoutTask);
				if (inactivityTask is not null)
					waitTasks.Add(inactivityTask);
				if (completionPattern is not null && graceTask is null)
					waitTasks.Add(completionTcs.Task);
				if (graceTask is not null)
					waitTasks.Add(graceTask);

				Task completed = await Task.WhenAny(waitTasks);
				if (completed == exitTask)
				{
					if (process.HasExited)
						break;

					if (ct.IsCancellationRequested)
					{
						terminationReason = TerminationReason.CallerCanceled;
						throw new ProcessRunnerCanceledException(
							await stopAndBuildAsync(terminationReason),
							ct
						);
					}
					if (inactivityCts.IsCancellationRequested)
					{
						terminationReason = TerminationReason.InactivityTimeout;
						return await stopAndBuildAsync(terminationReason);
					}
					break;
				}

				if (ct.IsCancellationRequested)
				{
					terminationReason = TerminationReason.CallerCanceled;
					throw new ProcessRunnerCanceledException(
						await stopAndBuildAsync(terminationReason),
						ct
					);
				}
				if (inactivityCts.IsCancellationRequested)
				{
					terminationReason = TerminationReason.InactivityTimeout;
					return await stopAndBuildAsync(terminationReason);
				}

				if (completed == inactivityTask)
				{
					terminationReason = TerminationReason.InactivityTimeout;
					return await stopAndBuildAsync(terminationReason);
				}

				if (completed == timeoutTask)
				{
					if (process.HasExited)
						break;

					terminationReason = TerminationReason.Timeout;
					Telemetry.Warn(
						"ProcessRunner.Timeout binary={Binary} elapsed={ElapsedMs}ms limit={LimitMs}ms",
						binaryName,
						sw.ElapsedMilliseconds,
						timeout!.Value.TotalMilliseconds
					);
					return await stopAndBuildAsync(terminationReason);
				}

				if (completed == completionTcs.Task)
				{
					graceTask = Task.Delay(
						completionTimeout ?? TimeSpan.FromSeconds(10),
						linkedToken
					);
					continue;
				}

				if (completed == graceTask)
				{
					terminationReason = TerminationReason.KilledAfterCompletionMarker;
					Telemetry.Info(
						"ProcessRunner.CompletionGraceKill binary={Binary} waited={WaitedMs}ms",
						binaryName,
						(int)(completionTimeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds
					);
					return await stopAndBuildAsync(terminationReason);
				}
			}

			sw.Stop();
			await DrainOutputAsync(process, stdoutDrainTcs.Task, stderrDrainTcs.Task);

			var stdout = stdoutSb.ToString();
			var stderr = stderrSb.ToString();
			var exitCode = process.ExitCode;

			Telemetry.Debug(
				"ProcessRunner.Complete binary={Binary} exitCode={ExitCode} elapsed={ElapsedMs}ms stdoutLen={StdoutLen} stderrLen={StderrLen}",
				binaryName,
				exitCode,
				sw.ElapsedMilliseconds,
				stdout.Length,
				stderr.Length
			);

			if (stderr.Length > 0)
				Telemetry.Debug(
					"ProcessRunner.Stderr binary={Binary} stderr={Stderr}",
					binaryName,
					stderr[..Math.Min(stderr.Length, 1000)]
				);

			return new ProcessResult(stdout, stderr, exitCode, terminationReason);
		}
		catch (ProcessRunnerCanceledException)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			if (process is not null)
				await KillAndReapAsync(process, stdoutDrainTcs?.Task, stderrDrainTcs?.Task);
			throw;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			if (process is not null)
				await KillAndReapAsync(process, stdoutDrainTcs?.Task, stderrDrainTcs?.Task);

			sw.Stop();
			Telemetry.Error(
				"ProcessRunner.Failed binary={Binary} elapsed={ElapsedMs}ms error={Error}",
				binaryName,
				sw.ElapsedMilliseconds,
				ex.Message
			);
			Telemetry.Warn(
				"ProcessRunner.StartFailed binary={Binary} error={Error}",
				binaryName,
				ex.Message
			);
			return new ProcessResult(string.Empty, string.Empty, -1, TerminationReason.StartFailed);
		}
		finally
		{
			process?.Dispose();
		}
	}

	private static async Task KillAndReapAsync(
		Process process,
		Task? stdoutDrain,
		Task? stderrDrain
	)
	{
		if (!process.HasExited)
			process.Kill(entireProcessTree: true);
		await DrainOutputAsync(process, stdoutDrain, stderrDrain);
	}

	private static async Task DrainOutputAsync(Process process, Task? stdoutDrain, Task? stderrDrain)
	{
		await process.WaitForExitAsync(CancellationToken.None);
		if (stdoutDrain is not null && stderrDrain is not null)
			await Task.WhenAll(stdoutDrain, stderrDrain);
	}

	private static string EscapeArg(string arg) => arg.Contains(' ') ? $"\"{arg}\"" : arg;

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

public sealed record ProcessResult(
	string Stdout,
	string Stderr,
	int ExitCode,
	TerminationReason TerminationReason
);

public sealed class ProcessRunnerCanceledException(ProcessResult result, CancellationToken cancellationToken)
	: OperationCanceledException(cancellationToken)
{
	public ProcessResult Result { get; } = result;
}
