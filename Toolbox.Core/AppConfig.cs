using System.Diagnostics.CodeAnalysis;

namespace Toolbox.Core;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public static class AppConfig
{
    public static string? Endpoint { get; set; }
    public static string? SpeechRegion { get; set; }
    public static string? TranslatorRegion { get; set; }
    public static string? OpenAiDeployment { get; set; }
    public static string? OpenAiEndpoint { get; set; }
    public static string? LogLevel { get; set; }
    public static string? OutputFormat { get; set; }
}