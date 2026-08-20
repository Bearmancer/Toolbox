# Pristine Services Refactor TODO List

## 1. Missing `.WaitAsync(ct)` and Incomplete Try/Catch

- **File**: `src/Services/Pristine/PristinePollService.cs`
- **Line**: 24
- **Code snippet**: `IPage page = await ctx.NewPageAsync();`
- **Missing Elements**: `.WaitAsync(ct)` is missing, and it is completely missing a `try/catch` block.
- **Rationale for Failure**: Playwright methods like `NewPageAsync()` do not accept a `CancellationToken` natively. If the underlying browser pipe hangs or stalls, this method will block the C# task infinitely.
- **Why the New Proposal Works**: By appending `.WaitAsync(ct)`, if the token is cancelled (e.g., due to a timeout), the `.WaitAsync(ct)` wrapper will forcefully throw an `OperationCanceledException`, immediately unblocking the C# execution thread. Adding a `try/catch` block with `OperationCanceledException` explicitly caught will correctly log the cancellation and propagate it up, preventing the application from hanging forever.

## 2. Missing `.WaitAsync(ct)` and Swallowed Cancellations

- **File**: `src/Services/Pristine/PristinePollService.cs`
- **Line**: 524
- **Code snippet**: `await Task.WhenAll(pendingDownloads);`
- **Missing Elements**: `.WaitAsync(ct)` is missing. The surrounding `catch (Exception ex)` (lines 527-530) swallows `OperationCanceledException` without explicitly throwing or logging a cancellation event.
- **Rationale for Failure**: If any of the pending downloads stall and their internal tokens fail to cancel the wait, `Task.WhenAll` can hang indefinitely.
- **Why the New Proposal Works**: Appending `.WaitAsync(ct)` enforces a timeout on the `WhenAll` operation itself. When combined with an explicit `catch (OperationCanceledException) { throw; }`, it guarantees that if the timeout is exceeded, the loop aborts and the failure is cleanly propagated rather than incorrectly logged as a general `Exception`.

## 3. Incomplete Error Handling (Missing Try/Catch entirely)

- **File**: `src/Services/Pristine/PristineOrchestrator.cs`
- **Line**: 100
- **Code snippet**: `Microsoft.Playwright.IPage seed = await ctx.NewPageAsync().WaitAsync(ct);`
- **Missing Elements**: `.WaitAsync(ct)` is present, but it lacks any localized `try/catch` block.
- **Rationale for Failure**: Although the token prevents an infinite hang by throwing `OperationCanceledException`, the absence of a localized `try/catch` means no Telemetry logs are generated. This makes diagnosing *where* and *why* the process was cancelled or failed very difficult.
- **Why the New Proposal Works**: Wrapping this in a `try/catch` with dedicated blocks for both `OperationCanceledException` and general `Exception` ensures the context is logged appropriately before bubbling the exception to the orchestrator layer.

## 4. Swallowed Cancellations (Missing `OperationCanceledException` Catch)

These occurrences have `.WaitAsync(ct)` and a generic `catch (Exception ex)` block, but they fail to explicitly catch `OperationCanceledException`. When cancellation occurs, they incorrectly log a general error and *continue* execution instead of aborting.

- **File**: `src/Services/Pristine/PristineLoginService.cs`
   - **Line**: 18 (`await ctx.NewPageAsync().WaitAsync(ct);`) - Has a `try/catch` with `OperationCanceledException` re-throw (`PristineLoginService.cs:18-28`).
   - **Line**: 39 (`await page.Locator("text=Browsing as guest").IsVisibleAsync().WaitAsync(ct);`) - Catch does NOT rethrow `OperationCanceledException` (partially valid concern).

- **File**: `src/Services/Pristine/PristineBrowser.cs`
  - **Line**: 126 (`await ctx.AddCookiesAsync([ck]).WaitAsync(ct);`) - Loop swallows cancellation and continues attempting to add more cookies.
  - **Line**: 182 (`await ctx.AddInitScriptAsync(...).WaitAsync(ct);`) - Loop swallows cancellation and continues evaluating initialization scripts.

- **Rationale for Failure**: A generic `catch (Exception ex)` catches `OperationCanceledException`. By not re-throwing it, the application treats cancellation as a minor localized warning and blindly continues to the next statement, undermining the purpose of the cancellation token.
- **Why the New Proposal Works**: By adding an explicit `catch (OperationCanceledException) { throw; }` before the general `Exception` catch block, the cancellation signal correctly interrupts the flow and cascades upward.
