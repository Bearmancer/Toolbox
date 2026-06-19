using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Toolbox.Core.Logging;

/// <summary>
/// Configures and initialises the Serilog pipeline.
/// Call <see cref="Configure"/> once at application startup before any log emission.
/// </summary>
public static class LogPipeline
{
    private static readonly string AppDataLogRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cache", "logs"
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
        LogEventLevel minimumLevel = LogEventLevel.Debug
    )
    {
        var logDir = Path.Combine(AppDataLogRoot, applicationName.ToLowerInvariant());
        Directory.CreateDirectory(logDir);

        var levelSwitch = new LoggingLevelSwitch(minimumLevel);

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
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
            );

        var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL");
        if (!string.IsNullOrEmpty(seqUrl))
        {
            config.WriteTo.Seq(
                seqUrl,
                apiKey: Environment.GetEnvironmentVariable("SEQ_API_KEY"),
                controlLevelSwitch: levelSwitch
            );
        }

        Serilog.Log.Logger = config.CreateLogger();
    }

    /// <summary>
    /// Flushes all pending log events and closes the pipeline.
    /// Call on application exit.
    /// </summary>
    public static void CloseAndFlush() => Serilog.Log.CloseAndFlush();
}
