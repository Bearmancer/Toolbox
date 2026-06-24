# Logging Discovery Research - .NET External Libraries

## Research Date: 2026-06-24
## Context: How to discover and control logging from external libraries (Google.Apis, Azure SDK)

---

## 1. THE THREE .NET LOGGING MECHANISMS

External libraries use three distinct mechanisms to emit logs. You need to know all three to discover what's available.

### Mechanism A: ILogger (Microsoft.Extensions.Logging)
- **What**: The standard .NET logging abstraction
- **How to discover**: Look for `ILogger<T>` injection, `ILoggerFactory` usage
- **Filtering**: Serilog `MinimumLevel.Override("Namespace", level)`
- **Used by**: ASP.NET Core, EF Core, Microsoft.Extensions.* libraries

### Mechanism B: EventSource (System.Diagnostics.Tracing)
- **What**: High-performance ETW/EventPipe logging, zero-allocation
- **How to discover**: Search source for `class.*EventSource`, `new EventSource(`
- **Filtering**: `EventListener` with `EnableEvents(source, level, keywords)`
- **Used by**: Azure SDK, System.Net.Http, System.Data.SqlClient
- **Key detail**: EventSource names are strings like `"Azure-Core"`, `"System.Net.Http"`

### Mechanism C: DiagnosticSource/DiagnosticListener
- **What**: In-process rich payload logging (non-serializable objects allowed)
- **How to discover**: Search source for `new DiagnosticListener(`, `DiagnosticSource`
- **Filtering**: `IObservable<T>` subscription with predicates
- **Used by**: HttpClient, ASP.NET Core, EF Core, SqlClient
- **Key detail**: Listener names like `"System.Net.Http"`, `"Microsoft.AspNetCore"`

---

## 2. DISCOVERY PROCEDURE FOR ANY .NET LIBRARY

### Step 1: Check Documentation
- Look for "Logging" section in library docs
- Search for "diagnostics", "tracing", "observability"
- Check README for logging configuration examples

### Step 2: Search Source Code (NuGet decompilation or GitHub)
```
# Find EventSource implementations
grep -r "EventSource" --include="*.cs"
grep -r "class.*EventSource" --include="*.cs"

# Find DiagnosticSource usage
grep -r "DiagnosticListener" --include="*.cs"
grep -r "DiagnosticSource" --include="*.cs"

# Find ILogger usage
grep -r "ILogger" --include="*.cs"
grep -r "ILoggerFactory" --include="*.cs"
```

### Step 3: List Active EventSources at Runtime
```csharp
// List all EventSources in the process
using var listener = new EventListener();
// EventListener.OnEventSourceCreated fires for each EventSource
// Check eventSource.Name to discover what's available
```

### Step 4: List Active DiagnosticListeners at Runtime
```csharp
DiagnosticListener.AllListeners.Subscribe(new Observer());
// Observer.OnNext fires for each DiagnosticListener
// Check listener.Name to discover what's available
```

### Step 5: Check for Known Namespaces
- `Microsoft.*` - ASP.NET Core, EF Core, Extensions
- `Azure.*` - Azure SDK libraries
- `System.Net.Http` - HttpClient
- `System.Data.SqlClient` - SQL Client
- `Google.Apis.*` - Google API client

---

## 3. SPECIFIC LIBRARY FINDINGS

### Google.Apis.YouTube.v3

**Logging Mechanism**: Minimal to none
- Google.Apis is an **auto-generated client** from API discovery documents
- Does NOT use ILogger, EventSource, or DiagnosticSource internally
- HTTP calls go through `HttpClient` which HAS DiagnosticSource instrumentation
- The library itself logs nothing - all logging comes from underlying HTTP infrastructure

**What you CAN observe**:
1. **HttpClient DiagnosticSource events** - Request/Response details
2. **Your own wrapper code** - Where you call the API

**What you CANNOT observe**:
- Internal Google API client logic (no logging hooks)
- Request signing details
- Token refresh internals (unless using Google credential libraries)

### Azure SDK (Azure.Core, Azure.AI.*)

**Logging Mechanism**: EventSource-based via `AzureEventSourceListener`
- All Azure SDK libraries use `AzureEventSource` base class
- EventSource names follow pattern: `"Azure-{PackageName}"` (e.g., `"Azure-Core"`, `"Azure-Identity"`)

**Known EventSource Names**:
| EventSource Name | Package | What it logs |
|-----------------|---------|--------------|
| `Azure-Core` | Azure.Core | HTTP requests/responses, retries, pipeline policies |
| `Azure-Identity` | Azure.Identity | Credential selection, token acquisition |
| `Azure.Security.KeyVault.*` | KeyVault packages | Key operations, local vs remote decisions |

**What Azure-Core logs automatically**:
- HTTP method, URI, headers (sanitized)
- Response status code, duration
- Retry attempts
- Pipeline policy execution

**How to enable Azure SDK logging**:
```csharp
// Option 1: Console listener (quick debug)
using var listener = AzureEventSourceListener.CreateConsoleLogger();

// Option 2: Forward to ILogger (production)
builder.Services.AddAzureClientsCore(true);

// Option 3: Custom filtering
using var listener = new AzureEventSourceListener((e, message) =>
{
    if (string.Equals(e.EventSource.Name, "Azure-Core", StringComparison.Ordinal))
        Console.WriteLine(message);
}, EventLevel.Verbose);
```

### System.Net.Http (HttpClient)

**Logging Mechanism**: BOTH DiagnosticSource AND EventSource

**DiagnosticSource Events** (in-process, rich payloads):
| Event Name | Payload | When |
|------------|---------|------|
| `System.Net.Http.HttpRequestOut.Start` | `HttpRequestMessage` | Request begins |
| `System.Net.Http.HttpRequestOut.Stop` | `HttpResponseMessage`, `TaskStatus` | Request completes |
| `System.Net.Http.Exception` | `Exception`, `HttpRequestMessage` | Request fails |

**EventSource Events** (ETW/EventPipe, serializable):
| EventSource Name | Events | What it logs |
|-----------------|--------|--------------|
| `System.Net.Http` | `RequestStart`, `RequestStop`, `RequestFailed` | HTTP operations |

**How to subscribe to HttpClient diagnostics**:
```csharp
DiagnosticListener.AllListeners.Subscribe(new Observer());

class Observer : IObserver<DiagnosticListener>
{
    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "System.Net.Http")
        {
            listener.Subscribe(new HttpObserver());
        }
    }
    // ... other methods
}

class HttpObserver : IObserver<KeyValuePair<string, object>>
{
    public void OnNext(KeyValuePair<string, object> value)
    {
        Console.WriteLine($"Event: {value.Key}");
        // Cast payload to see details
        if (value.Value is HttpRequestMessage request)
            Console.WriteLine($"URL: {request.RequestUri}");
    }
}
```

---

## 4. SERILOG FILTERING BY SOURCE CONTEXT

### How SourceContext Works
When you use `Log.ForContext<T>()`, Serilog adds a `SourceContext` property to the log event containing the type name. This enables per-namespace filtering.

### Configuration Methods

**appsettings.json**:
```json
"Serilog": {
    "MinimumLevel": {
        "Default": "Information",
        "Override": {
            "Microsoft": "Warning",
            "Microsoft.AspNetCore": "Warning",
            "Microsoft.EntityFrameworkCore": "Warning",
            "System.Net.Http": "Warning",
            "Azure-Core": "Information",
            "Azure-Identity": "Warning"
        }
    }
}
```

**Code-based**:
```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .MinimumLevel.Override("Azure-Core", LogEventLevel.Information)
    .CreateLogger();
```

### Filter by SourceContext in Sink Configuration
```csharp
.WriteTo.File("logs/azure.jsonl",
    restrictedToMinimumLevel: LogEventLevel.Information,
    outputTemplate: "{Timestamp:yyyy-MM-dd} [{Level}] {Message:lj} {Properties:j}{NewLine}")
```

### Advanced Filtering with Serilog.Filters
```csharp
.Filter.ByIncludingOnly(e =>
    e.Properties.ContainsKey("SourceContext") &&
    e.Properties["SourceContext"].ToString().StartsWith("Azure"))
```

---

## 5. LOG LEVEL DECISION TREE

### The Mental Model

```
┌─────────────────────────────────────────────────────────────────┐
│                    LOG LEVEL DECISION TREE                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Will someone need to ACT on this?                              │
│  ├─ YES → Is the app broken?                                   │
│  │        ├─ YES → FATAL (app is dying)                         │
│  │        └─ NO  → Is a specific operation broken?              │
│  │                 ├─ YES → ERROR (operation failed)            │
│  │                 └─ NO  → Is something unexpected?            │
│  │                          ├─ YES → WARNING (recoverable)      │
│  │                          └─ NO  → Continue...                │
│  └─ NO  → Is this a significant business event?                 │
│           ├─ YES → INFORMATION (system is working)              │
│           └─ NO  → Is this useful for debugging?                │
│                    ├─ YES → DEBUG (dev/staging only)            │
│                    └─ NO  → Don't log it                       │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Level Definitions with Examples

| Level | Production? | Volume | Example |
|-------|-------------|--------|---------|
| **FATAL** | Yes, alert | Very Low | `Database connection lost after 10 retries, shutting down` |
| **ERROR** | Yes, alert | Low | `Failed to process video upload: InvalidQuota` |
| **WARNING** | Yes | Low-Med | `YouTube API quota 80% consumed` |
| **INFORMATION** | Yes | Medium | `Successfully uploaded video {VideoId}` |
| **DEBUG** | No (dev/staging) | High | `API request to youtube/v3/videos returned 200 in 245ms` |
| **VERBOSE** | Never | Very High | `Deserializing response payload: {JsonPayload}` |

### The Golden Rules

1. **Production runs at INFORMATION or WARNING** - DEBUG is too noisy
2. **ERROR means someone should be notified** - if nobody needs to act, it's a WARNING
3. **INFORMATION should be readable by a human** - if wall-of-text, it's DEBUG
4. **Each log line should be actionable** - if you can't act on it, why log it?
5. **Same levels in dev and production** - if you can't follow production logs in dev, add better INFO logs (don't turn on DEBUG)

### What to Log at Each Level in YOUR Code

**INFORMATION** - Business milestones:
- Request received/completed
- Operation succeeded/failed (with context)
- Configuration loaded
- Service started/stopped
- External dependency called (not the details, just the fact)

**DEBUG** - Diagnostic breadcrumbs (dev only):
- Decision points: `Using strategy {Strategy} for {VideoId}`
- State transitions: `Cache miss for {Key}, fetching from API`
- Input/output summaries: `Request: {Part}, Fields: {Fields}`
- Performance: `Operation {Name} took {Elapsed}ms`

**WARNING** - Recoverable anomalies:
- Retry attempts: `Attempt {Attempt} failed, retrying in {Delay}ms`
- Degraded state: `Cache unavailable, using stale data`
- Threshold approaching: `Quota usage at {Percent}%`
- Fallback triggered: `Using backup credential`

**ERROR** - Failed operations:
- External API failures: `YouTube API returned {StatusCode}: {ErrorMessage}`
- Data corruption detected
- Required resource missing
- Authentication failures

**VERBOSE** - Full payloads (development only):
- Complete request/response bodies
- Internal state dumps
- Loop iterations
- Variable values

---

## 6. WHAT IS USELESS LOGGING (NOISE PATTERNS)

### Patterns to Avoid

1. **Logging without context**: `Error occurred` (what error? where?)
2. **Logging the obvious**: `Starting method X` (redundant with structured traces)
3. **Logging sensitive data**: Passwords, tokens, PII
4. **Logging at wrong level**: DEBUG in production, INFO for routine operations
5. **Logging without actionable info**: Wall of text nobody will read
6. **Logging control flow**: `If user is admin, do X` (that's code, not a log)
7. **Logging in loops**: Every iteration (log the summary, not each item)
8. **Logging without structure**: Free-text strings (use message templates)

### The "3 AM Test"
> Would you want to see this log at 3 AM while debugging a production issue?
> If no, it's noise. Delete it or lower the level.

### What External Libraries Log That IS Useful
- **Azure-Core**: HTTP request/response details (with duration)
- **Azure-Identity**: Which credential was selected (can't infer from result)
- **HttpClient**: Request/response lifecycle events
- **SQL Client**: Query execution details

### What External Libraries Log That IS Noise
- **Verbose HTTP headers**: Sanitized but still noise in most cases
- **Retry internals**: Usually handled by library, not actionable
- **Connection pooling**: Normal operation, not interesting

---

## 7. DECISION FRAMEWORK: WHAT TO LOG IN YOUR CODE

### The Three Questions

Before adding any log statement, ask:

1. **Is this already logged by a dependency?**
   - HTTP requests? → Azure-Core/HttpClient already logs these
   - Token refresh? → Azure-Identity already logs this
   - Don't duplicate what's already observable

2. **What's missing that I need?**
   - Business context not in HTTP logs
   - Decision logic invisible to infrastructure
   - Correlation IDs for distributed tracing
   - Performance metrics specific to my domain

3. **What level is appropriate?**
   - Would I want this at 3 AM? → INFO or WARNING
   - Would I only want this while debugging? → DEBUG
   - Would I only want this during development? → VERBOSE

### Example: YouTube Video Upload

**Don't log** (external library covers it):
- HTTP request details → Azure-Core logs this
- Response status code → Azure-Core logs this
- Token refresh → Azure-Identity logs this

**Do log** (your business context):
```
INFORMATION: Upload started for {VideoId}, Size: {SizeBytes} bytes
INFORMATION: Upload completed for {VideoId}, Processing time: {Elapsed}ms
WARNING: Upload retry {Attempt}/3 for {VideoId} after {Delay}ms
ERROR: Upload failed for {VideoId}: {ErrorMessage}
DEBUG: Using part {Part} strategy for {VideoId} (size: {SizeBytes})
```

---

## 8. PRACTICAL CONFIGURATION FOR YOUR APP

### Recommended Serilog Configuration

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // Suppress noisy external libraries
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    // Keep Azure SDK logs at Info (they're useful)
    .MinimumLevel.Override("Azure-Core", LogEventLevel.Information)
    // Suppress Azure-Identity verbose token logs
    .MinimumLevel.Override("Azure-Identity", LogEventLevel.Warning)
    // Your code at Debug level
    .MinimumLevel.Override("YourApp.Namespace", LogEventLevel.Debug)
    .WriteTo.Console()
    .WriteTo.File("logs/app.jsonl", rollingInterval: RollingInterval.Day)
    .CreateLogger();
```

### Enabling Azure SDK Logging (When Debugging)

```csharp
// Add to Program.cs temporarily when investigating Azure issues
if (args.Contains("--verbose-azure"))
{
    using var listener = AzureEventSourceListener.CreateConsoleLogger(EventLevel.Verbose);
}
```

---

## 9. KEY TAKEAWAYS

### For Your Specific Libraries

| Library | Mechanism | What's Logged | How to Control |
|---------|-----------|---------------|----------------|
| **Google.Apis.YouTube.v3** | None (uses HttpClient) | Nothing directly | N/A - see HttpClient |
| **Azure.AI.*** | EventSource (`Azure-*`) | HTTP requests, retries, pipeline | `MinimumLevel.Override("Azure-Core", ...)` |
| **HttpClient** | DiagnosticSource + EventSource | Request/Response lifecycle | Subscribe to `System.Net.Http` listener |
| **ASP.NET Core** | ILogger | Framework internals | `MinimumLevel.Override("Microsoft", Warning)` |

### The Discovery Process

1. **Check docs** for "Logging" or "Diagnostics" section
2. **Search source** for `EventSource`, `DiagnosticSource`, `ILogger`
3. **List at runtime** using `EventListener` or `DiagnosticListener.AllListeners`
4. **Test by enabling** verbose logging temporarily
5. **Filter with Serilog** `MinimumLevel.Override` to control noise

### The Mental Model

> Your code should log what external libraries CAN'T know.
> External libraries log what you CAN'T know.
> Together, they give you complete observability.
>
> Use INFORMATION for business milestones.
> Use DEBUG for diagnostic breadcrumbs (dev only).
> Use WARNING for recoverable anomalies.
> Use ERROR for failed operations.
> Never log sensitive data.
> Each log line should answer: "What do I do with this?"

---

## RESEARCH COMPLETE
