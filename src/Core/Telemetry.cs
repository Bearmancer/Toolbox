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

    private static readonly ServiceName[] RegisteredServices =
    [
        ServiceName.LastFm,
        ServiceName.Google,
        ServiceName.OpenAI,
        ServiceName.Vision,
        ServiceName.Translate,
        ServiceName.TextAnalytics,
        ServiceName.Speech,
        ServiceName.DocIntel
    ];

    public static async Task Configure(LogEventLevel level = LogEventLevel.Information)
    {
        LevelSwitch = new LoggingLevelSwitch(level);

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .WriteTo.Spectre("{Timestamp:HH:mm:ss} {Level:u4} {Message:lj}{NewLine}{Exception}");

        foreach (var service in RegisteredServices)
            AddServiceLogger(
                config,
                service,
                $"logs/{service.ToString().ToLowerInvariant()}-.jsonl"
            );

        if (await IsSeqReachableAsync())
            config.WriteTo.Seq("http://localhost:5341");

        Log.Logger = config.CreateLogger();
    }

    private static void AddServiceLogger(
        LoggerConfiguration config,
        ServiceName service,
        string path
    )
    {
        config.WriteTo.Logger(lc =>
            lc.Filter.ByIncludingOnly(e =>
                    e.Properties["Service"].ToString().IsEqualTo(service.ToString())
                )
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    path,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7
                )
        );
    }

    public static IDisposable ForService(ServiceName service) =>
        LogContext.PushProperty("Service", service.ToString());

    public static void Info(string template, params object[] args) =>
        Log.Information(template, args);

    public static void Warn(string template, params object[] args) => Log.Warning(template, args);

    public static void Debug(string template, params object[] args) => Log.Debug(template, args);

    public static void Error(string template, params object[] args) => Log.Error(template, args);

    public static dynamic StartActivity(string messageTemplate, params object[] args) =>
        Log.Logger.StartActivity(messageTemplate, args);

    private static async Task<bool> IsSeqReachableAsync()
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await client.ConnectAsync("localhost", 5341, cts.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return false;
        }
    }
}
