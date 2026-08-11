using System.Diagnostics;
using System.Diagnostics.Tracing;
using CLI;
using CLI.Audio;
using CLI.Azure;
using CLI.Dashboard;
using CLI.Sync;
using Core;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Services.Audio;
using Services.Azure;
using Services.Google;
using Services.LastFm;
using Spectre.Console.Cli;

namespace App;

internal sealed class SerilogTraceListener : TraceListener
{
	public override void Write(string? message)
	{
		if (!string.IsNullOrWhiteSpace(message))
			Telemetry.Debug("[TraceSource] {Message}", message.Trim());
	}

	public override void WriteLine(string? message) => Write(message);
}

internal static class Program
{
	public static async Task<int> Main(string[] args)
	{
		var envPath = Path.Combine(PathResolver.RepoRoot, ".env");

		if (!File.Exists(envPath))
		{
			Telemetry.Error(
				".env not found at {Path}. Create one at the repo root with all required keys.",
				envPath
			);
			return 2;
		}

		Env.Load(envPath);

		Trace.Listeners.Add(new SerilogTraceListener());

		var logLevel =
			args.Contains("--verbose") ? LogEventLevel.Verbose
			: args.Contains("--debug") ? LogEventLevel.Debug
			: LogEventLevel.Information;

		await Telemetry.Configure(logLevel);

		var commandArgs = args.Where(a => a is not "--verbose" and not "--debug").ToArray();

		var enableDiagnostics = logLevel <= LogEventLevel.Debug;
		using var azureListener = enableDiagnostics
			? new AzureSdkEventListener(EventLevel.Verbose)
			: null;
		using var clientModelListener = enableDiagnostics
			? new ClientModelEventListener(EventLevel.Verbose)
			: null;
		using var speechListener = enableDiagnostics
			? new SpeechSdkEventListener(LogEventLevel.Debug)
			: null;
		speechListener?.Activate();

		var services = new ServiceCollection();

		var isAudioOnly = commandArgs.Contains("audio");

		try
		{
			if (!isAudioOnly)
			{
				services.AddAzureServices();
				await services.AddGoogleServicesAsync();
				services.AddLastFmServices();
			}
			services.AddAudioServices();
		}
		catch (InvalidOperationException ex)
		{
			var msg = $"[STARTUP FAILURE] {ex.GetType().Name}: {ex.Message}";
			Telemetry.Error("{StartupFailure}\n{StackTrace}", msg, ex.StackTrace ?? "");
			await Console.Error.WriteLineAsync(msg);
			await Serilog.Log.CloseAndFlushAsync();
			return 2;
		}
		catch (OperationCanceledException ex)
		{
			var msg = $"[STARTUP FAILURE] {ex.GetType().Name}: {ex.Message}";
			Telemetry.Error("{StartupFailure}\n{StackTrace}", msg, ex.StackTrace ?? "");
			await Console.Error.WriteLineAsync(msg);
			await Serilog.Log.CloseAndFlushAsync();
			return 2;
		}

		var registrar = new TypeRegistrar(services);
		var toolbox = new CommandApp(registrar);

		toolbox.Configure(cfg =>
		{
			cfg.SetApplicationName("Toolbox");
			cfg.SetApplicationVersion("1.0.0");
			AzureCommandModule.ConfigureCommands(cfg);
			SyncCommandModule.ConfigureCommands(cfg);
			DashboardCommandModule.ConfigureCommands(cfg);
			AudioCommandModule.ConfigureCommands(cfg);
		});

		using var appCts = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			appCts.Cancel();
		};

		var exitCode = await toolbox.RunAsync(commandArgs, appCts.Token);
		await Serilog.Log.CloseAndFlushAsync();
		return exitCode;
	}
}
