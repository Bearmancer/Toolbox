# ProcessRunner Streaming & Timeout Plan

> **Plan ID:** `plan:process-runner-streaming`
> **For agentic workers:** Use `/athena-build` to execute this plan wave-by-wave.

**Goal:** Add live output streaming and inactivity timeout to `ProcessRunner` and consume it in `SacdProbe`.
**Approach:** Update `ProcessRunner.RunAsync` to capture standard output/error asynchronously, invoking an `onOutputLine` callback for each line and resetting a 60-second inactivity `CancellationTokenSource`. Update `SaraconService` and `ProbeRunner` to utilize this callback to stream output to the console and a log file.
**Tech Stack:** C#, .NET 8

**Files:**

- Create: `tools/SacdProbe/ProcessRunnerTests.cs` — ad-hoc test runner (since no test project exists)
- Modify: `src/Services/Audio/ProcessRunner.cs` — add `inactivityTimeout` and `onOutputLine` parameters, implement streaming logic
- Modify: `src/Services/Audio/SaraconService.cs` — bubble up `onOutputLine` to `ProcessRunner`
- Modify: `tools/SacdProbe/ProbeRunner.cs` — supply callback to write to console and `saracon-run.log`
- Modify: `tools/SacdProbe/Program.cs` — temporarily hook test runner

---

## Wave 1 (Sequential due to dependencies)

### Task 1: Update ProcessRunner

**Files:** Create `tools/SacdProbe/ProcessRunnerTests.cs`, Modify `tools/SacdProbe/Program.cs`, Modify `src/Services/Audio/ProcessRunner.cs`

- [ ] **Step 1: Write the failing test**
      Create `tools/SacdProbe/ProcessRunnerTests.cs` to test the new signature. Use `ping` (on Windows) or `cmd /c` to simulate a process that outputs lines and one that hangs.

```csharp
using Services.Audio;
namespace SacdProbe;
public static class ProcessRunnerTests
{
    public static async Task Run()
    {
        var runner = new ProcessRunner();
        var lines = new List<string>();
        // Test 1: Streaming
        await runner.RunAsync("cmd.cs", ["/c", "echo testline"], CancellationToken.None, onOutputLine: line => lines.Add(line));
        if (lines.Count == 0 || !lines[0].Contains("testline")) throw new Exception("FAIL: Streaming did not work");
        
        // Test 2: Timeout
        var start = DateTime.UtcNow;
        var result = await runner.RunAsync("powershell.exe", ["-c", "Start-Sleep -Seconds 10"], CancellationToken.None, inactivityTimeout: TimeSpan.FromSeconds(2));
        if (!result.IsError || !result.FirstError.Description.Contains("Timed out")) throw new Exception("FAIL: Timeout did not trigger");
        if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5)) throw new Exception("FAIL: Timeout took too long");
        
        Console.WriteLine("PASS: ProcessRunner tests");
    }
}
```

Update `tools/SacdProbe/Program.cs` to call `await ProcessRunnerTests.Run(); return 0;` temporarily at the top of `Main`. (Since `Main` is sync, use `.GetAwaiter().GetResult()`).

- [ ] **Step 2: Run test to verify it fails**
      Run: `dotnet run --project tools/SacdProbe`
      Expected: Compiler error because `inactivityTimeout` and `onOutputLine` don't exist on `RunAsync`.

- [ ] **Step 3: Write minimal implementation**
      Modify `src/Services/Audio/ProcessRunner.cs`. Update the `RunAsync` signature to include `TimeSpan? inactivityTimeout = null, Action<string>? onOutputLine = null`.
      Modify the output reading logic to use `process.OutputDataReceived` and `process.ErrorDataReceived` (or `ReadLineAsync` in a loop) and append to a `StringBuilder` while invoking `onOutputLine`. Use a `CancellationTokenSource` linked to the timeout that gets reset on every line received.

- [ ] **Step 4: Run test to verify it passes**
      Run: `dotnet run --project tools/SacdProbe`
      Expected: Output showing "PASS: ProcessRunner tests".

- [ ] **Step 5: Commit**

```bash
git add tools/SacdProbe/ProcessRunnerTests.cs tools/SacdProbe/Program.cs src/Services/Audio/ProcessRunner.cs
git commit -m "feat: add streaming and inactivity timeout to ProcessRunner [plan:process-runner-streaming] [wave:1/task:1]"
```

---

## Wave 2 (after Wave 1 passes)

### Task 2: Update SaraconService

**Depends on:** Task 1
**Files:** Modify `src/Services/Audio/SaraconService.cs`, Modify `tools/SacdProbe/ProcessRunnerTests.cs`

- [ ] **Step 1: Write the failing test**
      Modify `ProcessRunnerTests.cs` to verify `SaraconService` accepts the `onOutputLine` parameter (even if we don't fully run saracon, just verifying the compiler signature).

```csharp
// add inside ProcessRunnerTests.Run():
var service = new SaraconService(new ProcessRunner(), "nonexistent");
Action<string> dummyCb = s => {};
// this will fail to compile if ConvertDsdToPcmAsync doesn't accept the param
_ = service.ConvertDsdToPcmAsync("in", "out", 44100, 16, 0.0, dummyCb);
```

- [ ] **Step 2: Run test to verify it fails**
      Run: `dotnet build tools/SacdProbe`
      Expected: Compiler error missing parameter.

- [ ] **Step 3: Write minimal implementation**
      Modify `src/Services/Audio/SaraconService.cs`. Add `Action<string>? onOutputLine = null` to `ConvertDsdToPcmAsync`. Pass this parameter down into `_runner.RunAsync` along with the inactivity timeout if desired (or rely on the caller to handle timeout).

- [ ] **Step 4: Run test to verify it passes**
      Run: `dotnet build tools/SacdProbe`
      Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add tools/SacdProbe/ProcessRunnerTests.cs src/Services/Audio/SaraconService.cs
git commit -m "feat: bubble up onOutputLine in SaraconService [plan:process-runner-streaming] [wave:2/task:2]"
```

---

## Wave 3 (after Wave 2 passes)

### Task 3: Hook up SacdProbe Output

**Depends on:** Task 2
**Files:** Modify `tools/SacdProbe/ProbeRunner.cs`, Modify `tools/SacdProbe/Program.cs`

- [ ] **Step 1: Write the failing test**
      Restore `tools/SacdProbe/Program.cs` back to its original state (remove `ProcessRunnerTests.Run()`). The test for this step is that `ProbeRunner.cs` compiles with the new callback parameter and successfully generates a log file.
      Delete `tools/SacdProbe/ProcessRunnerTests.cs`.

- [ ] **Step 2: Run test to verify it fails (or rather, prepare)**
      Run: `dotnet run --project tools/SacdProbe` (it might run normally or fail depending on if DFF fixture exists). We just need to make sure we are back to normal operations.

- [ ] **Step 3: Write minimal implementation**
      Modify `tools/SacdProbe/ProbeRunner.cs`. Inside `RunHeadless`:

```csharp
string logPath = Path.Combine(OutputRoot, "saracon-run.log");
Action<string> onLine = line => {
    Console.WriteLine(line);
    File.AppendAllText(logPath, line + Environment.NewLine);
};

var result = new SaraconService(new ProcessRunner(), "saracon")
    .ConvertDsdToPcmAsync(input, OutputRoot, 88200, 24, 0.0, onLine)
    .GetAwaiter().GetResult();
```

(Also pass `inactivityTimeout: TimeSpan.FromSeconds(60)` down the chain if you added it to `SaraconService`, or just rely on the existing params).

- [ ] **Step 4: Run test to verify it passes**
      Run: `dotnet build tools/SacdProbe`
      Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add tools/SacdProbe/Program.cs tools/SacdProbe/ProbeRunner.cs tools/SacdProbe/ProcessRunnerTests.cs
git commit -m "feat: stream saracon output to console and log file [plan:process-runner-streaming] [wave:3/task:3]"
```

---

## Verification

After all waves complete:

- [ ] Run full test suite: `dotnet build`
- [ ] Execute the `SacdProbe` tool on a real file to verify console streaming visually: `dotnet run --project tools/SacdProbe`
