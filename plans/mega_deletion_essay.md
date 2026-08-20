# The Ponytail Audit: The Architecture of Deletion

The following is an exhaustive compilation of findings from 14 independent seal-team audits across the workspace. It categorizes the architectural bloat, overengineering patterns, reinvented wheels, and domain muddling currently plaguing the codebase. 

The core philosophy of this audit is: **The best code is the code never written.** We prioritize deletion over addition, standard library over dependencies, and single-line native features over hand-rolled abstractions.

---

## 1. Separation of Concerns & God Methods
*The most dangerous bloat is code that does everything. Tightly coupled domains create massive, untestable God methods that entangle network I/O with local file mutations and CLI logic.*

### The Crimes
1. **Google Services Deploying to Oracle Cloud (`Services.Google.Dashboard`)**
   - **Finding:** The `Services.Google` namespace contains an `OciDashboardDeployer.cs` that handles SSH/SFTP deployment to Oracle Cloud Infrastructure (OCI). 
   - **Rationale for Deletion:** Google APIs have absolutely nothing to do with Oracle Cloud Linux VMs. This is a severe architectural violation. The CLI `SyncYoutubeCommand` compounds this by triggering YouTube syncs and *then* deploying an HTML dashboard. This must be ripped out into a dedicated `Core.Infrastructure` or `Services.Deployment` namespace, and the CLI must expose a separate `dashboard deploy` command.
2. **The 601-Line Pristine God Method (`PristinePollService.cs`)**
    - **Finding:** Contains a massive 601-line god method with 44 catch blocks attempting to micro-manage browser interactions.
   - **Rationale for Deletion:** Playwright has built-in, highly resilient auto-waiting, timeout, and state-verification mechanisms. Manually wrapping every element selector in a try/catch loop defeats the purpose of the framework and makes the file unreadable. 
3. **The YouTube Merger Leviathan (`YouTubeDuplicateMerger.cs`)**
   - **Finding:** A 450-line God method (`MergeDuplicateGroupsAsync`) that interleaves Google API inserts/deletes with direct filesystem mutations (`File.WriteAllTextAsync`, `Directory.CreateDirectory`).
   - **Rationale for Deletion:** File I/O and Network I/O must not live in the same method. The merger should be pure domain logic that returns a `ChangeDetectionResult`, which an outer orchestrator then applies to the API and filesystem.
4. **CLI Orchestrating Complex Pipelines (`CLI.Audio.DsdConvertCommand`)**
   - **Finding:** The CLI command's `ExecuteAsync` is 140 lines of creating temporary GUID directories, calculating audio gain, running `ffmpeg`, and manipulating tags.
   - **Rationale for Deletion:** The CLI layer should bind arguments and exit. Complex pipeline orchestration must live in the `Services` layer (as seen correctly in `SacdConvertCommand`). 
5. **Shelling out to ffmpeg in a Service Library (`Services.Azure.SpeechService`)**
   - **Finding:** A core Azure SDK service library invokes `ffmpeg.exe` via shell to convert audio formats.
   - **Rationale for Deletion:** Library layers should not have hard dependencies on local system executables unless explicitly designed as a wrapper. Use Azure Speech SDK's native compressed audio streams or enforce the format at the application boundary.

---

## 2. Reinventing the Wheel (Ignored Native Features)
*Writing custom code for features that Microsoft or the platform already ships for free.*

### The Crimes
1. **Hand-rolled Rate Limiting & Retries (`Services.LastFm`)**
   - **Finding:** `LastFmService` uses manual `for`-loops, exponential `Task.Delay` backoffs, and handwritten `LastRequestTime` throttles.
   - **Rationale for Deletion:** Delete completely. Replace with native .NET `Microsoft.Extensions.Http.Resilience` (`.AddStandardResilienceHandler()`) configured in the DI container.
2. **Manual Process Runner (`Services.Audio.ProcessRunner.cs`)**
   - **Finding:** A heavily abstracted class for async process execution, timeouts, and cancellation.
   - **Rationale for Deletion:** `.NET` natively supports `System.Diagnostics.Process.WaitForExitAsync(CancellationToken)`. 
3. **Manual DSD/DFF Parsing (`Services.Audio.DffHeaderReader.cs`)**
   - **Finding:** 507 lines of manual binary header parsing (144 + 363).
   - **Rationale for Deletion:** The project already depends on `z440.atl.core`, which reads DSD/DFF metadata out of the box. 
4. **Manual UTF-8 Validation (`Services.Audio.CueParser.cs`)**
   - **Finding:** A manual byte-by-byte scanner to check UTF-8 validity.
   - **Rationale for Deletion:** `System.IO.StreamReader` natively supports BOM detection and encoding validation via `detectEncodingFromByteOrderMarks=true`.
5. **Reinventing Azure Extensions (`Services.Azure`)**
   - **Finding:** Hand-rolled ETW listeners, manual `Telemetry.StartActivity` spans, and custom DI client singletons.
   - **Rationale for Deletion:** The `Microsoft.Extensions.Azure` library handles all ETW forwarding, OpenTelemetry integration, and DI lifecycle management automatically. 
6. **Manual Argument Parsing (`CLI.Sync.SyncLastFmCommand`)**
   - **Finding:** Manually validating a string and passing it to `DateTimeOffset.TryParse`.
   - **Rationale for Deletion:** Spectre.Console natively binds and validates `DateTimeOffset` properties on command settings.

---

## 3. The "Wrapper" Epidemic (YAGNI & Class Bloat)
*Wrapping single-line native calls or configuring layers of indirection for features that do not exist.*

### The Crimes
1. **Stateless Wrapper Classes (`Services.Audio`)**
   - **Finding:** `DiskSpaceChecker.cs` and `PathValidator.cs` provide expansion factors, a 500MB margin, and `ErrorOr<string>` validation (not trivial wrappers). 
   - **Rationale for Deletion:** These are pure, stateless functions. Wrapping them in injected classes forces consumers to mock the filesystem unnecessarily. Inline them or use static helpers.
2. **Over-abstracted Command Modules (`CLI.Dashboard`, `CLI.Pristine`)**
   - **Finding:** `DashboardCommandModule.cs` exists solely to call `cfg.AddBranch`.
   - **Rationale for Deletion:** Command branching should be flattened into the primary CLI orchestrator. 
3. **The Four-Headed LastFm Monster (`Services.LastFm`)**
   - **Finding:** Fetching a JSON endpoint and saving it is split across `LastFmApiClient`, `LastFmService`, `LastFmSyncOrchestrator`, and `LastFmState`.
   - **Rationale for Deletion:** The orchestrator is a wrapper around a wrapper. Collapse these into a single service.
4. **Dead Interfaces & Registries**
   - **Finding:** `Core.Errors` centralized registry, `ServiceName` enum mappings, and custom `String` extensions.
   - **Rationale for Deletion:** Serilog handles dynamic routing natively via `WriteTo.Map()`. Native string operators (`==`, `.StartsWith`) are universally understood; custom wrappers decrease readability.

---

## 4. Dependency Bloat
*NuGet packages that add compilation overhead and cognitive load but provide zero unique value.*

### The Crimes
1. **`ErrorOr`**
   - **Rationale for Deletion:** Wrapping every single return type in a Railway-Oriented Programming monad is massive overkill for a CLI tool. A simple 6-line native C# 10 Tuple or custom `Result<T>` struct (`public readonly record struct Result<T>(T? Value, string? Error)`) achieves the exact same safety without forcing a 30-file dependency into the binary.
2. **`DotNetEnv`**
   - **Rationale for Deletion:** Loading a `.env` file does not require a library. Three lines of `File.ReadAllLines` or standard `Microsoft.Extensions.Configuration` can bind env vars to strongly typed options.
3. **`Spectre.Console.Cli.Extensions.DependencyInjection`**
   - **Rationale for Deletion:** The project already utilizes a hand-rolled `TypeRegistrar` bridging Microsoft DI to Spectre. This extension package is totally redundant.
4. **Serilog Sprawl**
   - **Rationale for Deletion:** 5 distinct Serilog packages are currently referenced. Consolidating around the native `Microsoft.Extensions.Logging` abstraction would decouple the app from a specific logging vendor.

---

## 5. Domain Model Pollution & Anti-Patterns

### The Crimes
1. **Hardcoded Timezones in `LastFmService.cs:11`**
   - **Finding:** `LastFmService.cs:11` hardcodes `TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30)` (IST) and formats a display string.
   - **Rationale for Deletion:** Domain entities must hold raw `DateTimeOffset` UTC values. Formatting and timezone adjustments belong exclusively in the View/Presentation layer.
2. **HTML as C# Strings (`DashboardHtmlGenerator.cs`)**
   - **Finding:** A 365-line class returning a raw HTML/JS literal string.
   - **Rationale for Deletion:** Storing half a megabyte of front-end code inside a compiled backend binary destroys syntax highlighting, formatting, and maintainability. It must be extracted to a static `dashboard.html` asset.
3. **Token Poisoning via Global Prompts (`.commandcode/taste/-/taste.md`)**
   - **Finding:** The global system prompts contain hyper-niche rules (Fakémon exclusions, USB-C e-marker specifications, Windows 10 mandates) and exact duplicated lines.
   - **Rationale for Deletion:** You are paying AI token costs for USB cable specs every time you ask for a C# refactor. Global instructions must be fiercely minimalist. 
4. **Empty Artifact Hoarding (`.playwright-cli`, `docs`, `old`)**
   - **Finding:** 32 empty or obsolete log files, historical design justifications (323-line SDET essay), completed checkboxes, and entirely dead Python/C# scripts.
   - **Rationale for Deletion:** Checked-off to-do lists and deprecated prototypes provide zero runtime value and actively pollute codebase searches. Nuke them.

---

## 6. Deep Infrastructure & Disk Bloat
*Seal Team Six uncovered massive dead-weight storage usage at the physical layer.*

### The Crimes
1. **Centralized Compiler Hoarding**
   - **Finding:** The workspace routes all compilation via `.NET` to the `artifacts/` folder. It is swollen with gigabytes of nested `bin/obj` artifacts.
   - **Rationale for Deletion:** `artifacts/` can be nuked entirely to instantly reclaim space; it will rebuild cleanly.
2. **Rogue PostgreSQL Database (`old/Scripts/state/postgres`)**
   - **Finding:** An entirely detached, abandoned PostgreSQL cluster hoarding megabytes of `pg_wal` write-ahead logs.
   - **Rationale for Deletion:** Stale local dev-database files that are no longer used. Instant delete.
3. **Nested Orphaned Git Repo (`old/Scripts/.git`)**
   - **Finding:** A completely hidden `.git` folder inside the old scripts directory maintaining a secondary 18.5MB object pack.
   - **Rationale for Deletion:** Redundant version control within version control. Delete immediately.
4. **Gigabytes of JSON Caching & Markdown Logging**
   - **Finding:** The root is littered with 1MB+ markdown session logs (`session-ses_fe5d.md`), 14MB+ LastFm state files (`scrobbles.json`), and 145+ massive cached YouTube JSON files in `state/youtube/raw/`.
   - **Rationale for Deletion:** Raw network caching is essential for development but needs an automated eviction protocol or `.gitignore` exclusion so it doesn't rot locally forever.
