namespace App;

public static class ManualIntegrationTest
{
    public static async Task RunAsync(IServiceProvider provider, CancellationToken ct)
    {
        await Tests.GoogleTests.RunAsync(provider, ct);
    }
}
