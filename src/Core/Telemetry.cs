using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Spectre;
using SerilogTracing;

namespace Core;

public static class Telemetry
{
	private static LoggingLevelSwitch LevelSwitch { get; set; } = new();

	public static void Configure(LogEventLevel level = LogEventLevel.Information)
	{
		LevelSwitch = new LoggingLevelSwitch(level);

		LoggerConfiguration? config = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.Enrich.FromLogContext()
			.WriteTo.Logger(lc =>
				lc.MinimumLevel.ControlledBy(LevelSwitch)
					.WriteTo.Spectre(
						"{Timestamp:HH:mm:ss} [{Level:u4}] {Message:lj}{NewLine}{Exception}"
					)
			);

		var logDir = Path.Combine(PathResolver.RepoRoot, "state", "logs");
		Directory.CreateDirectory(logDir);

		foreach (ServiceName service in Enum.GetValues<ServiceName>())
			AddServiceLogger(
				config,
				service,
				Path.Combine(logDir, $"{service.ToFileSlug()}.jsonl")
			);

		var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL");
		if (!string.IsNullOrEmpty(seqUrl))
			_ = config.WriteTo.Seq(seqUrl);

		Serilog.Log.Logger = config.CreateLogger();
	}

	private static void AddServiceLogger(
		LoggerConfiguration config,
		ServiceName service,
		string path
	)
	{
		_ = config.WriteTo.Logger(lc =>
			lc.MinimumLevel.ControlledBy(LevelSwitch)
				.Filter.ByIncludingOnly(e =>
					e.Properties.TryGetValue("Service", out LogEventPropertyValue? propValue)
					&& propValue is ScalarValue sv
					&& sv.Value is string serviceName
					&& serviceName == service.ToString()
				)
				.WriteTo.File(
					new CompactJsonFormatter(),
					path,
					restrictedToMinimumLevel: LogEventLevel.Debug,
					rollingInterval: RollingInterval.Infinite,
					retainedFileCountLimit: null,
					fileSizeLimitBytes: 50 * 1024 * 1024
				)
		);
	}

	public static IDisposable ForService(ServiceName service) =>
		LogContext.PushProperty("Service", service.ToString());

	public static void Log(
		ServiceName service,
		LogEventLevel level,
		string template,
		params object[] args
	)
	{
		using IDisposable _ = ForService(service);
		Serilog.Log.Write(level, template, args);
	}

	public static void Info(string template, params object[] args) =>
		Serilog.Log.Write(LogEventLevel.Information, template, args);

	public static void Warn(string template, params object[] args) =>
		Serilog.Log.Write(LogEventLevel.Warning, template, args);

	public static void Debug(string template, params object[] args) =>
		Serilog.Log.Write(LogEventLevel.Debug, template, args);

	public static void Verbose(string template, params object[] args) =>
		Serilog.Log.Write(LogEventLevel.Verbose, template, args);

	public static void Error(string template, params object[] args) =>
		Serilog.Log.Write(LogEventLevel.Error, template, args);

	public static LoggerActivity StartActivity(string messageTemplate, params object[] args) =>
		Serilog.Log.Logger.StartActivity(LogEventLevel.Debug, messageTemplate, args);
}
