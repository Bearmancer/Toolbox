using System.Diagnostics;
using CLI;
using CLI.Azure;
using CLI.Sync;
using Core;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Services.Azure;
using Services.Google;
using Services.LastFm;
using Spectre.Console.Cli;

namespace App;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");

        if (!File.Exists(path: envPath))
        {
            await Console.Error.WriteLineAsync(
                $".env not found at {envPath}. Create one at the repo root with all required keys."
            );
            return 2;
        }

        Env.TraversePath().Load();

        using var eventListener = new LoggingExplorerEventListener();
        DiagnosticListener.AllListeners.Subscribe(new DiagnosticObserver());
        Trace.Listeners.Add(new SerilogTraceListener());

        Telemetry.Configure(
            args.Contains(value: "--verbose") ? LogEventLevel.Debug : LogEventLevel.Information
        );

        var services = new ServiceCollection();

        try
        {
            services.AddAzureServices();
            services.AddGoogleServices();
            services.AddLastFmServices();
        }
        catch (InvalidOperationException ex)
        {
            Telemetry.Error("Configuration error: {Error}", ex.Message);
            return 2;
        }

        var registrar = new TypeRegistrar(services: services);
        var app = new CommandApp(registrar: registrar);

        app.Configure(cfg =>
        {
            cfg.SetApplicationName(name: "app");
            cfg.SetApplicationVersion(version: "1.0.0");
            AzureCommandModule.ConfigureCommands(cfg: cfg);
            SyncCommandModule.ConfigureCommands(cfg: cfg);
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
