# CLI Layer

Spectre.Console.Cli commands. Thin wrappers only — no service logic here.

## STRUCTURE

```
CLI/
├── TypeRegistrar.cs          # Bridges Spectre DI to IServiceProvider
├── Azure/
│   ├── AzureCommandModule.cs # "azure" branch: translate, docintel, vision, stt, ner, phrases
│   ├── TranslateCommand.cs
│   ├── DocIntelCommand.cs
│   ├── VisionCommand.cs
│   ├── SpeechSttCommand.cs
│   ├── NerCommand.cs
│   ── PhrasesCommand.cs
├── Dashboard/
│   ├── DashboardCommandModule.cs  # "dashboard" branch: generate
│   ├── DashboardGenerateCommand.cs
│   ├── DashboardHtmlGenerator.cs
│   └── DashboardDataBuilder.cs
└── Sync/
    ├── SyncCommandModule.cs  # "sync" branch: youtube, lastfm
    └── YouTube/
        └── SyncYoutubeCommand.cs
└── Sync/
    ├── SyncCommandModule.cs  # "sync" branch: youtube, lastfm
    └── YouTube/
        └── SyncYoutubeCommand.cs
```

## WHERE TO LOOK

| Task                 | File                          | Notes                                                             |
|----------------------|-------------------------------|-------------------------------------------------------------------|
| Add Azure subcommand | `Azure/AzureCommandModule.cs` | Register in `ConfigureCommands`, create command class             |
| Add sync subcommand  | `Sync/SyncCommandModule.cs`   | Same pattern                                                      |
| Command pattern      | Any `*Command.cs`             | `IRemainingArguments` → service call → `result.Match` → exit code |

## CONVENTIONS

- **Thin commands only.** Command class receives args, calls service, prints result, returns 0/1.
- **No business logic in commands.** Orchestration belongs in `Services/`.
- **Result matching:**
  `result.Match(onSuccess: Console.WriteLine, onError: e => { Console.Error.WriteLine(e.Description); return 1; })`
- **Cancellation:** All commands accept `CancellationToken ct` from Spectre's built-in support.

## ANTI-PATTERNS

- **NEVER** put merge/state/pagination logic in a command. Extract to service layer.
- **NEVER** import `Core` for business logic. CLI uses Core only for `Telemetry`, `Errors`, `PathResolver`.
