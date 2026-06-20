namespace Core.Logging;

public enum Severity
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal,
}

public interface ILogEvent
{
    string EventName { get; }
    Severity Severity { get; }

    /// <summary>
    /// Optional exception. When set, Serilog writes it to the native Exception
    /// slot — preserving the full stack trace, inner exceptions, and type
    /// information that Seq indexes and groups automatically.
    /// Defaults to <see langword="null"/> for events that carry no exception.
    /// </summary>
    Exception? Exception => null;
}
