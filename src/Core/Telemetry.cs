using System.Net.Sockets;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using SerilogTracing;

namespace Core;

public static class Telemetry
{
    public static LoggingLevelSwitch LevelSwitch { get; private set; } = null!;

    public static void Configure(bool debug = false)
    {
        LevelSwitch = new LoggingLevelSwitch(debug ? LogEventLevel.Debug : LogEventLevel.Information);

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(LevelSwitch)
            .WriteTo.File("logs/app.log")
            .WriteTo.Spectre();

        if (IsSeqReachable())
            config.WriteTo.Seq("http://localhost:5341");

        Log.Logger = config.CreateLogger();
    }

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
        catch
        {
            return false;
        }
    }
}
