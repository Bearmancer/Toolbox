# CLI Layer

Spectre.Console.Cli thin wrappers — `Settings` → service → `result.Match` → 0/1. No business logic.

## STRUCTURE

```
CLI/
├── TypeRegistrar.cs              # Spectre ITypeRegistrar → IServiceProvider bridge
├── Azure/
│   ├── AzureCommandModule.cs     # "azure" branch: translate, docintel, vision, stt, tts, ner, phrases
│   ├── TranslateCommand.cs
│   ├── DocIntelCommand.cs
│   ├── VisionCommand.cs
│   ├── SpeechSttCommand.cs
│   ├── SpeechTtsCommand.cs
│   ├── NerCommand.cs
│   └── PhrasesCommand.cs
├── Audio/
│   ├── AudioCommandModule.cs     # "audio" branch: sacd-convert, dsd-convert
│   ├── SacdConvertCommand.cs
│   └── DsdConvertCommand.cs
├── Dashboard/
│   ├── DashboardCommandModule.cs # "dashboard" branch: generate
│   ├── DashboardGenerateCommand.cs
│   ├── DashboardDataBuilder.cs
│   ├── DashboardHtmlGenerator.cs
│   └── OciDashboardDeployer.cs   # lives in CLI (not Services) — repo truth
└── Sync/
    ├── SyncCommandModule.cs      # "sync" branch: youtube, lastfm
    ├── YouTube/SyncYoutubeCommand.cs
    └── LastFm/SyncLastFmCommand.cs
```

## WHERE TO LOOK

| Task | File | Notes |
|------|------|-------|
| Add Azure subcommand | `Azure/AzureCommandModule.cs` | `AddCommand<T>` in `AddBranch("azure")`, new `AsyncCommand<Settings>` |
| Add Audio subcommand | `Audio/AudioCommandModule.cs` | Same pattern |
| Add Dashboard subcommand | `Dashboard/DashboardCommandModule.cs` | Same pattern |
| Command pattern | Any `*Command.cs` | `AsyncCommand<Settings>` → `service.CallAsync(ct)` → `result.Match` → exit code |

## CONVENTIONS

- **Thin only.** `ExecuteAsync(Settings, CancellationToken)` → service `ErrorOr<T>` → `Match(onSuccess→0, onError→1)`. No orchestration.
- **No business logic.** Merge/state/pagination/ETag → `Services/` orchestrator.
- **Result matching.** `result.Match(v => { Console.WriteLine(v); return 0; }, e => { Console.Error.WriteLine(e.Description); return 1; })`
- **Cancellation.** `CancellationToken ct` from Spectre `ExecuteAsync` signature — pass to service.

## ANTI-PATTERNS

- **NEVER** merge/state/pagination in command — extract to service layer.
- **NEVER** import `Core` for business logic — CLI uses `Core` only for `Telemetry`/`Errors`.
