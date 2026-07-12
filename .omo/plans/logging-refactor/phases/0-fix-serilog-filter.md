# Phase 0: Fix Serilog Filter Bug

## Task
Fix the ScalarValue filter bug in Telemetry.cs where Serilog filter does not properly unwrap ScalarValue to compare against service names.

## Steps
1. Read src/Core/Telemetry.cs, find the ForService method and its filter logic
2. Replace broken ScalarValue comparison with proper unwrapping
3. Build and verify

## Verify
```powershell
dotnet build
Select-String -Path "src/Core/Telemetry.cs" -Pattern "ScalarValue"
```
Expected: 0 errors. No broken filter references.

## Commit
`fix(telemetry): fix Serilog ScalarValue filter bug`
