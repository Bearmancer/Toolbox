# Phase 5: Telemetry.StartActivity — Kill `dynamic`

## Task 13: Replace `dynamic` return with `IDisposable` in Telemetry

In `src/Core/Telemetry.cs`, replace the `StartActivity` method (line 67-68):

**Replace:**
```csharp
    public static dynamic StartActivity(string messageTemplate, params object[] args) =>
        Log.Logger.StartActivity(messageTemplate, args);
```

**With:**
```csharp
    public static IDisposable StartActivity(string messageTemplate, params object[] args) =>
        Log.Logger.StartActivity(messageTemplate, args);
```

**Must NOT:**
- Change any other method in Telemetry.cs
- Add any new imports
- Change the `SerilogTracing` using directive — it stays because `StartActivity` extension method comes from it

**QA:**
```bash
dotnet build
```
Expected: Clean build. All callers use `using var activity = Telemetry.StartActivity(...)` — `IDisposable` satisfies `using`. The `.Complete()` call on the return value requires a cast or the actual type. Check if any caller calls `.Complete()` on the result.

**If `.Complete()` callers exist:** Add a wrapper struct. Replace with:

```csharp
    public readonly struct ActivityScope : IDisposable
    {
        private readonly SerilogTracing.LogActivity _inner;
        public ActivityScope(SerilogTracing.LogActivity inner) => _inner = inner;
        public void Complete() => _inner.Complete();
        public void Dispose() => _inner.Dispose();
    }

    public static ActivityScope StartActivity(string messageTemplate, params object[] args) =>
        new(Log.Logger.StartActivity(messageTemplate, args));
```

Then verify all callers still compile. `.Complete()` and `using` both work on `ActivityScope`.

**QA after wrapper (if needed):**
```bash
dotnet build
```
Expected: Clean build. Zero `dynamic` in codebase.

**Commit:** `refactor(core): replace dynamic return type in Telemetry.StartActivity`
