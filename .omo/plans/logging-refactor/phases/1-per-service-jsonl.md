# Phase 1: Per-Service JSONL Files

## Task
Replace per-run timestamped log files with persistent per-service JSONL files.

## Steps
1. Read src/Core/Telemetry.cs and src/Core/ServiceName.cs
2. Change Serilog file sink configuration from timestamped filenames to per-service names
3. Each ServiceName value maps to a logs/{name}.jsonl file
4. Rolling retention: 7 days, shared across all services
5. Ensure each log entry is a single JSON object on one line (JSONL)

## Verify
```powershell
dotnet build
dotnet run --project src/App -- sync yt
Get-ChildItem logs/*.jsonl | Select Name
Get-Content logs/youtube.jsonl | Select-Object -First 1 | ConvertFrom-Json
```
Expected: Per-service files (youtube.jsonl, translate.jsonl, etc.). First line is valid JSON.

## Commit
`refactor(logging): per-service JSONL files with 7-day rolling retention`
