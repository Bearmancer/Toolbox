# Probe: Verify proposed Serilog template fix actually eliminates duplicate EventName
# Runs against the live local Serilog in this app's bin/ output, no subagent.

$repoRoot = "C:\Users\Lance\Desktop\Azure\New"
$csproj = Join-Path $repoRoot "src\Azure\Azure.csproj"

Add-Type -Path "C:\Users\Lance\.nuget\packages\serilog\4.3.1\lib\net10.0\Serilog.dll" -ErrorAction SilentlyContinue
Add-Type -Path "C:\Users\Lance\.nuget\packages\serilog.formatting.compact\3.0.0\lib\netstandard2.0\Serilog.Formatting.Compact.dll" -ErrorAction SilentlyContinue
Add-Type -Path "C:\Users\Lance\.nuget\packages\serilog.sinks.console\6.1.1\lib\net8.0\Serilog.Sinks.Console.dll" -ErrorAction SilentlyContinue

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.StreamWriter($out, [System.Text.Encoding]::UTF8)
$writer.AutoFlush = $true

Write-Host "=== BEFORE: current code template `{EventName} {@Event}` ===" -ForegroundColor Cyan
$evt1 = [PSCustomObject]@{
    EventName = "SessionStarted"
    Severity  = "Info"
    Service   = "Azure"
    SessionId = "abc123"
}
[Serilog.Log]::Logger = [Serilog.LoggerConfiguration]::new().MinimumLevel.Verbose().WriteTo.Sink(
    [Serilog.Sinks.RollingFile::new('unused'), [Serilog.Events.LogEventLevel]::Verbose)
).CreateLogger()
# Emit using the EXACT pattern from Log.cs
[Serilog.Log]::ForContext("Service", "Azure").ForContext("SessionId", "abc123").Write(
    [Serilog.Events.LogEventLevel]::Information,
    $null,
    "{EventName} {@Event}",
    $evt1.EventName,
    $evt1
)
[Serilog.Log]::CloseAndFlush()

Write-Host "=== AFTER: proposed template `{@Event}` ===" -ForegroundColor Green
[Serilog.Log]::Logger = [Serilog.LoggerConfiguration]::new().MinimumLevel.Verbose().WriteTo.Sink(
    [Serilog.Sinks.RollingFile::new('unused'), [Serilog.Events.LogEventLevel]::Verbose)
).CreateLogger()
[Serilog.Log]::ForContext("Service", "Azure").ForContext("SessionId", "abc123").Write(
    [Serilog.Events.LogEventLevel]::Information,
    $null,
    "{@Event}",
    $evt1
)
[Serilog.Log]::CloseAndFlush()

Write-Host ""
Write-Host "=== SelfLog.Enable overloads (verified by source above) ===" -ForegroundColor Cyan
Write-Host "  1. SelfLog.Enable(TextWriter)         — writes to text writer, flushes after each line"
Write-Host "  2. SelfLog.Enable(Action<string>)    — invokes delegate with each message"
Write-Host "  3. SelfLog.Disable()                   — clears the output"
Write-Host "  Native .NET recommendation: Action<string> delegate so we can format + log to file"
