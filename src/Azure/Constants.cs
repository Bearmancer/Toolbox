namespace Services.Azure;

public static class Constants
{
    public static string Resources => Path.Combine(Directory.GetCurrentDirectory(), "resources");
    public const long DocIntelMaxBytes = 200L * 1024 * 1024;
    public const long VisionMaxBytes = 20L * 1024 * 1024;
    public const long SpeechMaxBytes = 200L * 1024 * 1024;
    public const int TextAnalyticsMaxChars = 5_000;
    public const int TranslatorMaxChars = 50_000;
    public const int OpenAiMaxChars = 512_000;
    public const int SpeechMaxDurationSeconds = 600;
}
