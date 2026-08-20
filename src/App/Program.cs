using System.Diagnostics;
using System.Diagnostics.Tracing;
using CLI;
using CLI.Audio;
using CLI.Azure;
using CLI.Dashboard;
using CLI.Pristine;
using CLI.Sync;
using Core;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Services.Audio;
using Services.Azure;
using Services.Google;
using Services.LastFm;
using Services.Pristine;
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

		LogEventLevel logLevel =
			args.Contains("--verbose") ? LogEventLevel.Verbose
			: args.Contains("--debug") ? LogEventLevel.Debug
			: LogEventLevel.Information;

		Telemetry.Configure(logLevel);

		var commandArgs = args.Where(a => a is not "--verbose" and not "--debug").ToArray();

		var enableDiagnostics = logLevel <= LogEventLevel.Debug;
		using AzureSdkEventListener? azureListener = enableDiagnostics
			? new(EventLevel.Verbose)
			: null;
		using ClientModelEventListener? clientModelListener = enableDiagnostics
			? new(EventLevel.Verbose)
			: null;
		using SpeechSdkEventListener? speechListener = enableDiagnostics
			? new(LogEventLevel.Debug)
			: null;
		speechListener?.Activate();

		ServiceCollection services = new();

		try
		{
			services.AddAzureServices();
			await services.AddGoogleServicesAsync();
			services.AddLastFmServices();
			services.AddAudioServices();
			services.AddPristineServices();
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

		TypeRegistrar registrar = new(services);
		CommandApp toolbox = new(registrar);

		toolbox.Configure(cfg =>
		{
			cfg.SetApplicationName("Toolbox");
			cfg.SetApplicationVersion("1.0.0");
			AzureCommandModule.ConfigureCommands(cfg);
			SyncCommandModule.ConfigureCommands(cfg);
			DashboardCommandModule.ConfigureCommands(cfg);
			AudioCommandModule.ConfigureCommands(cfg);
			PristineCommandModule.ConfigureCommands(cfg);
		});

		using CancellationTokenSource appCts = new();
		var cancelPresses = 0;
		Console.CancelKeyPress += (_, e) =>
		{
			cancelPresses++;
			if (cancelPresses > 1)
			{
				e.Cancel = false;
				return;
			}

			e.Cancel = true;
			Console.Error.WriteLine("Cancelling… press Ctrl+C again to force exit.");
			appCts.Cancel();
		};

		var exitCode = await toolbox.RunAsync(commandArgs, appCts.Token);
		await Serilog.Log.CloseAndFlushAsync();
		return exitCode;
	}
}
