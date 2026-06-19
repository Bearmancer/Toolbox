namespace Toolbox.Core.Logging;

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
}
