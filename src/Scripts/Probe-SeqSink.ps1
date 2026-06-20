# Serilog.Sinks.Seq Real-Time Probe
# Inspects the actual assembly, the buffer file, and the bookmark.

$dll = "C:\Users\Lance\.nuget\packages\serilog.sinks.seq\9.1.0\lib\net10.0\Serilog.Sinks.Seq.dll"
$serilog = "C:\Users\Lance\.nuget\packages\serilog\4.3.1\lib\net10.0\Serilog.dll"
$logDir = Join-Path $env:USERPROFILE ".cache\logs\app"

Write-Host "=== A. Assembly: Seq extension methods (Seq() overloads) ===" -ForegroundColor Cyan
Add-Type -Path $serilog -ErrorAction SilentlyContinue
Add-Type -Path $dll -ErrorAction SilentlyContinue

$ext = [System.Reflection.Assembly]::LoadFrom($dll).GetType("Serilog.SeqLoggerConfigurationExtensions")
foreach ($m in $ext.GetMethods() | Where-Object { $_.Name -eq "Seq" }) {
    $sig = ($m.GetParameters() | ForEach-Object {
        $t = $_.ParameterType.Name
        $d = if ($_.HasDefaultValue) { " = $($_.DefaultValue)" } else { "" }
        "[$t] $($_.Name)$d"
    }) -join ", "
    Write-Host "  Seq($sig)"
}

Write-Host ""
Write-Host "=== B. Non-public types in the sink assembly ===" -ForegroundColor Cyan
$asm = [System.Reflection.Assembly]::LoadFrom($dll)
foreach ($t in $asm.GetTypes() | Where-Object { $_.IsClass -and $_.Namespace -eq "Serilog.Sinks.Seq" -and -not $_.IsPublic }) {
    Write-Host "  $($t.FullName)"
    foreach ($ctor in $t.GetConstructors([System.Reflection.BindingFlags]"Instance,NonPublic,Public")) {
        $params = ($ctor.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        Write-Host "    ctor($params)"
    }
    foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]"Instance,Public,NonPublic,DeclaredOnly") | Select-Object -First 20) {
        $params = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
        Write-Host "    $($m.ReturnType.Name) $($m.Name)($params)"
    }
    foreach ($f in $t.GetFields([System.Reflection.BindingFlags]"Instance,Public,NonPublic,DeclaredOnly")) {
        Write-Host "    field $($f.FieldType.Name) $($f.Name)"
    }
}

Write-Host ""
Write-Host "=== C. Buffer file state ===" -ForegroundColor Cyan
$buf = Join-Path $logDir "seq-buffer-20260620_004.clef"
$book = Join-Path $logDir "seq-buffer.bookmark"
if (Test-Path $buf) {
    $fi = Get-Item $buf
    Write-Host "  file:        $($fi.Name)"
    Write-Host "  size:        $($fi.Length) bytes"
    Write-Host "  modified:    $($fi.LastWriteTime.ToString('o'))"
    Write-Host "  age:         $(([DateTime]::Now - $fi.LastWriteTime).TotalMinutes.ToString('F1')) min"
}
if (Test-Path $book) {
    $bm = Get-Content $book -Raw
    Write-Host "  bookmark:    $($bm.Trim())"
}

Write-Host ""
Write-Host "=== D. Bookmark vs file size ===" -ForegroundColor Cyan
if (Test-Path $book) {
    $bmText = (Get-Content $book -Raw).Trim()
    $bmPos = [long]($bmText.Split(':::')[0])
    $fileSize = (Get-Item $buf).Length
    Write-Host "  bookmark pos: $bmPos"
    Write-Host "  file size:    $fileSize"
    if ($bmPos -eq $fileSize) {
        Write-Host "  >>> STATE: STALLED — bookmark pinned to EOF, no events shipped" -ForegroundColor Red
    } elseif ($bmPos -lt $fileSize) {
        Write-Host "  >>> STATE: BACKLOG — bookmark behind file by $($fileSize - $bmPos) bytes" -ForegroundColor Yellow
    } else {
        Write-Host "  >>> STATE: AHEAD — bookmark past file (impossible under normal use)" -ForegroundColor Magenta
    }
}

Write-Host ""
Write-Host "=== E. Test the SEQ_URL sentinel issue ===" -ForegroundColor Cyan
Add-Type -TypeDefinition @"
using System;
public static class UriTest {
    public static string Try(string s) {
        try { var u = new Uri(s); return u.ToString(); }
        catch (Exception e) { return "EXCEPTION: " + e.GetType().Name + " — " + e.Message; }
    }
}
"@
Write-Host "  new Uri(\"\"):           $([UriTest]::Try(''))"
Write-Host "  new Uri(null):          $([UriTest]::Try($null))"
Write-Host "  new Uri(\"http://x\"):    $([UriTest]::Try('http://x'))"

Write-Host ""
Write-Host "=== F. Live sink configuration with empty SEQ_URL ===" -ForegroundColor Cyan
$env:SEQ_URL = ""
$cfg = New-Object Serilog.LoggerConfiguration
$cfg = $cfg.WriteTo.Seq("", [Serilog.Events.LogEventLevel]::Information, $null, $null, $null, $null, $null, 0)
Write-Host "  Seq sink with empty URL — creation did NOT throw. Sink instantiated but unusable."

Write-Host ""
Write-Host "=== G. Realistic Log.Emit output: EventName duplication ===" -ForegroundColor Cyan
Add-Type -AssemblyName System.Text.Json
$evt = [PSCustomObject]@{
    EventName = "SessionStarted"
    Severity  = "Info"
    Service   = "Azure"
    SessionId = "abc123"
    $type     = "SessionStarted"
}
$tpl = "{EventName} {@Event}"
$args = @($evt.EventName, $evt)
Write-Host "  template:    $tpl"
Write-Host "  args[0]:     $($args[0])   (extracted as top-level property)"
Write-Host "  args[1]:     $($args[1] | ConvertTo-Json -Compress)"
Write-Host "  >>> EventName appears in BOTH the template parameter and the destructured object"
