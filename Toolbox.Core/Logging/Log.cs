using Serilog.Events;

namespace Toolbox.Core.Logging;

public static class Log
{
    // ── Emit ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The single entry point for all structured logging.
    /// </summary>
    public static void Emit(ILogEvent evt)
    {
        var level = ToSerilogLevel(evt.Severity);
        if (!Serilog.Log.IsEnabled(level))
            return;

        var service = ServiceContext.Current?.ServiceType.ToString() ?? "Unknown";
        var sessionId = ServiceContext.Current?.SessionId ?? "none";

        Serilog.Log.ForContext("Service", service)
            .ForContext("SessionId", sessionId)
            .Write(level, "{EventName} {@Event}", evt.EventName, evt);
    }

    // ── Session ───────────────────────────────────────────────────────────────

    public static IDisposable BeginSession(ServiceType serviceType)
    {
        var scope = ServiceContext.Begin(serviceType);
        var ctx = ServiceContext.Current!;

        Emit(new SessionStarted(ctx.ServiceType.ToString(), ctx.SessionId));

        return new SessionScope(scope, ctx.ServiceType, ctx.SessionId);
    }

    // ── Operation ─────────────────────────────────────────────────────────────

    public static OperationScope BeginOperation(string operationName)
    {
        Emit(new OperationStarted(operationName));
        return new OperationScope(operationName);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private static LogEventLevel ToSerilogLevel(Severity severity) =>
        severity switch
        {
            Severity.Debug => LogEventLevel.Debug,
            Severity.Info => LogEventLevel.Information,
            Severity.Warning => LogEventLevel.Warning,
            Severity.Error => LogEventLevel.Error,
            Severity.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information,
        };

    private sealed class SessionScope(
        IDisposable serviceScope,
        ServiceType serviceType,
        string sessionId
    ) : IDisposable
    {
        public void Dispose()
        {
            Emit(new SessionEnded(serviceType.ToString(), sessionId));
            serviceScope.Dispose();
        }
    }
}
