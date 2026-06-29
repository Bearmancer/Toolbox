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

public sealed class SerilogTraceListener : TraceListener
{
    public override void Write(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Telemetry.Debug("[TraceSource] {Message}", message.Trim());
    }

    public override void WriteLine(string? message) => Write(message);
}

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

        Trace.Listeners.Add(new SerilogTraceListener());

        await Telemetry.Configure(
            args.Contains("--verbose") ? LogEventLevel.Debug : LogEventLevel.Information
        );

        var services = new ServiceCollection();

        try
        {
            services.AddAzureServices();
            await services.AddGoogleServicesAsync();
            services.AddLastFmServices();
        }
        catch (InvalidOperationException ex)
        {
            Telemetry.Error("Configuration error: {Error}", ex.Message);
            return 2;
        }

        var registrar = new TypeRegistrar(services: services);
        var toolbox = new CommandApp(registrar: registrar);

        toolbox.Configure(cfg =>
        {
            cfg.SetApplicationName(name: "Toolbox");
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

        return await toolbox.RunAsync(args, appCts.Token);
    }
}
