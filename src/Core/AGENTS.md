# Core Layer

Shared utilities: telemetry, errors, path resolution, text helpers. Zero dependencies on Services.

## STRUCTURE

```
Core/
├── Telemetry.cs     # Serilog config, per-service JSONL, Seq sink, ForService() scope
├── Errors.cs        # ErrorOr taxonomy: General, Validation, YouTube, Azure, LastFm, etc.
├── PathResolver.cs  # RepoRoot detection, input path resolution, file size checks
├── ServiceName.cs   # Enum: LastFm, YouTube, OpenAI, Vision, Translate, TextAnalytics, Speech, DocIntel, Audio
└── Text.cs          # String sanitization for file names
```

## WHERE TO LOOK

| Task                     | File                              | Notes                                                  |
| ------------------------ | --------------------------------- | ------------------------------------------------------ |
| Add error category       | `Errors.cs`                       | Add `ErrorFactory` static class, e.g. `Errors.YouTube`, `Errors.Audio`  |
| Add service to telemetry | `ServiceName.cs` + `Telemetry.cs` | Add enum value, add to `RegisteredServices` array      |
| Change log format        | `Telemetry.cs`                    | `AddServiceLogger()` controls per-service file sink    |
| Resolve file paths       | `PathResolver.cs`                 | `RepoRoot`, `ResolveInput()`, `ReadChecked()`          |

## CONVENTIONS

- **ErrorOr pattern:** All fallible operations return `ErrorOr<T>`. Errors are typed via `ErrorFactory` methods.
- **Telemetry scoping:** `using var _ = Telemetry.ForService(ServiceName.X);` at operation start.
- **Path resolution:** `PathResolver.RepoRoot` walks up from `AppContext.BaseDirectory` looking for `.git` or `.env`.
- **No service references.** Core must never reference `Services.*` projects.

## ANTI-PATTERNS

- **NEVER** add service-specific logic to Core. Core is a utility layer, not a knowledge hub.
- **NEVER** add new `ServiceName` enum values without also adding the corresponding JSONL logger in `Telemetry.cs`.
