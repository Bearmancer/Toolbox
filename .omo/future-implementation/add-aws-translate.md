# add-aws-translate - Work Plan

## TL;DR (For humans)

**What you'll get:** A new `src/Services/Amazon` project that mirrors the Azure service layout, a `aws translate` CLI command, AWS MCP servers configured in OpenCode, and a real Hindi→German translation test.

**Why this approach:** Add AWS as a third service provider following the exact same credentials→setup→service→CLI pattern as Azure and Google. Configure native AWS MCP servers so OpenCode can call AWS services directly. Use AWS SDK V4 (V3 is EOL).

**What it will NOT do:** No changes to existing Azure/Google behavior, no interactive AWS login, no credential storage in the app, no additional AWS services beyond Translate in the codebase.

**Effort:** Short
**Risk:** Low — additive only, no existing code touched
**Decisions to sanity-check:** None — all defaults are reversible

Your next move: approve, or run a high-accuracy review first.

---

> TL;DR (machine): Short effort, low risk. Add src/Services/Amazon (credentials/setup/translate), CLI aws translate branch, AWS MCP servers in opencode.jsonc, real translation test.

## Scope
### Must have
- New `src/Services/Amazon` project with `AwsCredentials.cs`, `AwsSetup.cs`, `AwsTranslateService.cs` (with inline `AwsTranslateResult` record)
- Package entry for `AWSSDK.Translate` in `Directory.Packages.props`
- Project references from `App` and `CLI` to `Services.Amazon`
- New `CLI.AWS.AwsCommandModule` and `CLI.AWS.TranslateCommand`
- Register `AddAmazonServices()` and `AwsCommandModule.ConfigureCommands(cfg)` in `App/Program.cs`
- Add AWS log sink in `Core/Telemetry.cs`
- Add five AWS MCP server entries to `~/.config/opencode/opencode.jsonc`
- Real execution of translate with source auto-detect and target German

### Must NOT have (guardrails, anti-slop, scope boundaries)
- No changes to existing Azure or Google service behavior
- No interactive AWS login flow, credential caching, or key storage
- No additional AWS services beyond Translate in the codebase (MCP servers may expose more, but only Translate is wrapped in C#)
- No shared Core DTO changes (AWS keeps its own result record, inline in service file)
- No root-account usage guidance
- No Docker-based MCP proxy unless explicitly asked
- No inline or explanatory comments
- Use file-scoped namespaces (`namespace Foo;` not `namespace Foo { ... }`)

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: tests-after + `dotnet build` + real MCP tool call
- Evidence: .omo/evidence/task-<N>-add-aws-translate.<ext>

## Execution strategy
### Parallel execution waves

**Wave 1** — Foundation (parallel, no deps):
- Tasks 1 + 2 (package + csproj)

**Wave 2a** — Credentials + TranslateService (parallel, both depend on Wave 1):
- Task 3 (AwsCredentials)
- Task 5 (AwsTranslateService + AwsTranslateResult)

**Wave 2b** — Setup (depends on Wave 2a completing):
- Task 4 (AwsSetup) — needs both Credentials and TranslateService types to register in DI

**Wave 3** — CLI + wiring + telemetry (sequential within, parallel where possible):
- Task 9 (project references) — can start immediately after Wave 2b
- Task 6 (CLI TranslateCommand) — depends on Task 5
- Task 7 (CLI AwsCommandModule) — depends on Task 6
- Task 8 (Program.cs wiring) — depends on Tasks 4, 7, 9
- Task 10 (Telemetry sink) — independent, parallel with 6-9

**Wave 4** — MCP config (independent, can run with any wave):
- Task 11 (opencode.jsonc)

**Wave 5** — Test + final verification:
- Task 12 + Final verification wave

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 (Package) | — | 3, 5 | 2 |
| 2 (Csproj) | — | 3, 5 | 1 |
| 3 (Credentials) | 1, 2 | 4 | 5 |
| 4 (Setup) | 3, 5 | 7, 8, 9 | — |
| 5 (TranslateService) | 1, 2 | 4, 6 | 3 |
| 6 (CLI Command) | 5 | 7 | 10 |
| 7 (CommandModule) | 4, 6 | 8 | — |
| 8 (Program.cs) | 4, 7 | 12 | 10 |
| 9 (Project refs) | 2 | 8 | 6, 10 |
| 10 (Telemetry) | — | 12 | 6, 8, 9 |
| 11 (MCP config) | — | 12 | 1-10 |
| 12 (Test) | 8, 10, 11 | — | — |

## Todos
> Implementation + Test = ONE todo. Never separate.

- [ ] 1. Add AWSSDK.Translate package to Directory.Packages.props
  What to do / Must NOT do: Add `<PackageVersion Include="AWSSDK.Translate" Version="4.0.2.11" />` to the `<ItemGroup>` in `Directory.Packages.props`. Do not add any other packages.
  Parallelization: Wave 1 | Blocked by: — | Blocks: 3, 5
  References: `Directory.Packages.props:6-38`
  Acceptance criteria (agent-executed): `dotnet restore` succeeds
  QA scenarios: happy — run `dotnet restore` and verify no errors; failure — if version conflicts, check AWSSDK.Core dependency range. Evidence .omo/evidence/task-1-add-aws-translate.txt
  Commit: Y | chore(packages): add AWSSDK.Translate V4

- [ ] 2. Create Amazon.csproj
  What to do / Must NOT do: Create `src/Services/Amazon/Amazon.csproj` mirroring `src/Services/Azure/Azure.csproj`. Set `<RootNamespace>Services.Amazon</RootNamespace>`. Reference `AWSSDK.Translate`, `Microsoft.Extensions.DependencyInjection`, and `ProjectReference` to `../../Core/Core.csproj`. Do NOT reference Azure.csproj. Use file-scoped namespace in any .cs files.
  Parallelization: Wave 1 | Blocked by: — | Blocks: 3, 5
  References: `src/Services/Azure/Azure.csproj:1-18`, `src/Services/Google/Google.csproj:1-13`
  Acceptance criteria (agent-executed): `dotnet build src/Services/Amazon/Amazon.csproj` succeeds (even with empty .cs files)
  QA scenarios: happy — `dotnet build` clean; failure — check package versions match Directory.Packages.props. Evidence .omo/evidence/task-2-add-aws-translate.txt
  Commit: Y | feat(amazon): add Services.Amazon project skeleton

- [ ] 3. Create AwsCredentials.cs
  What to do / Must NOT do: Create `src/Services/Amazon/AwsCredentials.cs` with `public sealed class AwsCredentials` reading `AWS_REGION` from environment (required, throws `InvalidOperationException` if missing). Use `init` properties. Read-only, no mutation. Mirror AzureCredentials pattern exactly. Property name: `Region` (not `AwsRegion` — class already says `Aws`). Do NOT store AWS keys — only region. The user is logged in via AWS CLI; the SDK default chain handles auth. Use file-scoped namespace. No comments.
  Parallelization: Wave 2a | Blocked by: 1, 2 | Blocks: 4
  References: `src/Services/Azure/AzureCredentials.cs:1-43`, `src/Services/Google/GoogleCredentials.cs:1-17`
  Acceptance criteria (agent-executed): `dotnet build src/Services/Amazon/Amazon.csproj` succeeds; setting `AWS_REGION=us-east-1` and instantiating `AwsCredentials.Read()` returns a valid object
  QA scenarios: happy — set env var, call Read(), verify region is populated; failure — unset env var, call Read(), verify `InvalidOperationException` with message "Missing: AWS_REGION". Evidence .omo/evidence/task-3-add-aws-translate.txt
  Commit: Y | feat(amazon): add AwsCredentials reading AWS_REGION from env

- [ ] 4. Create AwsSetup.cs
  What to do / Must NOT do: Create `src/Services/Amazon/AwsSetup.cs` with `public static class AwsSetup` and `AddAmazonServices(this IServiceCollection services)` extension method. Read credentials, register as singleton. Create `AmazonTranslateClient` with region config from `AmazonTranslateConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(credentials.Region) }`. Register `AwsTranslateService` as singleton. Do NOT create the client with explicit credentials — let the SDK default chain handle auth. Use file-scoped namespace. No comments. Use collection expressions where applicable.
  Parallelization: Wave 2b | Blocked by: 3, 5 | Blocks: 7, 8, 9
  References: `src/Services/Azure/AzureSetup.cs:1-59`, `src/Services/Google/GoogleSetup.cs:1-43`
  Acceptance criteria (agent-executed): `dotnet build src/Services/Amazon/Amazon.csproj` succeeds; calling `AddAmazonServices()` on a `ServiceCollection` does not throw
  QA scenarios: happy — build succeeds, DI registration compiles; failure — if `RegionEndpoint` not found, check AWS_REGION value is a valid system name (e.g., "us-east-1" not "US East 1"). Evidence .omo/evidence/task-4-add-aws-translate.txt
  Commit: Y | feat(amazon): add AwsSetup with DI registration

- [ ] 5. Create AwsTranslateService.cs
  What to do / Must NOT do: Create `src/Services/Amazon/AwsTranslateService.cs` with inline `AwsTranslateResult` record at the top (same file as service, mirroring Azure's pattern). Service class uses primary constructor `AwsTranslateService(IAmazonTranslate client)`. Implement `TranslateAsync(string text, string toLang, string fromLang, CancellationToken ct)` returning `AwsTranslateResult` and `TranslateBatchAsync(IReadOnlyList<string> texts, string toLang, CancellationToken ct)` returning `IReadOnlyList<AwsTranslateResult>`. Use `SourceLanguageCode = "auto"` when fromLang is empty/null. Use `Telemetry.ForService("AwsTranslate")` for logging. Throw `ArgumentOutOfRangeException` if batch texts exceed 50_000 chars. Do NOT add any Azure SDK types to this file. Use file-scoped namespace. No comments. Use collection expressions (`[text]` not `new List<string> { text }`).
  Parallelization: Wave 2a | Blocked by: 1, 2 | Blocks: 4, 6
  References: `src/Services/Azure/TranslateService.cs:1-60` (exact pattern to mirror), `src/Core/Telemetry.cs:100-101`
  Acceptance criteria (agent-executed): `dotnet build src/Services/Amazon/Amazon.csproj` succeeds; `AwsTranslateResult` is `sealed record` with `DetectedLanguage` and `TranslatedText` properties
  QA scenarios: happy — build succeeds, record shape matches Azure; failure — check namespace is `Services.Amazon`, check `using Amazon.Translate` is present. Evidence .omo/evidence/task-5-add-aws-translate.txt
  Commit: Y | feat(amazon): add AwsTranslateService with inline AwsTranslateResult

- [ ] 6. Create CLI TranslateCommand.cs
  What to do / Must NOT do: Create `src/CLI/AWS/TranslateCommand.cs` (inside `AWS/` subdirectory, mirroring `src/CLI/Azure/TranslateCommand.cs`). Namespace `CLI.AWS`. Class `TranslateCommand(AwsTranslateService service) : AsyncCommand<TranslateCommand.Settings>`. Settings class with `<text>` argument, `--to <LANG>` option defaulting to `"ja"`, `--from <LANG>` optional. Call `service.TranslateBatchAsync` and print translated text. Use `[Description]` attribute on class and options. Do NOT add any Azure types or references. Use file-scoped namespace. No comments.
  Parallelization: Wave 3 | Blocked by: 5 | Blocks: 7
  References: `src/CLI/Azure/TranslateCommand.cs:1-40` (exact mirror)
  Acceptance criteria (agent-executed): `dotnet build src/CLI/CLI.csproj` succeeds (after project reference added)
  QA scenarios: happy — build succeeds; failure — check using directive `Services.Amazon` is present. Evidence .omo/evidence/task-6-add-aws-translate.txt
  Commit: Y | feat(amazon): add CLI aws translate command

- [ ] 7. Create CLI AwsCommandModule.cs
  What to do / Must NOT do: Create `src/CLI/AWS/AwsCommandModule.cs` (inside `AWS/` subdirectory, mirroring `src/CLI/Azure/AzureCommandModule.cs`). Namespace `CLI.AWS`. Static method `ConfigureCommands(IConfigurator cfg)` adding `"aws"` branch with `b.AddCommand<TranslateCommand>("translate")`. Set description to "AWS services — translation". Do NOT add other commands yet. Use file-scoped namespace. No comments.
  Parallelization: Wave 3 | Blocked by: 4, 6 | Blocks: 8
  References: `src/CLI/Azure/AzureCommandModule.cs:1-18`
  Acceptance criteria (agent-executed): `dotnet build src/CLI/CLI.csproj` succeeds
  QA scenarios: happy — build succeeds; failure — check `using CLI.AWS;` is present. Evidence .omo/evidence/task-7-add-aws-translate.txt
  Commit: Y | feat(amazon): add AwsCommandModule for CLI aws branch

- [ ] 8. Wire DI and CLI in Program.cs
  What to do / Must NOT do: In `src/App/Program.cs`: (a) add `using CLI.AWS;` and `using Services.Amazon;`, (b) call `services.AddAmazonServices()` in the try block after `services.AddGoogleServices()`, (c) call `AwsCommandModule.ConfigureCommands(cfg)` in the `app.Configure` block. Do NOT move or reorder existing Azure/Google registrations. Do NOT change the try/catch error handling. No comments.
  Parallelization: Wave 3 | Blocked by: 4, 7 | Blocks: 12
  References: `src/App/Program.cs:36-58`, `src/App/App.csproj:1-15`
  Acceptance criteria (agent-executed): `dotnet build src/App/App.csproj` succeeds; `dotnet run -- aws translate --help` shows the command description
  QA scenarios: happy — build succeeds, help output shows "AWS services — translation"; failure — check project reference to Services.Amazon exists in CLI.csproj. Evidence .omo/evidence/task-8-add-aws-translate.txt
  Commit: Y | feat(amazon): wire Amazon services and CLI into App

- [ ] 9. Add project reference to CLI.csproj
  What to do / Must NOT do: Add `<ProjectReference Include="..\Services\Amazon\Amazon.csproj" />` to `src/CLI/CLI.csproj`. Do NOT add to App.csproj (it gets transitive access through CLI.csproj). Do NOT remove any existing references. Do NOT change order of existing references.
  Parallelization: Wave 3 | Blocked by: 2 | Blocks: 8
  References: `src/CLI/CLI.csproj:1-13`
  Acceptance criteria (agent-executed): `dotnet build src/CLI/CLI.csproj` succeeds
  QA scenarios: happy — build clean; failure — check path relative to CLI.csproj location. Evidence .omo/evidence/task-9-add-aws-translate.txt
  Commit: Y | chore(amazon): add project reference for Services.Amazon

- [ ] 10. Add AWS log sink to Telemetry.cs
  What to do / Must NOT do: In `src/Core/Telemetry.cs`, add a new `.WriteTo.Logger(lc => lc.Filter.ByIncludingOnly(e => e.Properties.ContainsKey("Service") && e.Properties["Service"].ToString().Contains("AwsTranslate")).WriteTo.File(new CompactJsonFormatter(), "logs/azureai-aws-.jsonl", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7))` block. Place it after the existing Whisper sink (line 90). Do NOT modify any existing sinks.
  Parallelization: Wave 3 | Blocked by: — | Blocks: 12
  References: `src/Core/Telemetry.cs:27-92` (pattern for each sink)
  Acceptance criteria (agent-executed): `dotnet build src/Core/Core.csproj` succeeds; log files for "AwsTranslate" service will be created on first use
  QA scenarios: happy — build succeeds, file pattern `logs/azureai-aws-*.jsonl` would be created; failure — check filter string matches `Telemetry.ForService("AwsTranslate")` exactly. Evidence .omo/evidence/task-10-add-aws-translate.txt
  Commit: Y | feat(telemetry): add per-service JSONL sink for AWS

- [ ] 11. Configure AWS MCP servers in opencode.jsonc
  What to do / Must NOT do: Add five MCP server entries to `~/.config/opencode/opencode.jsonc` under the `"mcp"` key. Use Windows `uv tool run` form for local servers. IMPORTANT: Before committing to the .exe command names, run `uv tool run --from <package>@latest <package>.exe --help` to verify the entry point exists. If it doesn't, try without the `.exe` suffix. Entries:
  1. `"aws-knowledge"` — type remote, url `https://knowledge-mcp.global.api.aws`, enabled true
  2. `"aws-documentation"` — type local, command `["uv", "tool", "run", "--from", "awslabs.aws-documentation-mcp-server@latest", "awslabs.aws-documentation-mcp-server.exe"]`, environment: `{"FASTMCP_LOG_LEVEL": "ERROR", "AWS_DOCUMENTATION_PARTITION": "aws"}`, enabled true
  3. `"amazon-translate"` — type local, command `["uv", "tool", "run", "--from", "awslabs.amazon-translate-mcp-server@latest", "awslabs.amazon-translate-mcp-server.exe"]`, environment: `{"AWS_REGION": "{env:AWS_REGION}", "AWS_PROFILE": "{env:AWS_PROFILE}", "FASTMCP_LOG_LEVEL": "ERROR"}`, enabled true
  4. `"billing-cost-management"` — type local, command `["uv", "tool", "run", "--from", "awslabs.billing-cost-management-mcp-server@latest", "awslabs.billing-cost-management-mcp-server.exe"]`, environment: `{"AWS_REGION": "{env:AWS_REGION}", "AWS_PROFILE": "{env:AWS_PROFILE}", "FASTMCP_LOG_LEVEL": "ERROR"}`, enabled true
  5. `"document-loader"` — type local, command `["uv", "tool", "run", "--from", "awslabs.document-loader-mcp-server@latest", "awslabs.document-loader-mcp-server.exe"]`, environment: `{"FASTMCP_LOG_LEVEL": "ERROR"}`, enabled true

  Do NOT modify any existing MCP entries. Do NOT add `"autoApprove"` or `"disabled"` keys. Do NOT hardcode AWS credentials or profile names.
  Parallelization: Wave 4 | Blocked by: — | Blocks: 12
  References: `~/.config/opencode/opencode.jsonc:13-70` (existing MCP config pattern), awslabs/mcp READMEs for each server
  Acceptance criteria (agent-executed): OpenCode starts without MCP connection errors; `AWS_REGION` and `AWS_PROFILE` are set in shell environment; `uv --version` returns successfully
  QA scenarios: happy — opencode.jsonc is valid JSON (parseable), all five entries present, `uv --version` works; failure — if opencode fails to start MCP, check that `uv` is installed and on PATH, check that the .exe names match the package names. Evidence .omo/evidence/task-11-add-aws-translate.txt
  Commit: Y | feat(config): add AWS MCP servers to opencode

- [ ] 12. Run translation test
  What to do / Must NOT do: Execute the translation test. Two paths:
  - **Primary (MCP):** Ask the assistant to use the `amazon-translate` MCP `translate_text` tool: `text="Jahan Teri Yeh Nazar Hai Mujhe Hai Jaan Mujhe Khabar Hai"`, `source_language="auto"`, `target_language="de"`. Verify output is non-empty German text.
  - **Fallback (CLI):** Run `dotnet run --project src/App -- aws translate "Jahan Teri Yeh Nazar Hai Mujhe Hai Jaan Mujhe Khabar Hai" --to de`. Verify output is non-empty German text.
  Do NOT skip verification. Do NOT assert the exact translation — just confirm it's non-empty and contains German characters.
  Parallelization: Wave 5 | Blocked by: 8, 10, 11 | Blocks: —
  References: `src/CLI/Azure/TranslateCommand.cs:14-19` (test pattern), amazon-translate-mcp-server README
  Acceptance criteria (agent-executed): German text is printed to stdout; the detected language is Hindi or "auto"; no exception is thrown
  QA scenarios: happy — German output appears (e.g., contains umlauts or common German words); failure — if MCP fails, fall back to CLI; if CLI fails, check AWS_REGION is set and AWS credentials are available via default chain. Evidence .omo/evidence/task-12-add-aws-translate.txt
  Commit: N | (test only, no commit needed)

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. Plan compliance audit — verify all 12 todos are present, each has references + acceptance + QA + commit
- [ ] F2. Code quality review — `dotnet build` clean, no style warnings, C# conventions followed
- [ ] F3. Real manual QA — German translation test passes via MCP or CLI
- [ ] F4. Scope fidelity — no changes to Azure/Google behavior, no credential storage, no extra AWS services

## Commit strategy
1. `chore(packages): add AWSSDK.Translate V4` (Task 1)
2. `feat(amazon): add Services.Amazon project skeleton` (Task 2)
3. `feat(amazon): add AwsCredentials reading AWS_REGION from env` (Task 3)
4. `feat(amazon): add AwsTranslateService with inline AwsTranslateResult` (Task 5)
5. `feat(amazon): add AwsSetup with DI registration` (Task 4)
6. `feat(amazon): add CLI aws translate command` (Tasks 6, 7)
7. `feat(amazon): add project reference for Services.Amazon` (Task 9)
8. `feat(amazon): wire Amazon services and CLI into App` (Task 8)
9. `feat(telemetry): add per-service JSONL sink for AWS` (Task 10)
10. `feat(config): add AWS MCP servers to opencode` (Task 11)
11. (Task 12 — test, no commit)

## Success criteria
- `dotnet build src/App/App.csproj` succeeds with zero errors/warnings
- `dotnet run --project src/App -- aws translate "Jahan Teri Yeh Nazar Hai Mujhe Hai Jaan Mujhe Khabar Hai" --to de` outputs German text
- `~/.config/opencode/opencode.jsonc` contains all five AWS MCP server entries
- OpenCode starts without MCP connection errors when AWS_REGION and AWS_PROFILE are set
- No existing Azure/Google behavior changed (regression check)
