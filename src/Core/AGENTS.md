# Core — Zero-Dep Utility Layer

Leaf. All `Services.*` → Core. Never reverse. No service knowledge here.

## Structure

```
Core/
├── Core.csproj     # ErrorOr, Serilog+Compact/File/Seq/Spectre/Tracing, Spectre.Console, SSH.NET
├── Telemetry.cs    # Per-service JSONL (CompactJsonFormatter, 50 MB) + Spectre console + Seq probe
├── Errors.cs       # Domain factories: General/Validation/YouTube/Azure/LastFm/DocIntel/Speech/Vision/OpenAi/Translate/TextAnalytics/Audio
├── PathResolver.cs # RepoRoot, GetStatePath(), ResolveInput(), ReadChecked()
├── ServiceName.cs  # 10 values + ToFileSlug() extension
├── Text.cs         # SanitizeFileName + string extensions (IsEqualTo, Has, StartsWith)
└── OciConfig.cs    # OCI deploy constants: Host, User, KeyPath (~/.ssh/oci/id_ed25519)
```

`Telemetry.cs`: `state/logs` = `Path.Combine(RepoRoot,"state","logs")`; one JSONL per `ServiceName` via `Enum.GetValues`→`AddServiceLogger` filtering on `Service==service.ToString()`; `LevelSwitch` controls Spectre sink; Seq at `SEQ_URL`|`localhost:5341` gated by 500 ms TCP probe.
`ServiceName.cs`: `LastFm, YouTube, OpenAi, Vision, Translate, TextAnalytics, Speech, DocIntel, Audio, SdkDiagnostics` → `ToFileSlug()` maps `TextAnalytics→textanalytics`, `SdkDiagnostics→sdk`.
`PathResolver.cs`: `RepoRoot` lazy walks ≤10 parents for `.git`/`.env`, fallback `GetCurrentDirectory()`; `ResolveInput()` resolves relative via `resources/`; `ReadChecked(path,maxBytes,service)`.

## Where to Look

| Task | File | Note |
|------|------|------|
| Add error category | `Errors.cs` | Add `static class Errors.{Domain}` with `Error.*` factories |
| Add service to telemetry | `ServiceName.cs` + `Telemetry.cs` | Add enum value + `ToFileSlug` case; `Configure()` auto-creates logger via `Enum.GetValues` |
| Change log format/sink | `Telemetry.cs` | `AddServiceLogger()` owns file sink; `Configure()` owns Spectre+Seq |
| Resolve paths/state | `PathResolver.cs` | `RepoRoot`, `GetStatePath(subdir)`, `ResolveInput()`, `ReadChecked()` |
| OCI deploy target | `OciConfig.cs` | `Host`/`User`/`KeyPath` — consumed by dashboard deploy |

## Conventions

- Fallible → `ErrorOr<T>` via `Errors.{Domain}` factories (`Errors.cs` taxonomy, not ad-hoc `Error.Failure`).
- Telemetry scope: `using var _ = Telemetry.ForService(ServiceName.X);` pushes `Service` property for `AddServiceLogger` filter.
- Paths via `PathResolver.RepoRoot`/`GetStatePath()` — never hardcode `state/`.
- No `Services.*` references. No domain logic. Pure utilities.

## Anti-Patterns

- **NEVER** add service-specific logic to Core.
- **NEVER** add `ServiceName` value without `ToFileSlug()` case — `Configure()` will throw `ArgumentOutOfRangeException`.
- **NEVER** bypass `PathResolver` with `Directory.GetCurrentDirectory()` — breaks when `AppContext.BaseDirectory` ≠ CWD.
