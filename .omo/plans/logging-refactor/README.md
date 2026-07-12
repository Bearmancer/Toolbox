# Plan: Logging Refactor

**Task:** T-542e72d6-b9e1-41c1-b117-f1be74a95ba6

## Goal
Replace per-run timestamped log files with persistent per-service JSONL files. Fix Serilog filter bug.

## Phases
- **0-fix-serilog-filter.md** — Fix ScalarValue filter bug in Telemetry.cs
- **1-per-service-jsonl.md** — One JSONL file per service, rolling 7-day retention
- **2-seq-adherence.md** — Ensure Seq sink outputs JSONL format

## Verify
```powershell
Get-ChildItem logs/*.jsonl | Select Name
Get-Content logs/sync.jsonl | Select-Object -First 1 | ConvertFrom-Json
dotnet build
```
