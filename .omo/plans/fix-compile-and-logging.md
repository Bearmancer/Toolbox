# Fix Compile Errors, Logging, TypeRegistrar, Access Modifiers

## Pre-Conditions
- Build must pass before starting
- Run `dotnet build AzureAI.slnx` after EVERY step

## Step 1: Create `Core/Limits.cs` (Fix Compile Error)

**Create:** `src/Core/Limits.cs`
**Delete:** None (no separate Limits files exist currently)
**Modify:** 8 files that reference `Constants.*`

### File: `src/Core/Limits.cs`
```csharp
namespace Core;

internal static class Limits
{
    public static class OpenAi { public const int MaxChars = 512_000; }
    public static class TextAnalytics { public const int MaxChars = 5_120; }
    public static class Translator { public const int MaxChars = 50_000; }
    public static class Vision { public const int MaxBytes = 20_000_000; }
    public static class DocIntel { public const int MaxBytes = 500_000_000; }
    public static class Speech { public const int MaxBytes = 100_000_000; public const int MaxDurationSeconds = 120; }
    public static class Resources { public const string Directory = "resources"; }
}
```

### Call site updates:
| File | Old | New |
|------|-----|-----|
| `src/Services/Azure/OpenAiService.cs:19` | `Constants.OpenAiMaxChars` | `Limits.OpenAi.MaxChars` |
| `src/Services/Azure/SpeechTtsService.cs:19` | `Constants.OpenAiMaxChars` | `Limits.OpenAi.MaxChars` |
| `src/Services/Azure/TranslateService.cs:17` | `Constants.TranslatorMaxChars` | `Limits.Translator.MaxChars` |
| `src/Services/Azure/TextAnalyticsService.cs:17,45,77,105,133` | `Constants.TextAnalyticsMaxChars` | `Limits.TextAnalytics.MaxChars` |
| `src/Services/Azure/VisionService.cs:19` | `Constants.VisionMaxBytes` | `Limits.Vision.MaxBytes` |
| `src/Services/Azure/DocIntelService.cs:30` | `Constants.DocIntelMaxBytes` | `Limits.DocIntel.MaxBytes` |
| `src/Services/Azure/SpeechSttService.cs:21` | `Constants.SpeechMaxBytes` | `Limits.Speech.MaxBytes` |
| `src/Services/Azure/SpeechSttService.cs:50` | `Constants.SpeechMaxDurationSeconds` | `Limits.Speech.MaxDurationSeconds` |
| `src/Services/Azure/FileHelpers.cs:9` | `Constants.Resources` | `Limits.Resources.Directory` |

### Verify: `dotnet build AzureAI.slnx`

## Step 2: Fix TypeRegistrar

**Modify:** `src/CLI/TypeRegistrar.cs`

### Change:
Add `private ServiceProvider? _provider;` field. Change `Build()` to `_provider ??= services.BuildServiceProvider();`

### Verify: `dotnet build AzureAI.slnx`

## Step 3: Refactor Logging (Per-Service JSONL)

**Modify:** `src/Core/Telemetry.cs`

### Changes:
1. Add `using Serilog.Context;`
2. Add `public static IDisposable ForService(string service) => LogContext.PushProperty("Service", service);`
3. Add sub-logger sinks for azure.jsonl and google.jsonl with filters
4. Simplify timestamp to `yyyy-MM-dd HH:mm:ss`

### Verify: `dotnet build AzureAI.slnx`

## Step 4: Access Modifier Cleanup

**Modify:**
- `src/Services/Azure/FileHelpers.cs` → `internal static class FileHelpers`

### Verify: `dotnet build AzureAI.slnx`

## Post-Conditions
- `dotnet build AzureAI.slnx` passes with zero errors
- No new files except `src/Core/Limits.cs`
