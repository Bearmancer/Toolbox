using System.Diagnostics;

namespace Toolbox.Core.Logging;

/// <summary>
/// Tracks the lifecycle of a named operation — started when created,
/// completed or failed on dispose. Use inside a <c>using</c> block.
/// </summary>
public sealed class OperationScope : IDisposable
{
    private readonly string Name;
    private readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    private bool Completed;
    private bool Disposed;

    internal OperationScope(string name)
    {
        Name = name;
    }

    public TimeSpan Elapsed => Stopwatch.Elapsed;

    public void Complete()
    {
        Completed = true;
    }

    public void Fail()
    {
        Completed = false;
    }

    public void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;

        Stopwatch.Stop();
        var ms = Stopwatch.Elapsed.TotalMilliseconds;

        if (Completed)
            Log.Emit(new OperationCompleted(Name, ms));
        else
            Log.Emit(new OperationFailed(Name, ms));
    }
}
