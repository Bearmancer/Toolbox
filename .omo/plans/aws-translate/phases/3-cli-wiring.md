# Phase 3: CLI + Wiring + Telemetry

## Tasks

### Task 6: Create CLI TranslateCommand.cs

**What to do:**
Create `src/CLI/AWS/TranslateCommand.cs`:

```csharp
using System.ComponentModel;
using Services.Amazon;
using Spectre.Console.Cli;

namespace CLI.AWS;

public sealed class TranslateCommand(AwsTranslateService service) : AsyncCommand<TranslateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<text>")]
        [Description("Text to translate")]
        public string Text { get; init; } = "";

        [CommandOption("--to <LANG>")]
        [Description("Target language code (default: ja)")]
        public string ToLang { get; init; } = "ja";

        [CommandOption("--from <LANG>")]
        [Description("Source language code (optional, auto-detect if omitted)")]
        public string? FromLang { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var results = await service.TranslateBatchAsync([settings.Text], settings.ToLang, CancellationToken.None);
        foreach (var result in results)
            Console.WriteLine(result.TranslatedText);
        return 0;
    }
}
```

**Must NOT:**

- Add Azure types or references
- Use block-scoped namespaces
- Add comments

**References:**

- `src/CLI/Azure/TranslateCommand.cs:1-40`

**Acceptance criteria:**

- `dotnet build src/CLI/CLI.csproj` succeeds (after project reference added)

**QA:**

```bash
dotnet build src/CLI/CLI.csproj
```

Expected: Clean build

**Commit:** `feat(amazon): add CLI aws translate command`

---

### Task 7: Create CLI AwsCommandModule.cs

**What to do:**
Create `src/CLI/AWS/AwsCommandModule.cs`:

```csharp
namespace CLI.AWS;

public static class AwsCommandModule
{
    public static void ConfigureCommands(IConfigurator cfg)
    {
        cfg.AddBranch("aws", aws =>
        {
            aws.SetDescription("AWS services — translation");
            aws.AddCommand<TranslateCommand>("translate");
        });
    }
}
```

**Must NOT:**

- Add other commands yet
- Use block-scoped namespaces
- Add comments

**References:**

- `src/CLI/Azure/AzureCommandModule.cs:1-18`

**Acceptance criteria:**

- `dotnet build src/CLI/CLI.csproj` succeeds

**QA:**

```bash
dotnet build src/CLI/CLI.csproj
```

Expected: Clean build

**Commit:** `feat(amazon): add AwsCommandModule for CLI aws branch`

---

### Task 8: Wire DI and CLI in Program.cs

**What to do:**
In `src/App/Program.cs`:

1. Add `using CLI.AWS;` and `using Services.Amazon;`
2. Call `services.AddAmazonServices()` after `services.AddGoogleServices()`
3. Call `AwsCommandModule.ConfigureCommands(cfg)` in the `app.Configure` block

**Must NOT:**

- Move or reorder existing Azure/Google registrations
- Change try/catch error handling
- Add comments

**References:**

- `src/App/Program.cs:36-58`
- `src/App/App.csproj:1-15`

**Acceptance criteria:**

- `dotnet build src/App/App.csproj` succeeds
- `dotnet run --project src/App -- aws translate --help` shows command description

**QA:**

```bash
dotnet build src/App/App.csproj
dotnet run --project src/App -- aws translate --help
```

Expected: Build succeeds, help output shows "AWS services — translation"

**Commit:** `feat(amazon): wire Amazon services and CLI into App`

---

### Task 9: Add project reference to CLI.csproj

**What to do:**
Add `<ProjectReference Include="..\Services\Amazon\Amazon.csproj" />` to `src/CLI/CLI.csproj`.

**Must NOT:**

- Add to App.csproj (transitive through CLI.csproj)
- Remove existing references
- Change order of existing references

**References:**

- `src/CLI/CLI.csproj:1-13`

**Acceptance criteria:**

- `dotnet build src/CLI/CLI.csproj` succeeds

**QA:**

```bash
dotnet build src/CLI/CLI.csproj
```

Expected: Clean build

**Commit:** `chore(amazon): add project reference for Services.Amazon`

---

### Task 10: Add AWS log sink to Telemetry.cs

**What to do:**
In `src/Core/Telemetry.cs`, add after existing Whisper sink (line 90):

```csharp
.WriteTo.Logger(lc => lc
    .Filter.ByIncludingOnly(e => e.Properties.ContainsKey("Service") && e.Properties["Service"].ToString().Contains("AwsTranslate"))
    .WriteTo.File(new CompactJsonFormatter(), "logs/azureai-aws-.jsonl", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7))
```

**Must NOT:**

- Modify existing sinks

**References:**

- `src/Core/Telemetry.cs:27-92`

**Acceptance criteria:**

- `dotnet build src/Core/Core.csproj` succeeds

**QA:**

```bash
dotnet build src/Core/Core.csproj
```

Expected: Clean build

**Commit:** `feat(telemetry): add per-service JSONL sink for AWS`

---

## Verify Phase 3

```bash
dotnet build
dotnet run --project src/App -- aws translate --help
```

Full solution builds. CLI help shows aws translate command.

**Dependencies:** Phase 2
**Blocks:** Phase 4
