using System.Net.Sockets;
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

	public static async Task Configure(LogEventLevel level = LogEventLevel.Information)
	{
		LevelSwitch = new LoggingLevelSwitch(level);

		LoggerConfiguration? config = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(LevelSwitch)
			.Enrich.FromLogContext()
			.WriteTo.Spectre("{Timestamp:HH:mm:ss} {Level:u4} {Message:lj}{NewLine}{Exception}");

		foreach (ServiceName service in Enum.GetValues<ServiceName>())
			AddServiceLogger(config, service, $"logs/{service.ToFileSlug()}.jsonl");

		var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://localhost:5341";
		if (await IsSeqReachableAsync(seqUrl))
			_ = config.WriteTo.Seq(seqUrl);

		Log.Logger = config.CreateLogger();
	}

	private static void AddServiceLogger(
		LoggerConfiguration config,
		ServiceName service,
		string path
	)
	{
		_ = config.WriteTo.Logger(lc =>
			lc.Filter.ByIncludingOnly(e =>
					e.Properties.TryGetValue("Service", out LogEventPropertyValue? propValue)
					&& propValue is ScalarValue sv
					&& sv.Value is string serviceName
					&& serviceName == service.ToString()
				)
				.WriteTo.File(
					new CompactJsonFormatter(),
					path,
					rollingInterval: RollingInterval.Infinite,
					retainedFileCountLimit: null,
					fileSizeLimitBytes: 50 * 1024 * 1024
				)
		);
	}

	public static IDisposable ForService(ServiceName service) =>
		LogContext.PushProperty("Service", service.ToString());

	public static void Info(string template, params object[] args) =>
		Log.Write(LogEventLevel.Information, template, args);

	public static void Warn(string template, params object[] args) =>
		Log.Write(LogEventLevel.Warning, template, args);

	public static void Debug(string template, params object[] args) =>
		Log.Write(LogEventLevel.Debug, template, args);

	public static void Error(string template, params object[] args) =>
		Log.Write(LogEventLevel.Error, template, args);

	public static LoggerActivity StartActivity(string messageTemplate, params object[] args) =>
		Log.Logger.StartActivity(messageTemplate, args);

	private static async Task<bool> IsSeqReachableAsync(string seqUrl)
	{
		try
		{
			var uri = new Uri(seqUrl);
			using var client = new TcpClient();
			using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
			await client.ConnectAsync(uri.Host, uri.Port, cts.Token);
			return true;
		}
		catch (Exception ex)
			when (ex is SocketException or IOException or OperationCanceledException or UriFormatException)
		{
			return false;
		}
	}
}
