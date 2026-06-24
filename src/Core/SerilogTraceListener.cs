using System.Diagnostics;

namespace Core;

public sealed class SerilogTraceListener : TraceListener
{
    public override void Write(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Telemetry.Debug("[TraceSource] {Message}", message.Trim());
    }

    public override void WriteLine(string? message) => Write(message);
}
