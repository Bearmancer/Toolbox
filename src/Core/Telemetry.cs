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
    private static readonly string[] Services =
    [
        "Translate",
        "Google",
        "Vision",
        "Speech",
        "TextAnalytics",
        "DocIntel",
        "OpenAI",
        "Whisper",
    ];

    private static LoggingLevelSwitch LevelSwitch { get; set; } = new();

    public static void Configure(LogEventLevel level = LogEventLevel.Information)
    {
        LevelSwitch = new LoggingLevelSwitch(level);

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .WriteTo.File(
                new CompactJsonFormatter(),
                "logs/all-.jsonl",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7
            )
            .WriteTo.Spectre("{Timestamp:HH:mm:ss} {Level:u4} {Message:lj}{NewLine}{Exception}");

        foreach (var service in Services)
            AddServiceLogger(config, service, $"logs/{service.ToLowerInvariant()}-.jsonl");

        if (IsSeqReachable())
            config.WriteTo.Seq("http://localhost:5341");

        Log.Logger = config.CreateLogger();
    }

    private static void AddServiceLogger(LoggerConfiguration config, string service, string path)
    {
        config.WriteTo.Logger(lc =>
            lc.Filter.ByIncludingOnly(e =>
                    e.Properties["Service"].ToString().Equals(service, StringComparison.Ordinal)
                )
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    path,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7
                )
        );
    }

    public static IDisposable ForService(string service) =>
        LogContext.PushProperty("Service", service);

    public static void Info(string template, params object[] args) =>
        Log.Information(template, args);

    public static void Warn(string template, params object[] args) => Log.Warning(template, args);

    public static void Debug(string template, params object[] args) => Log.Debug(template, args);

    public static void Error(string template, params object[] args) => Log.Error(template, args);

    public static dynamic StartActivity(string messageTemplate, params object[] args) =>
        Log.Logger.StartActivity(messageTemplate, args);

    private static bool IsSeqReachable()
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync("localhost", 5341);
            return task.Wait(500);
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return false;
        }
    }
}
