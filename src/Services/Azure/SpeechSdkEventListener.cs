using Microsoft.CognitiveServices.Speech.Diagnostics.Logging;
using Serilog;
using Serilog.Events;

namespace Services.Azure;

public sealed class SpeechSdkEventListener(LogEventLevel logLevel) : IDisposable
{
	private readonly EventHandler<string> _handler = (_, message) =>
	{
		Log.ForContext("Source", "SpeechSDK")
			.ForContext("Service", "SdkDiagnostics")
			.Write(logLevel, "{Message}", message);
	};

	public void Dispose() => EventLogger.OnMessage -= _handler;

	public void Activate()
	{
		var speechLevel = logLevel switch
		{
			LogEventLevel.Verbose => Level.Verbose,
			LogEventLevel.Debug => Level.Info,
			LogEventLevel.Information => Level.Info,
			LogEventLevel.Warning => Level.Warning,
			LogEventLevel.Error or LogEventLevel.Fatal => Level.Error,
			_ => Level.Warning,
		};
		EventLogger.SetLevel(speechLevel);
		EventLogger.OnMessage += _handler;
	}
}
