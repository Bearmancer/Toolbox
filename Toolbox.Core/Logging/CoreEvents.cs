namespace Toolbox.Core.Logging;

// ── Session lifecycle ────────────────────────────────────────────────────────

public record SessionStarted(string Service, string SessionId) : ILogEvent
{
    public string EventName => "SessionStarted";
    public Severity Severity => Severity.Info;
}

public record SessionEnded(string Service, string SessionId) : ILogEvent
{
    public string EventName => "SessionEnded";
    public Severity Severity => Severity.Info;
}

// ── Operation lifecycle ──────────────────────────────────────────────────────

public record OperationStarted(string Operation) : ILogEvent
{
    public string EventName => "OperationStarted";
    public Severity Severity => Severity.Debug;
}

public record OperationCompleted(string Operation, double ElapsedMs) : ILogEvent
{
    public string EventName => "OperationCompleted";
    public Severity Severity => Severity.Debug;
}

public record OperationFailed(string Operation, double ElapsedMs) : ILogEvent
{
    public string EventName => "OperationFailed";
    public Severity Severity => Severity.Error;
}

// ── HTTP / API ───────────────────────────────────────────────────────────────

public record ApiRequested(string Api, string Method, string Resource) : ILogEvent
{
    public string EventName => "ApiRequested";
    public Severity Severity => Severity.Debug;
}

public record ApiResponded(string Api, int StatusCode, double ElapsedMs) : ILogEvent
{
    public string EventName => "ApiResponded";

    public Severity Severity =>
        StatusCode >= 500 ? Severity.Error
        : StatusCode >= 400 ? Severity.Warning
        : Severity.Debug;
}

// ── Errors ───────────────────────────────────────────────────────────────────

public record ErrorOccurred(string Message, string? Context = null, string? ExceptionType = null) : ILogEvent
{
    public string EventName => "ErrorOccurred";
    public Severity Severity => Severity.Error;

    public static ErrorOccurred From(Exception ex, string? context = null) =>
        new(ex.Message, context, ex.GetType().Name);
}

public record FatalOccurred(string Message, string? ExceptionType = null) : ILogEvent
{
    public string EventName => "FatalOccurred";
    public Severity Severity => Severity.Fatal;

    public static FatalOccurred From(Exception ex) =>
        new(ex.Message, ex.GetType().Name);
}
