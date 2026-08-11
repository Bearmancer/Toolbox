using System.Diagnostics.Tracing;
using Azure.Core.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Services.Azure;

public sealed class AzureSdkEventListener(EventLevel captureLevel) : IDisposable
{
	private static readonly string[] AllowedEventSources = ["Azure-Core", "Azure-Identity"];

	private readonly AzureEventSourceListener _listener = new(OnEventWritten, captureLevel);

	private static void OnEventWritten(EventWrittenEventArgs eventData, string formattedMessage)
	{
		var sourceName = eventData.EventSource.Name;

		if (!AllowedEventSources.Contains(sourceName))
			return;

		var level = MapLevel(eventData.Level);

		Log.ForContext("Source", "AzureSDK")
			.ForContext("EventSource", sourceName)
			.ForContext("Service", "SdkDiagnostics")
			.Write(level, "{Message}", formattedMessage);
	}

	private static LogEventLevel MapLevel(EventLevel level) => EventLevelMapper.Map(level);

	public void Dispose() => _listener.Dispose();
}
