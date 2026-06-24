using System.Diagnostics;

namespace Core;

public sealed class DiagnosticObserver : IObserver<DiagnosticListener>
{
    public void OnNext(DiagnosticListener listener)
    {
        Telemetry.Debug("DiagnosticSource discovered: {Name}", listener.Name);
        if (listener.Name.Contains("HttpHandler") || listener.Name.Contains("Azure"))
            listener.Subscribe(new DiagnosticEventObserver(listener.Name));
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
}

public sealed class DiagnosticEventObserver(string sourceName) : IObserver<KeyValuePair<string, object?>>
{
    public void OnNext(KeyValuePair<string, object?> value)
    {
        Telemetry.Debug("[Diagnostic:{Source}] {Key}: {Value}", sourceName, value.Key, value.Value ?? "null");
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
}
