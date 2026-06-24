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
    private static LoggingLevelSwitch LevelSwitch { get; set; } = new(LogEventLevel.Information);

    public static void Configure(bool debug = false)
    {
        LevelSwitch = new LoggingLevelSwitch(debug ? LogEventLevel.Debug : LogEventLevel.Information);

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .WriteTo.File(
                new CompactJsonFormatter(),
                "logs/azureai-all-.jsonl",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("Service")
                    && e.Properties["Service"].ToString().Contains("Translate"))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "logs/azureai-translate-.jsonl",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7))
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("Service")
                    && e.Properties["Service"].ToString().Contains("YouTube"))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "logs/azureai-youtube-.jsonl",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7))
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("Service")
                    && e.Properties["Service"].ToString().Contains("Vision"))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "logs/azureai-vision-.jsonl",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7))
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("Service")
                    && e.Properties["Service"].ToString().Contains("Speech"))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "logs/azureai-speech-.jsonl",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7))
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("Service")
                    && e.Properties["Service"].ToString().Contains("TextAnalytics"))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "logs/azureai-textanalytics-.jsonl",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7))
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("Service")
                    && e.Properties["Service"].ToString().Contains("DocIntel"))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "logs/azureai-docintel-.jsonl",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7))
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("Service")
                    && e.Properties["Service"].ToString().Contains("OpenAI"))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    "logs/azureai-openai-.jsonl",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7))
            .WriteTo.Spectre(
                outputTemplate: "{Timestamp:HH:mm:ss} {Level:u4} {Message:lj}{NewLine}{Exception}");

        if (IsSeqReachable())
            config.WriteTo.Seq("http://localhost:5341");

        Log.Logger = config.CreateLogger();
    }

    public static IDisposable ForService(string service)
        => LogContext.PushProperty("Service", service);

    public static IDisposable ForService<T>()
        => LogContext.PushProperty("Service", typeof(T).Name.Replace("Service", ""));

    public static void Info(string template, params object[] args)
        => Log.Information(template, args);

    public static void Warn(string template, params object[] args)
        => Log.Warning(template, args);

    public static void Debug(string template, params object[] args)
        => Log.Debug(template, args);

    public static void Error(string template, params object[] args)
        => Log.Error(template, args);

    public static dynamic StartActivity(string messageTemplate, params object[] args)
        => Log.Logger.StartActivity(messageTemplate, args);

    private static bool IsSeqReachable()
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync("localhost", 5341);
            return task.Wait(500);
        }
        catch (SocketException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
