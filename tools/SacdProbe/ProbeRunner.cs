using System.Diagnostics;
using Services.Audio;

namespace SacdProbe;

/// <summary>
/// Runs the SACD probe matrix against the real Disc 10 Karajan DFF.
///
/// Implements v2 spec §6 (precondition canary), §7 (signature-matched verdicts),
/// §8 (confidence-tagged journal entries).
///
/// Matrix: real DFF × {raw, stripped} × {headless (ProcessRunner default), visible-window}
/// = 4 runs total. The visible-window arm exposes wx locale init → expected to trigger
/// "Unknown encoding (-1)" when ACP=65001.
/// </summary>
internal static class ProbeRunner
{
    private static readonly string RepoRoot    = @"C:\Users\Lance\Dev\Toolbox-sacd-repro";
    private static readonly string JournalPath = Path.Combine(RepoRoot, ".superpowers", "audit", "sacd-probe-journal.md");
    private static readonly string OutRoot     = @"C:\Temp\saracon-probe\out";
    private static readonly string SaraconBin  = "saracon";

    // ─── Failure signatures (v2 §7) ───────────────────────────────────────────

    private enum FailureSignature
    {
        None,
        RegistryOleInit,    // "Can't open registry key" / "Cannot initialize OLE" / "wxIdleWakeUpModule"
        CharsetEncoding,    // "Unknown encoding" / "Cannot convert from the charset"
        Truncation,         // exit 0, output bytes < 50% expected
        ZeroBytes,          // exit 0, no output file
        Other,
    }

    private static FailureSignature Classify(string text) => text switch
    {
        var s when s.Contains("Cannot initialize OLE",   StringComparison.OrdinalIgnoreCase)
                || s.Contains("Can't open registry key", StringComparison.OrdinalIgnoreCase)
                || s.Contains("wxIdleWakeUpModule",       StringComparison.OrdinalIgnoreCase)
            => FailureSignature.RegistryOleInit,

        var s when s.Contains("Unknown encoding",                     StringComparison.OrdinalIgnoreCase)
                || s.Contains("Cannot convert from the charset",      StringComparison.OrdinalIgnoreCase)
            => FailureSignature.CharsetEncoding,

        _ => FailureSignature.Other,
    };

    // ─── Matrix ───────────────────────────────────────────────────────────────

    private record ProbeVariant(
        string Label,
        bool   Stripped,
        bool   Headless,           // true = ProcessRunner (CreateNoWindow=true, redirected I/O)
                                   // false = visible window (exposes wx locale init)
        FailureSignature DeclaredExpected  // what we expect at ACP=65001
    );

    private static readonly ProbeVariant[] Matrix =
    [
        // Headless variants: wx locale init suppressed → should PASS regardless of ACP
        new("raw/headless",       false, true,  FailureSignature.None),
        new("stripped/headless",  true,  true,  FailureSignature.None),

        // Visible-window variants: wx locale init fires → expect CharsetEncoding at ACP=65001
        new("raw/visible",        false, false, FailureSignature.CharsetEncoding),
        new("stripped/visible",   true,  false, FailureSignature.CharsetEncoding),
    ];

    // ─── Entry point ──────────────────────────────────────────────────────────

    public static int RunAll()
    {
        Directory.CreateDirectory(OutRoot);

        if (!RealDffFixture.Exists())
        {
            Console.Error.WriteLine($"PRECONDITION FAILED: real DFF not found at {RealDffFixture.Path}");
            return 3;
        }

        var expectedBytes = RealDffFixture.ExpectedPcmBytes();
        Console.WriteLine($"Real DFF  : {RealDffFixture.Path}");
        Console.WriteLine($"Expected  : {expectedBytes:N0} PCM bytes");

        // §6 — Precondition canary: raw/headless first; abort on RegistryOleInit
        Console.WriteLine("\n--- Precondition canary (raw/headless) ---");
        var canary = RunVariant(Matrix[0], expectedBytes);
        AppendJournal(canary.Row);
        Console.WriteLine(canary.Row);

        if (canary.Signature == FailureSignature.RegistryOleInit)
        {
            Console.Error.WriteLine(
                "\nPRECONDITION FAILED: registry/OLE init error — v2 spec §4.\n" +
                "Fix A: grant HKCU\\Software\\Weiss Engineering FullControl to the executing SID.\n" +
                "Fix B: confirm agent session vs. interactive session mismatch via:\n" +
                "       [Security.Principal.WindowsIdentity]::GetCurrent().Name; query session; (Get-Process -Id $PID).SessionId");
            return 2; // environment failure — distinct from hypothesis failure
        }

        // Full matrix
        Console.WriteLine("\n--- Full matrix ---");
        var unexpectedFail = false;
        foreach (var variant in Matrix)
        {
            var run = RunVariant(variant, expectedBytes);
            AppendJournal(run.Row);
            Console.WriteLine(run.Row);
            if (run.Row.Contains("FAIL-unexpected")) unexpectedFail = true;
        }

        Console.WriteLine(unexpectedFail ? "\nPROBE FAIL (unexpected outcome)" : "\nPROBE PASS");
        return unexpectedFail ? 1 : 0;
    }

    // ─── Single variant ───────────────────────────────────────────────────────

    private record RunResult(string Row, FailureSignature Signature);

    private static RunResult RunVariant(ProbeVariant v, long expectedBytes)
    {
        var sw = Stopwatch.StartNew();
        var input = RealDffFixture.Path;

        try
        {
            if (v.Stripped)
            {
                var strip = DffMetadataStripper.StripId3TagsAsync(input, OutRoot).GetAwaiter().GetResult();
                if (strip.IsError)
                {
                    sw.Stop();
                    var errText = strip.Errors[0].Description;
                    var sig     = Classify(errText);
                    return MakeResult(v, sw, -1, 0, sig, errText);
                }
                input = strip.Value;
            }

            if (v.Headless)
                return RunHeadless(v, input, sw, expectedBytes);
            else
                return RunVisible(v, input, sw, expectedBytes);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new RunResult(
                $"| {Ts()} | {v.Label} | exit=-2 | {sw.ElapsedMilliseconds}ms | 0B | FAIL-unexpected | {Escape(ex.Message)} |",
                FailureSignature.Other);
        }
    }

    // Headless: use SaraconService/ProcessRunner (CreateNoWindow=true, I/O redirected)
    private static RunResult RunHeadless(ProbeVariant v, string input, Stopwatch sw, long expectedBytes)
    {
        var runner  = new ProcessRunner();
        var saracon = new SaraconService(runner, SaraconBin);
        var result  = saracon.ConvertDsdToPcmAsync(input, OutRoot, 88200, 24, 0.0).GetAwaiter().GetResult();
        sw.Stop();

        if (result.IsError)
        {
            var errText = result.Errors[0].Description;
            return MakeResult(v, sw, -1, 0, Classify(errText), errText);
        }

        var outFile = result.Value;
        var bytes   = File.Exists(outFile) ? new FileInfo(outFile).Length : 0L;
        var sig     = OutSig(bytes, expectedBytes);
        return MakeResult(v, sw, 0, bytes, sig, $"{Path.GetFileName(outFile)} {bytes:N0}/{expectedBytes:N0} bytes");
    }

    // Visible-window: direct Process.Start with UseShellExecute=false but CreateNoWindow=false
    // This exposes wx locale init and should trigger "Unknown encoding (-1)" at ACP=65001.
    private static RunResult RunVisible(ProbeVariant v, string input, Stopwatch sw, long expectedBytes)
    {
        var saraconPath = FindSaracon();
        if (saraconPath is null)
        {
            sw.Stop();
            return new RunResult(
                $"| {Ts()} | {v.Label} | exit=-1 | {sw.ElapsedMilliseconds}ms | 0B | FAIL-unexpected | saracon not found on PATH |",
                FailureSignature.Other);
        }

        var outName = Path.Combine(OutRoot, Path.GetFileNameWithoutExtension(input) + "-visible");
        var psi = new ProcessStartInfo
        {
            FileName               = saraconPath,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = false,   // ← exposes wx locale init
            WorkingDirectory       = OutRoot,
        };
        // Same args as SaraconService.ConvertDsdToPcmAsync
        foreach (var arg in new[] { "-c", "d2p", "-r", "88200", "-f", "wav", "-n", "24bit",
                                    "-d", "tpdf", "-g", "0.00", "-T", "-V", "all",
                                    "-t", OutRoot, input })
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start saracon (visible)");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        Task.WhenAll(stdoutTask, stderrTask).GetAwaiter().GetResult();
        proc.WaitForExit();
        sw.Stop();

        var combined = stdoutTask.Result + stderrTask.Result;
        var sig      = Classify(combined);

        if (proc.ExitCode != 0 || sig != FailureSignature.None)
            return MakeResult(v, sw, proc.ExitCode, 0, sig, combined[..Math.Min(200, combined.Length)]);

        // Find output file: saracon appends "-d2p" postfix
        var outFile = Directory.EnumerateFiles(OutRoot, "*.wav")
            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
            .FirstOrDefault();
        var bytes = outFile is not null && File.Exists(outFile) ? new FileInfo(outFile).Length : 0L;
        return MakeResult(v, sw, proc.ExitCode, bytes, OutSig(bytes, expectedBytes),
            $"{(outFile is null ? "no output" : Path.GetFileName(outFile))} {bytes:N0}/{expectedBytes:N0} bytes");
    }

    private static string? FindSaracon()
    {
        if (ProcessRunner.IsOnPath("saracon")) return "saracon";
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, "saracon.exe"))
            .FirstOrDefault(File.Exists);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static FailureSignature OutSig(long bytes, long expected) =>
        bytes == 0         ? FailureSignature.ZeroBytes :
        bytes < expected * 0.5 ? FailureSignature.Truncation :
        FailureSignature.None;

    private static RunResult MakeResult(ProbeVariant v, Stopwatch sw, int exit, long bytes,
        FailureSignature actual, string note)
    {
        string verdict;
        if (actual == FailureSignature.None)
            verdict = "PASS";
        else if (v.DeclaredExpected != FailureSignature.None && actual == v.DeclaredExpected)
            verdict = $"FAIL-expected({actual})";
        else
            verdict = $"FAIL-unexpected({actual})";

        var row = $"| {Ts()} | {v.Label} | exit={exit} | {sw.ElapsedMilliseconds}ms | {bytes:N0}B | {verdict} | {Escape(note)} |";
        return new RunResult(row, actual);
    }

    private static string Ts() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");

    // ─── Journal ──────────────────────────────────────────────────────────────

    private static void AppendJournal(string row)
    {
        var content = File.Exists(JournalPath) ? File.ReadAllText(JournalPath) : "";
        if (content.Contains(row)) return;

        var header = "| timestamp | variant | exit | elapsed | out-bytes | verdict | note |\n" +
                     "|---|---|---|---|---|---|---|\n";
        var runsIdx = content.IndexOf("## Runs (v2", StringComparison.Ordinal);
        if (runsIdx >= 0)
        {
            var afterSep = content.IndexOf('\n',
                content.IndexOf("|---|", runsIdx, StringComparison.Ordinal));
            content = content.Insert(afterSep + 1, row + "\n");
        }
        else
        {
            content += $"\n## Runs (v2 — real DFF, {DateTime.Now:yyyy-MM-dd})\n\n{header}{row}\n";
        }
        File.WriteAllText(JournalPath, content);
    }
}