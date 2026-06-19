using System.Diagnostics.CodeAnalysis;

namespace Toolbox.Core;

[SuppressMessage("ReSharper", "UnusedType.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class Host
{
    private static readonly CancellationTokenSource ShutdownCts = new();
    private static bool Initialized;

    public static DateTime UtcNow => DateTime.UtcNow;

    public static CancellationToken ShutdownRequested => ShutdownCts.Token;

    public static void Initialize()
    {
        if (Initialized)
            return;
        Initialized = true;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            if (ShutdownCts.IsCancellationRequested)
                return;
            ShutdownCts.Cancel();
            Flush();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!ShutdownCts.IsCancellationRequested)
                ShutdownCts.Cancel();
            Flush();
        };
    }

    private static void Flush()
    {
        Console.Out.Flush();
        Console.Error.Flush();
    }
}