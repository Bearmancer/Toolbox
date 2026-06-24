# Learnings — YouTube Fetch

## YoutubeService Extensions (T3)
- `BatchRequest` takes `IClientService` (the `YouTubeService` itself), NOT `HttpClient`
- Use `global::` prefix to disambiguate `Google.Apis` from `Services.Google` in file-scoped namespaces
- `[GeneratedRegex]` requires the containing class to be `partial`
- `QuotaUsed` is a simple counter incremented after each `ExecuteAsync` call
- Default parameters preserve backward compatibility for existing callers
- `videoIds.Chunk(50)` works for batching Videos.list calls (max 50 IDs per request)
- `Iso8601Regex().ValueSpan` returns `ReadOnlySpan<char>` — use `int.Parse()` directly
