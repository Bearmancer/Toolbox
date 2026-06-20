namespace Core;

public static class Host
{
    private static readonly CancellationTokenSource ShutdownCts = new();
    private static int Initialized;

    public static void Initialize()
    {
        if (System.Threading.Interlocked.Exchange(ref Initialized, 1) != 0)
            return;

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

    public static void Flush()
    {
        try
        {
            Console.Out.Flush();
            Console.Error.Flush();
            Core.Logging.LogPipeline.CloseAndFlush();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during shutdown flush: {ex.Message}");
        }
    }
}
