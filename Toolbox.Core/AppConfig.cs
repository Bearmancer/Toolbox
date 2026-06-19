using System.Diagnostics.CodeAnalysis;

namespace Toolbox.Core;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public static class AppConfig
{
    public static string? Endpoint { get; set; }
    public static string? Key { get; set; }
    public static string? SpeechKey { get; set; }
    public static string? SpeechRegion { get; set; }
    public static string? TranslatorRegion { get; set; }
    public static string? OpenAiDeployment { get; set; }
    public static string? OpenAiEndpoint { get; set; }
    public static string? OpenAiKey { get; set; }
    public static string? SpotifyClientId { get; set; }
    public static string? SpotifyClientSecret { get; set; }
    public static string? LastFmApiKey { get; set; }
    public static string? LastFmApiSecret { get; set; }
    public static string? DiscogsUserToken { get; set; }
    public static string? YouTubeApiKey { get; set; }
    public static string? GoogleClientId { get; set; }
    public static string? GoogleClientSecret { get; set; }
    public static string? LogLevel { get; set; }
    public static string? OutputFormat { get; set; }
}