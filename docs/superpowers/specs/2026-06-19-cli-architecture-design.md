# CLI Architecture Design — Toolbox (Azure Phase)

**Date**: 2026-06-19  
**Scope**: Azure-only CLI. Music, Sync, Reader deferred. Extensibility preserved.  
**Target**: `.NET 10`, `Spectre.Console.Cli`, `Serilog → Seq`, `DefaultAzureCredential`

---

## 1. Decision Record

### 1.1 Authentication: DefaultAzureCredential, No API Keys, No Fallbacks

`DefaultAzureCredential` is used **exclusively** across all Azure SDK clients. There are no API
key fields, no fallback paths, and no environment variables holding secrets.

**Why this works for a solo dev:**

| Environment | How DAC resolves |
|---|---|
| Local dev (Windows) | `AzureCliCredential` → picks up active `az login` session |
| Azure VM / App Service (future) | `ManagedIdentityCredential` → Managed Identity toggle on resource |
| GitHub Actions (future) | `EnvironmentCredential` → `AZURE_CLIENT_ID` etc. via OIDC |

Zero code changes between environments. The waterfall handles it.

**Verified RBAC (as of 2026-06-19):**
- `Cognitive Services User` → rg-lance → covers DocIntel, Vision, Speech, TextAnalytics, Translate
- `Cognitive Services OpenAI User` → rg-lance → covers Azure OpenAI
- `Key Vault Secrets User` → kv-lance-2 → covers third-party secrets (Discogs, LastFm, YouTube)

**kilo-agent App Registration**: deleted. No Service Principals exist.

### 1.2 Third-Party Secrets: Key Vault via DAC

Secrets that cannot use DAC directly (Discogs token, LastFm key, YouTube key) are stored in
`kv-lance-2` and fetched at startup via `SecretClient` using `DefaultAzureCredential`. They are
injected into the DI container as named string options — never into environment variables.

Key Vault secrets present:
- `DiscogsUserToken`
- `LastFmApiKey`
- `YouTubeApiKey`
- `TextAnalyticsKey` ← stale, to be deleted (service uses DAC now)

### 1.3 Speech SDK Exception

The Azure Speech SDK (v1.x) does not support `DefaultAzureCredential` natively. Resolution:

```csharp
// In SpeechClientFactory or SpeechSttService constructor:
var tokenCredential = new DefaultAzureCredential();
var tokenContext = new TokenRequestContext(
    ["https://cognitiveservices.azure.com/.default"]);
var token = await tokenCredential.GetTokenAsync(tokenContext, ct);

var config = SpeechConfig.FromAuthorizationToken(token.Token, "centralindia");
```

Token TTL is ~10 minutes. CLI invocations are single-shot — no refresh needed.

### 1.4 Dependency Injection: Why for a Solo Dev

DI is not enterprise ceremony here. It solves three concrete solo-dev problems:

**Problem 1: Hidden dependency graphs.**  
Without DI, adding a service requires touching `AzureClients.cs`, `AppConfig.cs`, `Program.cs`,
and the command. With DI, you add one constructor parameter and one `services.Add*()` line in the
module. `Program.cs` never changes when adding a new service.

**Problem 2: `DefaultAzureCredential` must be a singleton.**  
It probes 7+ credential sources on construction. Creating it per-call is wasteful and slow.
`services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential())` guarantees one
instance shared by all clients automatically — you cannot express this guarantee with statics.

**Problem 3: Extensibility without modification.**  
The `ICommandModule` interface means adding a new domain (e.g., a Search module) means creating
one new file that registers its own services and commands. Nothing else changes. This is the
"add one at a time" guarantee.

---

## 2. Project Structure

```
New/
├── App.sln                        ← CLI + Core + Azure only (Music/Sync/Reader deferred)
├── .env                           ← endpoints + SEQ config, no secrets
├── Directory.Build.props          ← TreatWarningsAsErrors, nullable, analysis
│
├── Core/                          ← Core.csproj, no Azure deps
│   ├── CommandBase.cs             ← abstract AsyncCommand<T> with error handling
│   ├── Host.cs                    ← Ctrl+C handler + LogPipeline.CloseAndFlush()
│   ├── ServiceContext.cs          ← AsyncLocal session tracking
│   ├── Infrastructure/
│   │   ├── TypeRegistrar.cs       ← ITypeRegistrar wrapping IServiceCollection
│   │   └── TypeResolver.cs        ← ITypeResolver wrapping IServiceProvider
│   ├── Modules/
│   │   └── ICommandModule.cs      ← ConfigureServices() + ConfigureCommands()
│   └── Logging/
│       ├── ILogEvent.cs           ← Severity enum + interface
│       ├── CoreEvents.cs          ← structured event records
│       ├── Log.cs                 ← single Emit() entrypoint
│       ├── LogPipeline.cs         ← Serilog → console + file + Seq (opt-in)
│       └── OperationScope.cs      ← timed operation lifecycle (IDisposable)
│
├── Azure/                         ← Azure.csproj → depends on Core
│   ├── AzureCommandModule.cs      ← ICommandModule: registers services + commands
│   ├── Options/
│   │   └── AzureOptions.cs        ← typed config bound from IConfiguration
│   ├── Constants.cs               ← relative paths, no hardcoded absolutes
│   ├── DocIntelService.cs         ← ctor(DocumentIntelligenceClient, ILogger)
│   ├── OpenAiService.cs           ← ctor(AzureOpenAIClient, AzureOptions, ILogger)
│   ├── SpeechSttService.cs        ← ctor(TokenCredential, AzureOptions, ILogger)
│   ├── SpeechTtsService.cs        ← ctor(TokenCredential, AzureOptions, ILogger)
│   ├── TextAnalyticsService.cs    ← ctor(TextAnalyticsClient, ILogger)
│   ├── TranslateService.cs        ← ctor(TextTranslationClient, AzureOptions, ILogger)
│   └── VisionService.cs           ← ctor(ImageAnalysisClient, ILogger)
│
└── CLI/                           ← CLI.csproj → depends on Core + Azure
    ├── Program.cs                 ← composition root (see boot sequence)
    ├── ConfigCommand.cs
    └── Azure/
        ├── ChatCommand.cs         ← ctor(OpenAiService)
        ├── DocIntelCommand.cs     ← ctor(DocIntelService)
        ├── LanguageCommand.cs     ← ctor(TextAnalyticsService)
        ├── NerCommand.cs          ← ctor(TextAnalyticsService)
        ├── PhrasesCommand.cs      ← ctor(TextAnalyticsService)
        ├── PiiCommand.cs          ← ctor(TextAnalyticsService)
        ├── SentimentCommand.cs    ← ctor(TextAnalyticsService)
        ├── SpeechSttCommand.cs    ← ctor(SpeechSttService)
        ├── SpeechTtsCommand.cs    ← ctor(SpeechTtsService)
        ├── TranslateCommand.cs    ← ctor(TranslateService)
        └── VisionCommand.cs       ← ctor(VisionService)
```

---

## 3. Boot Sequence

```csharp
// Program.cs — ordered deliberately

// 1. Load .env FIRST — SEQ_URL, endpoints must be available before anything else
DotNetEnv.Env.Load();

// 2. Configure Serilog — now SEQ_URL is present, Seq sink registers correctly
LogPipeline.Configure("app");

// 3. Register Ctrl+C / ProcessExit — flushes Serilog on shutdown
Host.Initialize();

// 4. Build configuration from env vars (already loaded by DotNetEnv)
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

// 5. Build DI container — modules register their own services
var services = new ServiceCollection();
ICommandModule[] modules = [new AzureCommandModule()];
foreach (var m in modules)
    m.ConfigureServices(services, configuration);

// 6. Build Spectre app wired to DI
var registrar = new TypeRegistrar(services);
var app = new CommandApp(registrar);
app.Configure(config =>
{
    config.SetApplicationName("app");
    config.SetApplicationVersion("1.0.0");
    foreach (var m in modules)
        m.ConfigureCommands(config);
});

// 7. Run — Spectre resolves commands via TypeResolver → IServiceProvider
return await app.RunAsync(args);
```

---

## 4. Logging Pipeline

### Sinks
| Sink | When active | Format |
|---|---|---|
| Console | Always | `AnsiConsoleTheme.Code` (dev-friendly) |
| File | Always | CLEF compact JSON, rolling daily, 30-day retention |
| Seq | If `SEQ_URL` set in `.env` | Structured, level-switch controlled |

### Configuration
```
.env:
  SEQ_URL=http://localhost:5341      ← local Seq instance
  SEQ_INSTANCE=lance-laptop          ← enrichment property
  LOG_LEVEL=info                     ← minimum level
```

Seq is **opt-in** — if `SEQ_URL` is absent, only console and file sinks activate. Startup logs a
warning if `SEQ_URL` is set but the server is unreachable (non-fatal).

### Structured Events (already implemented in Core/Logging/)
- `SessionStarted` / `SessionEnded` — command lifecycle
- `OperationStarted` / `OperationCompleted` / `OperationFailed` — timed operations
- `ApiRequested` / `ApiResponded` — HTTP calls with status + elapsed
- `ErrorOccurred` / `FatalOccurred` — exceptions with type + context

---

## 5. Module Registration Contract

```csharp
// Core/Modules/ICommandModule.cs
public interface ICommandModule
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    void ConfigureCommands(IConfigurator config);
}
```

```csharp
// Azure/AzureCommandModule.cs
public class AzureCommandModule : ICommandModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Typed options
        services.Configure<AzureOptions>(configuration);

        // Credential — singleton, constructed once
        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

        // Azure SDK clients — singletons, share the credential
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequired<IOptions<AzureOptions>>().Value;
            return new DocumentIntelligenceClient(
                new Uri(opts.Endpoint), sp.GetRequired<TokenCredential>());
        });
        // ... TextAnalyticsClient, ImageAnalysisClient, AzureOpenAIClient,
        //     TextTranslationClient registered similarly

        // Domain services
        services.AddSingleton<DocIntelService>();
        services.AddSingleton<OpenAiService>();
        services.AddSingleton<TextAnalyticsService>();
        services.AddSingleton<VisionService>();
        services.AddSingleton<TranslateService>();
        services.AddSingleton<SpeechSttService>();
        services.AddSingleton<SpeechTtsService>();

        // Commands — transient (Spectre creates one per invocation)
        services.AddTransient<ChatCommand>();
        services.AddTransient<DocIntelCommand>();
        // ... all 11 commands
    }

    public void ConfigureCommands(IConfigurator config)
    {
        config.AddBranch("azure", azure =>
        {
            azure.AddCommand<ChatCommand>("chat")
                 .WithDescription("Chat with Azure OpenAI");
            azure.AddCommand<VisionCommand>("vision")
                 .WithDescription("Analyse an image");
            // ... etc.
        });
    }
}
```

**Adding a new service in future (e.g., Azure AI Search):**
1. Create `SearchService.cs` with ctor `(SearchClient, ILogger)`
2. Add `services.AddSingleton<SearchClient>(...)` and `services.AddSingleton<SearchService>()` in `AzureCommandModule.ConfigureServices()`
3. Add `azure.AddCommand<SearchCommand>("search")` in `ConfigureCommands()`
4. `Program.cs` does not change.

---

## 6. AzureOptions (typed configuration)

```csharp
// Azure/Options/AzureOptions.cs
public class AzureOptions
{
    public string Endpoint { get; init; } = "";         // AI Services multi-service
    public string OpenAiEndpoint { get; init; } = "";
    public string OpenAiDeployment { get; init; } = "gpt-4o-mini";
    public string SpeechRegion { get; init; } = "";
    public string TranslatorRegion { get; init; } = "";
}
```

Bound from environment variables (already present in `.env`):
```
ENDPOINT=https://ai-lance-services.cognitiveservices.azure.com/
OPENAI_ENDPOINT=https://ai-lance-oai.openai.azure.com/
OPENAI_DEPLOYMENT=gpt-4o-mini
SPEECH_REGION=centralindia
TRANSLATOR_REGION=centralindia
```

---

## 7. What Is Explicitly Out of Scope (This Phase)

- Music, Sync, Reader modules — not in `.sln`, not wired
- Polly v8 resilience pipelines — deferred to next phase
- Key Vault third-party secrets loading for Music/Sync — deferred with those modules
- `TextAnalyticsKey` in Key Vault — to be deleted (dead weight, service uses DAC)
- Unit tests — deferred; DI makes them trivially addable when wanted

---

## 8. Open Items Before Implementation

- [ ] Delete stale `TextAnalyticsKey` secret from `kv-lance-2`
- [ ] Verify `SpeechConfig.FromAuthorizationToken` works with `centralindia` region via DAC token
- [ ] Confirm `CLI.csproj` references `Core` and `Azure` (not yet in `.sln`)
