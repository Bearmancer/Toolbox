using Serilog;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Core.Logging;

/// <summary>
/// Configures and initialises the Serilog pipeline.
/// Call <see cref="Configure"/> once at application startup before any log emission.
/// </summary>
public static class LogPipeline
{
    private static readonly string AppDataLogRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache",
        "logs"
    );

    /// <summary>
    /// Configures the global Serilog logger.
    /// </summary>
    /// <param name="applicationName">
    /// Used as the log directory name and the <c>Application</c> enrichment property.
    /// </param>
    /// <param name="minimumLevel">Minimum level for file and Seq sinks.</param>
    public static void Configure(
        string applicationName,
        LogEventLevel? minimumLevel = null
    )
    {
        var logDir = Path.Combine(AppDataLogRoot, applicationName.ToLowerInvariant());
        Directory.CreateDirectory(logDir);

        if (minimumLevel is { } explicitLevel)
            AppConfig.LogSwitch.MinimumLevel = explicitLevel;

        var levelSwitch = AppConfig.LogSwitch;

        var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL");

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch);

        foreach (var (category, level) in AppConfig.Overrides)
            config.MinimumLevel.Override(category, level);

        config
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", applicationName)
            .Enrich.WithProperty(
                "Instance",
                Environment.GetEnvironmentVariable("SEQ_INSTANCE") ?? "local"
            )
            .WriteTo.File(
                formatter: new CompactJsonFormatter(),
                path: Path.Combine(logDir, $"{applicationName.ToLowerInvariant()}-.jsonl"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true
            )
            .WriteTo.Console(
                theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            );

        if (!string.IsNullOrWhiteSpace(seqUrl))
        {
            config.WriteTo.Seq(seqUrl, controlLevelSwitch: levelSwitch);
        }

        Serilog.Log.Logger = config.CreateLogger();
        SelfLog.Enable(Console.Error);
    }

    /// <summary>
    /// Flushes all pending log events and closes the pipeline.
    /// Call on application exit.
    /// </summary>
    public static void CloseAndFlush() => Serilog.Log.CloseAndFlush();
}
