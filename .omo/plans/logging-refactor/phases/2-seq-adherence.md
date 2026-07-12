# Phase 2: Seq Adherence

## Task
Ensure Seq sink (if configured via SEQ_URL) also outputs JSONL format.

## Steps
1. Read src/Core/Telemetry.cs Configure method
2. Verify Seq sink configuration uses JSON formatter
3. If not, add Serilog.Formatting.Json.JsonFormatter to Seq sink

## Verify
```powershell
dotnet build
Select-String -Path "src/Core/Telemetry.cs" -Pattern "JsonFormatter"
```
Expected: Seq sink uses JSON formatter. Build clean.

## Commit
`fix(telemetry): ensure Seq sink uses JSONL format`
