using System.Diagnostics.Tracing;
using Serilog.Events;

namespace Services.Azure;

internal static class EventLevelMapper
{
	public static LogEventLevel Map(EventLevel level) =>
		level switch
		{
			EventLevel.Critical or EventLevel.LogAlways => LogEventLevel.Error,
			EventLevel.Error => LogEventLevel.Error,
			EventLevel.Warning => LogEventLevel.Warning,
			EventLevel.Informational => LogEventLevel.Debug,
			EventLevel.Verbose => LogEventLevel.Verbose,
			_ => LogEventLevel.Debug,
		};
}
