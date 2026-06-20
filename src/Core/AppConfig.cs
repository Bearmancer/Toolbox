using Serilog.Core;
using Serilog.Events;

namespace Core;

public static class AppConfig
{
    public static LoggingLevelSwitch LogSwitch { get; }

    public static Dictionary<string, LogEventLevel> Overrides { get; }

    static AppConfig()
    {
        LevelAliases = BuildLevelAliases();
        LogSwitch = new LoggingLevelSwitch(ParseLevel(Environment.GetEnvironmentVariable("LOG_LEVEL")));
        Overrides = ParseOverrides(Environment.GetEnvironmentVariable("LOG_OVERRIDES"));
    }

    private static readonly Dictionary<string, LogEventLevel> LevelAliases;

    private static LogEventLevel ParseLevel(string? raw)
    {
#pragma warning disable IDE0046
        if (raw is not null && LevelAliases.TryGetValue(raw.Trim(), out var level))
            return level;
        return LogEventLevel.Debug;
#pragma warning restore IDE0046
    }

    private static Dictionary<string, LogEventLevel> BuildLevelAliases() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["verbose"] = LogEventLevel.Verbose,
            ["trace"] = LogEventLevel.Verbose,
            ["debug"] = LogEventLevel.Debug,
            ["info"] = LogEventLevel.Information,
            ["information"] = LogEventLevel.Information,
            ["warn"] = LogEventLevel.Warning,
            ["warning"] = LogEventLevel.Warning,
            ["error"] = LogEventLevel.Error,
            ["fatal"] = LogEventLevel.Fatal,
            ["critical"] = LogEventLevel.Fatal
        };

    private static Dictionary<string, LogEventLevel> ParseOverrides(string? raw)
    {
        var result = new Dictionary<string, LogEventLevel>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0 || eq == pair.Length - 1)
                continue;
            var key = pair[..eq].Trim();
            var level = ParseLevel(pair[(eq + 1)..]);
            result[key] = level;
        }
        return result;
    }
}
