# Phase 2: DI Setup

## Tasks

### Task 4: Create AwsSetup.cs

**What to do:**
Create `src/Services/Amazon/AwsSetup.cs`:

```csharp
using Amazon;
using Amazon.Translate;
using Amazon.Translate.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Amazon;

public static class AwsSetup
{
    public static void AddAmazonServices(this IServiceCollection services)
    {
        var credentials = AwsCredentials.Read();
        services.AddSingleton(credentials);

        var config = new AmazonTranslateConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(credentials.Region)
        };
        var client = new AmazonTranslateClient(config);
        services.AddSingleton<IAmazonTranslate>(client);
        services.AddSingleton<AwsTranslateService>();
    }
}
```

**Must NOT:**

- Create client with explicit credentials (use SDK default chain)
- Use block-scoped namespaces
- Add comments

**References:**

- `src/Services/Azure/AzureSetup.cs:1-59`
- `src/Services/Google/GoogleSetup.cs:1-43`

**Acceptance criteria:**

- `dotnet build src/Services/Amazon/Amazon.csproj` succeeds
- Calling `AddAmazonServices()` on a `ServiceCollection` does not throw

**QA:**

```bash
dotnet build src/Services/Amazon/Amazon.csproj
```

Expected: Clean build

**Commit:** `feat(amazon): add AwsSetup with DI registration`

---

## Verify Phase 2

```bash
dotnet build src/Services/Amazon/Amazon.csproj
```

Clean build. DI registration in place.

**Dependencies:** Phase 1 (Tasks 3, 5)
**Blocks:** Phase 3
