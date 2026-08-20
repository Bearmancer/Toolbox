# Telemetry Integration & Audio Debug Loss — Caveman Ultra

## Points Enumerated

P-01: File sink ignores LevelSwitch | BUG | restrictedToMinimumLevel:Debug drops Verbose silently. Propagate LevelSwitch to AddServiceLogger.
P-02: Seq TCP probe blocks startup | OVERENGINEERING | TCP open != HTTP healthy. Serilog retries natively. Drop probe.
P-03: DsdConvertService Debug drops | BUG | No ForService scope. Direct CLI calls lose logs. Wrap Telemetry.Log to enforce scope.
P-04: Azure SDK listeners bypass enum | LAYER MISPLACEMENT | String literals break enum routing. Force ServiceName enum.
P-05: LogPaths global string replace | LAYER MISPLACEMENT | Global mutable state outside Telemetry. Convert to Serilog Enricher.
P-06: 8/10 empty log files | BUG | Consequence of missing ForService + Verbose drop. Enforce scope, fix level.

## Dead Code Catalog

DC-01: Verbose file logs @ Telemetry.cs:64 | unused | --verbose flag set, restrictedToMinimumLevel:Debug drops event before file write.
DC-02: Seq TCP probe handlers @ Telemetry.cs:103 | unused | TCP probe unneeded. Serilog handles sink failures natively.

## Overengineering Assessment

OE-01: Seq TCP Startup Probe | CUT | Solo dev: native Serilog buffer/retry sufficient. Custom probe adds latency, no resilience gain.
OE-02: LogPaths custom formatter | CUT | Solo dev: custom replacement duplicates Serilog Enricher capability. Move to native.
OE-03: 10 per-service JSONL loggers | KEEP | Solo dev: clean routing pattern. Empty files indicate missing push scope, not bad architecture.

## Features At Risk

FR-01: Per-service JSONL routing | Telemetry.cs | Not dead. Empty files expose missing ForService calls. Keep routing, fix callers.
FR-02: DSD Gain calculation telemetry | DsdConvertService.cs | Logic valid. Logs invisible due to missing scope. Wrap logging to preserve visibility.

## Cross-Reference Verification

CR-01: File sink restrictedToMinimumLevel:Debug | Telemetry.cs:64 | MATCH
CR-02: Seq probe TCP localhost check | Telemetry.cs:100 | MATCH
CR-03: PipelineOrchestrator ForService(Audio) scope | PipelineOrchestrator.cs:27 | MATCH
CR-04: DsdConvertService ProbeStart lacks ForService | DsdConvertService.cs:40 | MATCH
CR-05: SDK Listeners bypass enum | Program.cs:60 | MATCH
CR-06: LogPaths global state | PipelineOrchestrator.cs:68 | MATCH
CR-07: 8/10 log files 0B | state/logs directory | MATCH (Verified audio 3.5KB, youtube 1MB, rest empty)
