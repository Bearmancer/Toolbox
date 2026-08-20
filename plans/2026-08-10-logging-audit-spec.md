# Logging Audit Specification

**Date:** 2026-08-10
**Auditor:** Oracle (read-only consultation)
**Scope:** All projects in C:\Users\Lance\Dev\Toolbox

---

## 1. Objective

Identify all locations in the codebase where failures, errors, and warnings are **not properly logged**, making debugging difficult. Focus on:

- Silent failures (exceptions caught but not logged)
- Missing error context (logged but insufficient detail)
- Inconsistent logging patterns
- Missing warning logs for recoverable errors
- Missing telemetry for external service calls

---

## 2. Audit Scope

### Projects to Audit

| Project        | Path                   | Priority |
| -------------- | ---------------------- | -------- |
| Audio Service  | `src/Services/Audio/`  | HIGH     |
| Azure Service  | `src/Services/Azure/`  | HIGH     |
| Google Service | `src/Services/Google/` | HIGH     |
| LastFm Service | `src/Services/LastFm/` | MEDIUM   |
| Core           | `src/Core/`            | MEDIUM   |
| CLI            | `src/CLI/`             | LOW      |
| App            | `src/App/`             | LOW      |
| SacdProbe      | `tools/SacdProbe/`     | LOW      |

### File Types to Audit

- `.cs` files (C# source code)
- Focus on service classes, not DTOs/models

---

## 3. What Constitutes "Missing Logging"

### 3.1 Silent Failures (CRITICAL)

**Pattern:** Exception caught but not logged

```csharp
// BAD
try { ... }
catch (Exception ex) { return false; }

// GOOD
try { ... }
catch (Exception ex) {
    Telemetry.Error("Operation failed: {Error}", ex.Message);
    return false;
}
```

**Detection:**

- `catch` blocks without `Telemetry.Error` or `Telemetry.Warn`
- Empty catch blocks
- Catch blocks that only return without logging

### 3.2 Insufficient Error Context (HIGH)

**Pattern:** Error logged but missing critical context

```csharp
// BAD
catch (Exception ex) {
    Telemetry.Error("Failed");
}

// GOOD
catch (Exception ex) {
    Telemetry.Error("Operation failed for {File}: {Error}", filePath, ex.Message);
}
```

**Detection:**

- Log messages without structured parameters
- Missing file paths, user IDs, operation names
- Generic error messages like "Error occurred"

### 3.3 Missing Warning Logs (MEDIUM)

**Pattern:** Recoverable errors that should warn but don't

```csharp
// BAD
if (!File.Exists(path)) return defaultValue;

// GOOD
if (!File.Exists(path)) {
    Telemetry.Warn("File not found, using default: {Path}", path);
    return defaultValue;
}
```

**Detection:**

- Early returns without logging
- Fallback logic without warnings
- Conditional branches that skip operations

### 3.4 Missing External Call Logging (HIGH)

**Pattern:** External service calls without telemetry

```csharp
// BAD
var result = await httpClient.GetAsync(url);

// GOOD
Telemetry.Debug("HTTP request: {Method} {Url}", HttpMethod.Get, url);
var result = await httpClient.GetAsync(url);
if (!result.IsSuccessStatusCode) {
    Telemetry.Warn("HTTP request failed: {StatusCode} {Url}", result.StatusCode, url);
}
```

**Detection:**

- HTTP client calls without logging
- Database queries without logging
- External API calls without logging
- Process execution without logging (ProcessRunner)

### 3.5 Inconsistent Logging Levels (LOW)

**Pattern:** Wrong log level for severity

```csharp
// BAD
catch (Exception ex) {
    Telemetry.Debug("Critical failure: {Error}", ex.Message);  // Should be Error
}

// GOOD
catch (Exception ex) {
    Telemetry.Error("Critical failure: {Error}", ex.Message);
}
```

**Detection:**

- `Debug` level for actual errors
- `Info` level for warnings
- Missing log level consistency

---

## 4. Logging Standards Reference

### 4.1 Telemetry API

The codebase uses `Core.Telemetry` for logging:

```csharp
Telemetry.Debug(string message, params object[] args)
Telemetry.Info(string message, params object[] args)
Telemetry.Warn(string message, params object[] args)
Telemetry.Error(string message, params object[] args)
```

### 4.2 Structured Logging

All log messages should use structured parameters:

```csharp
// GOOD
Telemetry.Error("Failed to convert {File}: {Error}", filePath, ex.Message);

// BAD
Telemetry.Error($"Failed to convert {filePath}: {ex.Message}");  // String interpolation
```

### 4.3 Log Level Guidelines

| Level     | When to Use                   | Examples                                           |
| --------- | ----------------------------- | -------------------------------------------------- |
| **Debug** | Development/diagnostic info   | Method entry/exit, intermediate values             |
| **Info**  | Normal operation milestones   | "Started conversion", "Completed sync"             |
| **Warn**  | Recoverable errors, fallbacks | "File not found, using default", "Retry attempt 1" |
| **Error** | Failures, exceptions          | "Conversion failed", "API call failed"             |

---

## 5. Audit Output Format

### 5.1 Findings Table

For each project, produce a table:

```markdown
## Audio Service

| File                 | Line | Issue                      | Severity | Fix                  |
| -------------------- | ---- | -------------------------- | -------- | -------------------- |
| SaraconService.cs    | 45   | Silent catch block         | CRITICAL | Add Telemetry.Error  |
| DsdConvertService.cs | 123  | Missing file path in error | HIGH     | Add {File} parameter |
| SoxService.cs        | 78   | No warning on fallback     | MEDIUM   | Add Telemetry.Warn   |
```

### 5.2 Summary Statistics

```markdown
## Summary

- Total files audited: 42
- Critical issues: 5
- High issues: 12
- Medium issues: 8
- Low issues: 3
- Total issues: 28
```

### 5.3 Priority Fix List

Ordered list of fixes by severity and impact:

```markdown
## Priority Fixes

1. **CRITICAL:** SaraconService.cs:45 - Silent catch block
   - Add: `Telemetry.Error("Saracon conversion failed: {Error}", ex.Message);`

2. **CRITICAL:** DsdConvertService.cs:89 - Empty catch block
   - Add: `Telemetry.Error("DSD probe failed for {File}: {Error}", filePath, ex.Message);`
```

---

## 6. Success Criteria

Audit is complete when:

- [ ] All 7 projects audited
- [ ] All `.cs` files in scope reviewed
- [ ] Findings table generated for each project
- [ ] Summary statistics calculated
- [ ] Priority fix list created
- [ ] Output saved to `docs/superpowers/audits/2026-08-10-logging-audit.md`

---

## 7. Oracle Instructions

**Your task:**

1. Read all `.cs` files in the audit scope
2. Identify missing logging patterns (Section 3)
3. Generate findings table (Section 5.1)
4. Calculate summary statistics (Section 5.2)
5. Create priority fix list (Section 5.3)
6. Save output to specified path

**Constraints:**

- Read-only analysis (no code changes)
- Focus on service classes, skip DTOs/models
- Use Telemetry API as reference (Section 4.1)
- Be thorough but pragmatic (don't flag every Debug log)

**Output:**

- Single markdown file at `docs/superpowers/audits/2026-08-10-logging-audit.md`
- Follow format in Section 5

---

## 8. Examples from Current Codebase

### 8.1 Good Example (SaraconService.cs)

```csharp
// Line 104-117 (after recent fix)
var result = await processRunner.RunAsync(...);
if (result.IsError)
{
    var error = result.Errors[0];
    Telemetry.Warn("Saracon.AttemptFailed attempt={Attempt}/{MaxAttempts} input={Input} error={Error}",
        attempt + 1, MaxRetries + 1, Path.GetFileName(inputDff), error.Description);
    if (attempt < MaxRetries && IsTransientError(error.Description))
    {
        Telemetry.Info("Saracon.Retrying input={Input} reason={Reason} delay={Delay}s",
            Path.GetFileName(inputDff), error.Description, 2);
        ...
    }
    Telemetry.Error("Saracon.ConversionFailed input={Input} attempts={Attempts} finalError={Error}",
        Path.GetFileName(inputDff), attempt + 1, error.Description);
    return result.Errors;
}
```

**Why it's good:**

- Warn-level for retryable failures
- Info-level for retry actions
- Error-level for final failure
- Structured parameters with context (file name, attempt count)

### 8.2 Bad Example (Hypothetical)

```csharp
// BAD
try {
    await DoSomething();
}
catch {
    return false;
}
```

**Why it's bad:**

- Silent failure
- No logging at all
- No context about what failed

---

## 9. Appendix: Telemetry Methods

Available in `Core.Telemetry`:

```csharp
public static void Debug(string message, params object[] args)
public static void Info(string message, params object[] args)
public static void Warn(string message, params object[] args)
public static void Error(string message, params object[] args)
```

Usage:

```csharp
Telemetry.Error("Failed to process {File}: {Error}", filePath, ex.Message);
```

---

**End of Specification**
