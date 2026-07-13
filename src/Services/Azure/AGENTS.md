# Azure Services

Azure AI SDK wrappers: Vision, Translate, Speech, DocIntel, OpenAI, TextAnalytics.

## STRUCTURE

```
Azure/
├── AzureSetup.cs            # DI: extension AddAzureServices() registers all clients + services
├── AzureCredentials.cs      # Reads 15 env vars (endpoints + keys for each service)
├── VisionService.cs         # ImageAnalysisClient → AnalyzeAsync()
├── TranslateService.cs      # TextTranslationClient → TranslateBatchAsync()
├── SpeechService.cs         # SpeechRecognizer → TranscribeAsync(), SpeechSynthesizer → SynthesizeAsync()
├── DocIntelService.cs       # DocumentIntelligenceClient → AnalyzeAsync()
├── OpenAiService.cs         # AzureOpenAIClient → ChatCompletionAsync()
└── TextAnalyticsService.cs  # TextAnalyticsClient → EntitiesAsync(), KeyPhrasesAsync()
```

## WHERE TO LOOK

| Task                   | File                                                                                         | Notes                                          |
| ---------------------- | -------------------------------------------------------------------------------------------- | ---------------------------------------------- |
| Add Azure service      | Create `XxxService.cs`, add credential to `AzureCredentials.cs`, register in `AzureSetup.cs` |
| Add env var            | `AzureCredentials.cs`                                                                        | Add property + `Env("KEY_NAME")` call          |
| Change DI registration | `AzureSetup.cs`                                                                              | `extension(IServiceCollection services)` block |

## CONVENTIONS

- **One SDK client per service class.** Constructor receives the SDK client from DI.
- **Credentials:** All from `.env` via `AzureCredentials.Read()`. No hardcoded values.
- **DI pattern:** `services.AddSingleton(new XxxClient(...))` then `services.AddSingleton<XxxService>()`.
- **Return types:** `ErrorOr<T>` for fallible operations. Throw only for configuration errors.

## ANTI-PATTERNS

- **NEVER** hardcode API keys or endpoints. Always read from env.
- **NEVER** add CLI logic here. Service classes are pure business logic.
