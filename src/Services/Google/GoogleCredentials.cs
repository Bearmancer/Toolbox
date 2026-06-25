namespace Services.Google;

public sealed class GoogleCredentials
{
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }

    public static GoogleCredentials Read() =>
        new() { ClientId = Env("GOOGLE_CLIENT_ID"), ClientSecret = Env("GOOGLE_CLIENT_SECRET") };

    private static string Env(string key) =>
        Environment.GetEnvironmentVariable(key)
        ?? throw new InvalidOperationException($"Missing: {key}");
}
