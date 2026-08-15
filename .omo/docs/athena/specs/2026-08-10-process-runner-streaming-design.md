# ProcessRunner Streaming & Inactivity Timeout Design

## 1. Architecture
- **`ProcessRunner`**: Will use event-based reading (`OutputDataReceived` / `ErrorDataReceived`) or asynchronous stream reading line-by-line instead of `ReadToEndAsync()`. As each line is received, it will:
  1. Append to a `StringBuilder` (to preserve the final `ProcessResult`).
  2. Invoke an optional `Action<string>` callback to allow callers to stream the data live.
  3. Reset a 60-second inactivity cancellation token.
- **`SaraconService`**: Will pass a callback through its `ConvertDsdToPcmAsync` method down to `ProcessRunner`.
- **`SacdProbe` (`ProbeRunner`)**: Will supply a callback that does two things:
  1. Prints the line to the screen (`Console.WriteLine`).
  2. Appends the line to a dedicated `saracon-run.log` file in the output directory.

## 2. Interface Contracts

**`ProcessRunner.cs`**
```csharp
public async Task<ErrorOr<ProcessResult>> RunAsync(
    string binaryPath,
    string[] args,
    CancellationToken ct,
    string? workingDir = null,
    TimeSpan? timeout = null, // Existing total timeout
    TimeSpan? inactivityTimeout = null, // NEW: Timeout if no output is received
    Action<string>? onOutputLine = null // NEW: Live streaming callback
)
```

**`SaraconService.cs`**
```csharp
public async Task<ErrorOr<string>> ConvertDsdToPcmAsync(
    string inputFilePath,
    string outputDirectory,
    int sampleRate,
    int bitDepth,
    double gain,
    Action<string>? onOutputLine = null // NEW
)
```

## 3. Error Handling
- **Inactivity Timeout**: If the `inactivityTimeout` (60s) elapses without the callback firing, `ProcessRunner` will log a warning via `Telemetry`, kill the process tree, and return a timeout `Error`.
- **Callback Safety**: If the caller's callback (`onOutputLine`) throws an exception (e.g., file lock on the log file), `ProcessRunner` will swallow and log it to `Telemetry` so it doesn't crash the underlying process execution.

## 4. Testing Approach
- **Verification**: Run `SacdProbe` via `dotnet run`.
- **Expectation 1**: Verbose text from `saracon` streams live to the terminal.
- **Expectation 2**: A complete log file is generated in the output directory.
- **Expectation 3**: If `saracon` is simulated to hang, it terminates after 60 seconds of silence.

## 5. Migration & Rollback
- **Migration**: Refactor `RunAsync` calls across the solution. Since the new parameters are optional, existing code outside of `SacdProbe` will not break.
- **Rollback**: Simply revert the commits in `ProcessRunner` and `ProbeRunner`.
