# Phase 1: Credentials + Translate Service

## Tasks

### Task 3: Create AwsCredentials.cs

**What to do:**
Create `src/Services/Amazon/AwsCredentials.cs`:

```csharp
namespace Services.Amazon;

public sealed class AwsCredentials
{
    public string Region { get; init; }

    public static AwsCredentials Read()
    {
        var region = Environment.GetEnvironmentVariable("AWS_REGION");
        if (string.IsNullOrWhiteSpace(region))
            throw new InvalidOperationException("Missing: AWS_REGION");
        return new AwsCredentials { Region = region };
    }
}
```

**Must NOT:**

- Store AWS keys (only region)
- Use block-scoped namespaces
- Add comments

**References:**

- `src/Services/Azure/AzureCredentials.cs:1-43`
- `src/Services/Google/GoogleCredentials.cs:1-17`

**Acceptance criteria:**

- `dotnet build src/Services/Amazon/Amazon.csproj` succeeds
- Setting `AWS_REGION=us-east-1` and instantiating `AwsCredentials.Read()` returns valid object

**QA:**

```bash
$env:AWS_REGION="us-east-1"
dotnet build src/Services/Amazon/Amazon.csproj
```

Expected: Clean build

**Commit:** `feat(amazon): add AwsCredentials reading AWS_REGION from env`

---

### Task 5: Create AwsTranslateService.cs

**What to do:**
Create `src/Services/Amazon/AwsTranslateService.cs` with inline result record:

```csharp
using Amazon.Translate;
using Amazon.Translate.Model;
using Core;

namespace Services.Amazon;

public sealed record AwsTranslateResult(string DetectedLanguage, string TranslatedText);

public sealed class AwsTranslateService(IAmazonTranslate client)
{
    private static readonly Serilog.ILogger Log = Telemetry.ForService("AwsTranslate");

    public async Task<AwsTranslateResult> TranslateAsync(string text, string toLang, string fromLang, CancellationToken ct)
    {
        var request = new TranslateTextRequest
        {
            Text = text,
            SourceLanguageCode = string.IsNullOrWhiteSpace(fromLang) ? "auto" : fromLang,
            TargetLanguageCode = toLang
        };
        var response = await client.TranslateTextAsync(request, ct);
        return new AwsTranslateResult(response.SourceLanguageCode, response.TranslatedText);
    }

    public async Task<IReadOnlyList<AwsTranslateResult>> TranslateBatchAsync(IReadOnlyList<string> texts, string toLang, CancellationToken ct)
    {
        if (texts.Sum(t => t.Length) > 50_000)
            throw new ArgumentOutOfRangeException(nameof(texts), "Batch exceeds 50,000 chars");
        var results = new List<AwsTranslateResult>();
        foreach (var text in texts)
            results.Add(await TranslateAsync(text, toLang, "", ct));
        return results;
    }
}
```

**Must NOT:**

- Add Azure SDK types
- Use block-scoped namespaces
- Add comments

**References:**

- `src/Services/Azure/TranslateService.cs:1-60`
- `src/Core/Telemetry.cs:100-101`

**Acceptance criteria:**

- `dotnet build src/Services/Amazon/Amazon.csproj` succeeds
- `AwsTranslateResult` is `sealed record` with `DetectedLanguage` and `TranslatedText`

**QA:**

```bash
dotnet build src/Services/Amazon/Amazon.csproj
```

Expected: Clean build

**Commit:** `feat(amazon): add AwsTranslateService with inline AwsTranslateResult`

---

## Verify Phase 1

```bash
dotnet build src/Services/Amazon/Amazon.csproj
```

Clean build. Credentials and service in place.

**Dependencies:** Phase 0
**Blocks:** Phase 2
