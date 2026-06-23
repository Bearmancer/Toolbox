using CLI;
using CLI.Azure;
using Core;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Services.Azure;
using Services.Google;
using Spectre.Console;
using Spectre.Console.Cli;
using CLI.Google;

namespace App;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");

        if (!File.Exists(envPath))
        {
            await Console.Error.WriteLineAsync(
                $".env not found at {envPath}. Create one at the repo root with all required keys.");
            return 2;
        }

        Env.TraversePath().Load();
        Telemetry.Configure(args.Contains("--debug"));

        var services = new ServiceCollection();

        try
        {
            services.AddAzureServices();
            services.AddGoogleServices();
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Configuration error:[/] {ex.Message}");
            return 2;
        }

        if (args.Contains("--test"))
        {
            using var cts = new CancellationTokenSource();
            var provider = services.BuildServiceProvider();
            await ManualIntegrationTest.RunAsync(provider, cts.Token);
            return 0;
        }

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(cfg =>
        {
            cfg.SetApplicationName("app");
            cfg.SetApplicationVersion("1.0.0");
            AzureCommandModule.ConfigureCommands(cfg);
            GoogleCommandModule.ConfigureCommands(cfg);
        });

        using var appCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            appCts.Cancel();
        };

        return await app.RunAsync(args, appCts.Token);
    }
}
