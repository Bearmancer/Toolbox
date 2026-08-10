using System.Diagnostics.Tracing;
using Serilog;
using Serilog.Events;

namespace Services.Azure;

public sealed class ClientModelEventListener(EventLevel captureLevel) : IDisposable
{
    private readonly InternalListener _listener = new(captureLevel);

    private sealed class InternalListener(EventLevel level) : EventListener
    {
        private readonly EventLevel _level = level;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "System.ClientModel")
                EnableEvents(eventSource, _level);
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            var message = eventData.Message is not null && eventData.Payload is not null
                ? string.Format(eventData.Message, [.. eventData.Payload])
                : eventData.EventName;

            Log.ForContext("Source", "AzureSDK")
               .ForContext("EventSource", eventData.EventSource.Name)
               .ForContext("Service", "SdkDiagnostics")
               .Write(MapLevel(eventData.Level), "{Message}", message);
        }

        private static LogEventLevel MapLevel(EventLevel level) => EventLevelMapper.Map(level);
    }

    public void Dispose() => _listener.Dispose();
}
