using System.Diagnostics.Tracing;

namespace Core;

public sealed class LoggingExplorerEventListener : EventListener
{
    protected override void OnEventSourceCreated(EventSource source) =>
        Telemetry.Debug("EventSource discovered: {Name}", source.Name);

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (
            eventData.EventSource.Name.StartsWith(
                "System.Net.Http",
                StringComparison.Ordinal
            )
            || eventData.EventSource.Name.StartsWith(
                "Azure",
                StringComparison.Ordinal
            )
            || eventData.EventSource.Name.StartsWith(
                "Microsoft",
                StringComparison.Ordinal
            )
        )
            Telemetry.Debug(
                "[EventSource:{Source}] {Event} {Message}",
                eventData.EventSource.Name,
                eventData.EventName ?? "",
                eventData.Message
                ?? string.Join(
                    ", ",
                    eventData.Payload?.Select(p => p?.ToString() ?? "null") ?? []
                )
            );
    }
}
