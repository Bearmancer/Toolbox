using CLI;
using CLI.Azure;
using Core;
using DotNetEnv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

namespace App;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");

        if (!File.Exists(envPath))
        {
            await Console.Error.WriteLineAsync(
                $".env not found at {envPath}. Create one at the repo root with all required keys."
            );
            return 2;
        }

        Env.TraversePath().Load();
        Telemetry.Configure(args.Contains("--debug"));

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();

        try
        {
            new AzureCommandModule().ConfigureServices(services, configuration);
        }
        catch (InvalidOperationException ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Configuration error:[/] {ex.Message}");
            return 2;
        }

        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(cfg =>
        {
            cfg.SetApplicationName("app");
            cfg.SetApplicationVersion("1.0.0");
            new AzureCommandModule().ConfigureCommands(cfg);
        });

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        return await app.RunAsync(args, cts.Token);
    }
}
