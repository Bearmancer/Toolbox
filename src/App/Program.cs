using CLI;
using Core;
using Core.Infrastructure;
using Core.Logging;
using DotNetEnv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

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

        // 1. Load .env — SEQ_URL, endpoints must be available before anything else
        Env.TraversePath().Load();

        // 2. Configure Serilog — SEQ_URL is now present so the Seq sink registers correctly
        LogPipeline.Configure("app");

        // 3. Wire Ctrl+C and ProcessExit — flushes Serilog on shutdown
        Host.Initialize();

        // 4. Build configuration from env vars (already loaded by DotNetEnv)
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();

        // 5. Build DI container — each module registers its own services and clients
        var services = new ServiceCollection();
        var modules = CliModuleRegistry.GetAllModules();

        foreach (var module in modules)
        {
            try
            {
                module.ConfigureServices(services, configuration);
            }
            catch (InvalidOperationException ex)
            {
                AnsiConsole.MarkupLine($"[red]Configuration error:[/] {ex.Message}");
                return 2;
            }
        }

        // 6. Build Spectre app wired to DI — provider built exactly once via TypeRegistrar ??= cache
        var registrar = new TypeRegistrar(services);
        var app = new CommandApp(registrar);

        app.Configure(cfg =>
        {
            cfg.SetApplicationName("app");
            cfg.SetApplicationVersion("1.0.0");
            foreach (var module in modules)
                module.ConfigureCommands(cfg);
        });

        // 7. Run — Spectre resolves commands via TypeResolver → IServiceProvider
        return await app.RunAsync(args);
    }
}
