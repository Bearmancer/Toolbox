# Azure Services

Thin SDK wrappers — one client + service per Azure AI capability. Cross-service consumer: `Services.Google` → `TranslateService`.

## STRUCTURE

```
Azure/
├── AzureSetup.cs               # extension AddAzureServices() — 6 SDK clients + SpeechService
├── AzureCredentials.cs         # 15 env vars: Read() + Env() — endpoints/keys/region/deployment
├── VisionService.cs            # ImageAnalysisClient → AnalyzeAsync()
├── TranslateService.cs         # TextTranslationClient → TranslateBatchAsync() / TransliterateBatchAsync()
├── SpeechService.cs            # SpeechConfig+ffmpeg → TranscribeAsync() / SynthesizeAsync() (chunked)
├── DocIntelService.cs          # DocumentIntelligenceClient → AnalyzeAsync()
├── OpenAiService.cs            # AzureOpenAIClient → ChatAsync()
├── TextAnalyticsService.cs     # TextAnalyticsClient → Sentiment/Entities/KeyPhrases/DetectLanguage/Pii
├── AzureSdkEventListener.cs    # AzureEventSourceListener → Serilog (Azure-Core/Identity)
├── ClientModelEventListener.cs # ClientModel EventSource → Serilog
├── SpeechSdkEventListener.cs   # Speech SDK EventSource → Serilog
└── EventLevelMapper.cs         # EventLevel → LogEventLevel
```

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Add Azure service | `XxxService.cs` + `AzureCredentials.cs` + `AzureSetup.cs` | Add props + `Env()`, register `new XxxClient()` + `AddSingleton<XxxService>()` |
| Add env var | `AzureCredentials.cs` | Add `required string` prop + `Env("KEY_NAME")` in `Read()` |
| Change DI | `AzureSetup.cs` | `extension(IServiceCollection services)` block |

## CONVENTIONS

- **One SDK client per service** (except `SpeechService` — builds `SpeechConfig` from `AzureCredentials`).
- **Credentials:** `.env` only via `AzureCredentials.Read()` — 15 vars, `Env()` throws on missing.
- **DI:** `extension(IServiceCollection)` → `AddSingleton(new XxxClient(...))` + `AddSingleton<XxxService>()`.
- **Errors:** `ErrorOr<T>` for fallible ops, `Errors.*.ApiError` on catch, throw only for missing config.

## ANTI-PATTERNS

- **NEVER** hardcode keys/endpoints — always `AzureCredentials.Read()`.
- **NEVER** add CLI logic here — thin service layer, CLI lives in `src/CLI/Azure/`.
