# SACD Death-Loop Repro Harness + Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a seconds-fast probe harness that reproduces the Saracon death loop through real service code, journal every run, then land two defense fixes (output-size check, stripper exception logging) — all evidence-gated via the journal.

**Architecture:** New standalone console project `tools/SacdProbe` (no test framework — project rule 4) that constructs `ProcessRunner`/`SaraconService`/`DffMetadataStripper` directly, runs a 6-case × 2-variant fixture matrix against real binaries from PATH, and appends one table row per run to `.superpowers/audit/sacd-probe-journal.md`. Phase 1 touches NO production file; fixes are Phase 2, gated by probe evidence.

**Tech Stack:** .NET 11, C#, ErrorOr (already in repo), no new NuGet packages.

## Global Constraints

- Root cause (confirmed): Windows "Beta: Use Unicode UTF-8 for worldwide language support" enabled → ACP=65001 → Saracon's wxWidgets 2.8.12 cannot map it → "Unknown encoding (-1)" + nondeterministic truncation. Spec §7 fix 0.
- Phase 1 changes NOTHING in `src/Services/Audio` — new files only.
- No new NuGet packages, no test frameworks (project rule 4), no ps1 scripts.
- Fixtures live in `C:\Temp\saracon-probe\` — never the repo.
- Build-verify after every edit (`dotnet build` — project rule 1).
- Commit after each task, 1–3 files, atomic (project rule 2).
- No root-cause claim without journal evidence (session lesson).
- Hardcode repo root as `private static readonly string` at top of file (project rule 8: inline paths at top of file). Repo root: `C:\Users\Lance\Dev\Toolbox`.
- Saracon/sox/sacd_extract resolve from PATH (AudioSetup.cs). Probe binary path strings: `"saracon"`.

---

### Task 1: SacdProbe project scaffold

**Files:**
- Create: `tools/SacdProbe/SacdProbe.csproj`
- Modify: `Toolbox.slnx` (add project line)

**Interfaces:**
- Produces: runnable console project `SacdProbe`; `dotnet build` compiles it via slnx.

- [ ] **Step 1: Write the csproj**

`tools/SacdProbe/SacdProbe.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<RootNamespace>SacdProbe</RootNamespace>
	</PropertyGroup>
	<ItemGroup>
		<ProjectReference Include="..\..\src\Services\Audio\Audio.csproj" />
		<ProjectReference Include="..\..\src\Core\Core.csproj" />
	</ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution**

Append to `Toolbox.slnx` inside `<Solution>`:
```xml
  <Project Path="tools\SacdProbe\SacdProbe.csproj" />
```

- [ ] **Step 3: Add minimal Program.cs so the project compiles**

`tools/SacdProbe/Program.cs`:
```csharp
Console.WriteLine("SacdProbe scaffold");
```

- [ ] **Step 4: Build verify**

Run: `dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx`
Expected: exit 0, SacdProbe compiled, no warnings-as-errors failures.

- [ ] **Step 5: Commit**

```bash
git add Toolbox.slnx tools/SacdProbe/SacdProbe.csproj tools/SacdProbe/Program.cs
git commit -m "chore(audio): scaffold SacdProbe repro harness project"
```

---

### Task 2: DffFixtureFactory — synthetic DSDIFF builder

**Files:**
- Create: `tools/SacdProbe/DffFixtureFactory.cs`

**Interfaces:**
- Produces:
  - `enum FixtureCase { Baseline, Id3Valid, Id3CorruptSize, ComtNonAscii, BracketedName, Id3CorruptPlusBracketed }`
  - `static class DffFixtureFactory`
  - `static string Build(FixtureCase c, string outDir)` — returns full path to written `.dff`; directory `C:\Temp\saracon-probe` created if missing.
  - `static string ExpectedOutputBytes(FixtureCase c)` — baseline math for the size check in Task 5: `sampleRate(2822400/8) * channels(2) * seconds(0.5) * (bitDepth 24/8 * 2)` → DSD64 0.5s stereo = 352800 bytes → PCM 24-bit 88.2k 0.5s stereo = 88200 × 3 × 2 × 0.5 = 264600 bytes.

**Structure (chunks in order, all sizes big-endian, pad byte appended when size is odd):**

- `FRM8` header: id `FRM8` + size (computed, 8 bytes BE) + form type `DSD `
- `FVER`: id `FVER` + size=4 + version `0x00000105` (4 bytes BE)
- `PROP`: id `PROP` + size + `SND ` + subchunks:
  - `FS  `: id + size=4 + sample rate 2822400 (4 BE)
  - `CHNL`: id + size + channel count 2 (2 BE) + speaker ids `SLFT` + `SRGT` (4 bytes each)
  - `CMPR`: id + size + `DSD ` (4 bytes) + reserved 4 zero bytes
  - `LSCO`: id + size=2 + `0x0006` (2 BE)
- `DSD `: id + size + 352800 bytes of `0x69` (0.5s DSD64 stereo)
- Optional trailing chunks per case:
  - `Id3Valid`: `ID3 ` + size + 32 bytes valid ID3v2.3 (header `ID3\x03\x00\x00` + syncsafe size 0 + frame-less)
  - `Id3CorruptSize`: same but the 4 size bytes after `ID3 ` are sync-safe-mangled: each byte's high bit cleared (`byte & 0x7F`) — mimics sacd-ripper #94 corruption so a walker reads a size off-by-large or off-by-one (use value 0x20 for the size so `& 0x7F` still yields 0x20 — corrupt variant uses value 0xA0 → becomes 0x20, i.e. writes `0xA0` where valid writes `0x20`).
  - `ComtNonAscii`: `COMT` + size + timestamp 4 zero bytes + count 2 BE + text bytes in UTF-8 with a non-ASCII char (e.g. `0xC3 0xA9` = é).
  - `BracketedName`: same chunks as baseline, but file name is `Disc 10 [SACD] (1)-test.dff` (brackets, spaces, parens, hyphen).
  - `Id3CorruptPlusBracketed`: corrupt ID3 + bracketed name combined.

- [ ] **Step 1: Write DffFixtureFactory.cs**

```csharp
using System.Text;

namespace SacdProbe;

public enum FixtureCase
{
    Baseline,
    Id3Valid,
    Id3CorruptSize,
    ComtNonAscii,
    BracketedName,
    Id3CorruptPlusBracketed,
}

public static class DffFixtureFactory
{
    private const int SampleRate = 2822400;
    private const short Channels = 2;
    private const double Seconds = 0.5;
    private const int DsdBytes = 352800; // DSD64 stereo 0.5s: 2822400/8 * 2 * 0.5

    private static readonly string WorkDir = @"C:\Temp\saracon-probe";

    public static string Build(FixtureCase c)
    {
        Directory.CreateDirectory(WorkDir);
        var name = c switch
        {
            FixtureCase.BracketedName or FixtureCase.Id3CorruptPlusBracketed
                => "Disc 10 [SACD] (1)-test.dff",
            _ => $"{c}.dff",
        };
        var path = Path.Combine(WorkDir, name);
        using var fs = File.Create(path);

        void WriteChunk(ReadOnlySpan<byte> id, ReadOnlySpan<byte> data)
        {
            fs.Write(id);
            Span<byte> size = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)data.Length);
            fs.Write(size);
            fs.Write(data);
            if (data.Length % 2 != 0) fs.WriteByte(0);
        }

        var prop = new MemoryStream();
        {
            Span<byte> fsRate = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(fsRate, SampleRate);
            var fsChunk = fsRate.ToArray();
            WriteChunkTo(prop, "FS  "u8, fsChunk);

            Span<byte> chnl = stackalloc byte[2 + 8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(chnl[..2], Channels);
            "SLFT"u8.CopyTo(chnl[2..6]);
            "SRGT"u8.CopyTo(chnl[6..10]);
            WriteChunkTo(prop, "CHNL"u8, chnl.ToArray());

            var cmpr = new byte[8];
            "DSD "u8.CopyTo(cmpr.AsSpan(0, 4));
            WriteChunkTo(prop, "CMPR"u8, cmpr);

            Span<byte> lsco = stackalloc byte[2];
            System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(lsco, 6);
            WriteChunkTo(prop, "LSCO"u8, lsco.ToArray());
        }
        var propData = new byte[4 + (int)prop.Length];
        "SND "u8.CopyTo(propData.AsSpan(0, 4));
        prop.ToArray().CopyTo(propData, 4);
        WriteChunk(fs, "PROP"u8, propData);

        WriteChunk(fs, "DSD "u8, Enumerable.Repeat((byte)0x69, DsdBytes).ToArray());

        switch (c)
        {
            case FixtureCase.Id3Valid:
                WriteChunk(fs, "ID3 "u8, new byte[32]); // zeros = valid-size ID3
                break;
            case FixtureCase.Id3CorruptSize:
            case FixtureCase.Id3CorruptPlusBracketed:
                WriteChunk(fs, "ID3 "u8, [0xA0, 0x00, 0x00, 0x00]); // sync-safe-mangled size: 0xA0 read as 0x20
                break;
            case FixtureCase.ComtNonAscii:
                var text = Encoding.UTF8.GetBytes("ripped by é test");
                var comt = new byte[4 + 2 + text.Length];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(comt[..4], 0);
                System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(comt.AsSpan(4, 2), (short)text.Length);
                text.CopyTo(comt, 6);
                WriteChunk(fs, "COMT"u8, comt);
                break;
            case FixtureCase.Baseline:
            case FixtureCase.BracketedName:
            default:
                break;
        }

        // FRM8 header last, size = everything after the 12-byte header
        var total = fs.Length - 12;
        fs.Position = 4;
        Span<byte> frmSize = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(frmSize, (ulong)total);
        fs.Write(frmSize);
        fs.Close();

        return path;
    }

    private static void WriteChunkTo(MemoryStream target, ReadOnlySpan<byte> id, ReadOnlySpan<byte> data)
    {
        target.Write(id);
        Span<byte> size = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(size, (ulong)data.Length);
        target.Write(size);
        target.Write(data);
        if (data.Length % 2 != 0) target.WriteByte(0);
    }

    public static long ExpectedPcmBytes() => (long)(Seconds * SampleRate / 8.0 * Channels * 3 * 2); // 24-bit stereo 88.2k
}
```

- [ ] **Step 2: Self-check — walk the generated DFF with the real stripper**

Add to `Program.cs` temporarily (replaced in Task 3):
```csharp
foreach (var c in Enum.GetValues<FixtureCase>())
{
    var p = DffFixtureFactory.Build(c);
    var hasId3 = DffMetadataStripper.HasId3Chunk(p);
    Console.WriteLine($"{c,-28} {Path.GetFileName(p),-32} HasId3Chunk={hasId3}");
}
```

Run: `dotnet run --project C:\Users\Lance\Dev\Toolbox\tools\SacdProbe`
Expected: `Id3Valid=True`, `Id3CorruptSize=True`, `Id3CorruptPlusBracketed=True`, `Baseline=False`, `BracketedName=False`, `ComtNonAscii=False`. If a corrupt-size fixture reports `False`, that IS the reproduced A2 bug — note it in the journal (expected FAIL-expected).

- [ ] **Step 3: Commit**

```bash
git add tools/SacdProbe/DffFixtureFactory.cs tools/SacdProbe/Program.cs
git commit -m "feat(audio): SacdProbe DFF fixture factory (6-case matrix)"
```

---

### Task 3: ProbeRunner + Program — matrix, journal, verdicts

**Files:**
- Create: `tools/SacdProbe/ProbeRunner.cs`
- Modify: `tools/SacdProbe/Program.cs` (replace self-check body)

**Interfaces:**
- Consumes: `DffFixtureFactory.Build(FixtureCase)`, `SaraconService(ProcessRunner, "saracon")`, `DffMetadataStripper.StripId3TagsAsync`.
- Produces: exit code 0 iff all runs are PASS or FAIL-expected; nonzero on any FAIL-unexpected. Appends rows to `.superpowers/audit/sacd-probe-journal.md`.

- [ ] **Step 1: Write ProbeRunner.cs**

```csharp
using System.Diagnostics;
using Services.Audio;

namespace SacdProbe;

public static class ProbeRunner
{
    private static readonly string RepoRoot = @"C:\Users\Lance\Dev\Toolbox";
    private static readonly string JournalPath = Path.Combine(RepoRoot, ".superpowers", "audit", "sacd-probe-journal.md");
    private static readonly string OutRoot = @"C:\Temp\saracon-probe\out";

    public static int RunAll()
    {
        Directory.CreateDirectory(OutRoot);
        var exit = 0;

        foreach (var c in Enum.GetValues<FixtureCase>())
        {
            var fixture = DffFixtureFactory.Build(c);
            foreach (var stripped in new[] { false, true })
            {
                var row = RunCase(c, fixture, stripped);
                AppendJournal(row);
                Console.WriteLine(row);
                if (row.Contains("FAIL-unexpected")) exit = 1;
            }
        }

        Console.WriteLine(exit == 0 ? "PROBE PASS" : "PROBE FAIL (unexpected outcome)");
        return exit;
    }

    private static string RunCase(FixtureCase c, string fixture, bool stripped)
    {
        var sw = Stopwatch.StartNew();
        var input = fixture;
        try
        {
            if (stripped)
            {
                var strip = DffMetadataStripper.StripId3TagsAsync(fixture, OutRoot).GetAwaiter().GetResult();
                if (strip.IsError)
                    return Row(c, stripped, -1, 0, 0, "FAIL-expected", $"strip error: {strip.Errors[0].Description}");
                input = strip.Value;
            }

            var runner = new ProcessRunner();
            var saracon = new SaraconService(runner, "saracon");
            var result = saracon.ConvertDsdToPcmAsync(input, OutRoot, 88200, 24, 0.0).GetAwaiter().GetResult();
            sw.Stop();

            if (result.IsError)
                return Row(c, stripped, -1, sw.ElapsedMilliseconds, 0, "FAIL-expected", result.Errors[0].Description);

            var outFile = result.Value;
            var bytes = new FileInfo(outFile).Length;
            var expected = DffFixtureFactory.ExpectedPcmBytes();
            var verdict = bytes >= expected * 0.5 ? "PASS" : "FAIL-expected";
            return Row(c, stripped, 0, sw.ElapsedMilliseconds, bytes, verdict,
                $"{Path.GetFileName(outFile)} {bytes} bytes (expected {expected})");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Row(c, stripped, -2, sw.ElapsedMilliseconds, 0, "FAIL-unexpected", ex.Message);
        }
    }

    private static string Row(FixtureCase c, bool stripped, int exit, long ms, long bytes, string verdict, string note) =>
        $"| {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {c} | {(stripped ? "stripped" : "raw")} | {exit} | {ms}ms | {bytes} | {verdict} | {Escape(note)} |";

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", " ");

    private static void AppendJournal(string row)
    {
        var header = "| timestamp | case | variant | exit | elapsed | out-bytes | verdict | snippet |";
        var sep = "|---|---|---|---|---|---|---|---|";
        var content = File.Exists(JournalPath) ? File.ReadAllText(JournalPath) : header + "\n" + sep + "\n";
        if (content.Contains("## Runs") && !content.Contains(row))
        {
            var idx = content.IndexOf("## Runs", StringComparison.Ordinal);
            var after = content.IndexOf("\n", idx, StringComparison.Ordinal);
            content = content.Insert(after + 1, row + "\n");
        }
        else if (!content.Contains(row))
        {
            content += "\n" + header + "\n" + sep + "\n" + row + "\n";
        }
        File.WriteAllText(JournalPath, content);
    }
}
```

- [ ] **Step 2: Replace Program.cs body**

```csharp
namespace SacdProbe;

public static class Program
{
    public static int Main()
    {
        Console.WriteLine("=== SACD PROBE ===");
        Console.WriteLine($"ACP check: registry HKLM\\SYSTEM\\CurrentControlSet\\Control\\Nls\\CodePage ACP={GetAcp()} (65001 = UTF-8 beta = death loop expected)");
        return ProbeRunner.RunAll();
    }

    private static int GetAcp() =>
        Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Nls\CodePage", "ACP", "0") as string is { } s
            ? int.Parse(s)
            : 0;
}
```

- [ ] **Step 3: Build verify**

Run: `dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx`
Expected: exit 0.

- [ ] **Step 4: Commit**

```bash
git add tools/SacdProbe/ProbeRunner.cs tools/SacdProbe/Program.cs
git commit -m "feat(audio): SacdProbe runner — 6-case matrix, journal append, exit gate"
```

---

### Task 4: Probe run #1 — capture the death loop (UTF-8 still ON)

**Files:** none (execution + journal only)

- [ ] **Step 1: Run the probe**

Run: `dotnet run --project C:\Users\Lance\Dev\Toolbox\tools\SacdProbe`
Expected: ACP=65001 printed; ≥1 row FAIL-expected (charset error via `IsCharsetError` retry exhaustion, or truncated output < 50% expected, or no output). Control Baseline may also fail — that itself is the reproduction.

- [ ] **Step 2: Verify journal rows landed**

Read `.superpowers/audit/sacd-probe-journal.md` — expect 12 rows under `## Runs`.

- [ ] **Step 3: Append analysis to journal `## Findings`**

One line: `2026-08-08 — Probe run #1 (ACP=65001, UTF-8 beta ON): <N> FAIL-expected, <M> PASS. Death loop reproduced: <cases>.`

- [ ] **Step 4: Commit**

```bash
git add .superpowers/audit/sacd-probe-journal.md
git commit -m "docs(audio): probe run #1 — death loop captured (UTF-8 beta ON)"
```

- [ ] **Step 5: USER CHECKPOINT — disable UTF-8 beta + reboot**

User: Settings → Time & Language → Language → Administrative Language Settings → Change system locale → uncheck "Beta: Use Unicode UTF-8 for worldwide language support" → reboot.
Verify after reboot: ACP must equal 1252. Do NOT proceed to Task 5 until verified.

---

### Task 5: Probe run #2 — post-locale-fix verification

**Files:** none (execution + journal only)

- [ ] **Step 1: Confirm ACP**

Run: `reg query HKLM\SYSTEM\CurrentControlSet\Control\Nls\CodePage /v ACP`
Expected: `ACP REG_SZ 1252` (or other non-65001 value).

- [ ] **Step 2: Re-run probe**

Run: `dotnet run --project C:\Users\Lance\Dev\Toolbox\tools\SacdProbe`
Expected: all rows PASS (control converts fully; ID3/COMT cases still warn-and-continue per DSDIFF spec), probe exit 0.

- [ ] **Step 3: Journal the flip**

Append to `## Findings`: `Probe run #2 (ACP=1252): all cases PASS. Charset/truncation failure eliminated by locale change — root-cause fix confirmed.`

- [ ] **Step 4: Commit**

```bash
git add .superpowers/audit/sacd-probe-journal.md
git commit -m "docs(audio): probe run #2 — all PASS after UTF-8 beta disabled (root cause confirmed)"
```

---

### Task 6: Fix A1 — output-size sanity check in SaraconService

**Files:**
- Modify: `src/Services/Audio/SaraconService.cs` (inside `RunConversionWithRetryAsync`, after `FindSaraconOutput` — around line 131)

**Interfaces:**
- Consumes: nothing new. Produces: `ConversionFailed` error when output is <50% of expected PCM bytes. Expected PCM computed from input DFF: DSD64 duration via DSD chunk bytes / (2822400/8 × channels), × sampleRate × channels × 3.

- [ ] **Step 1: Read current code**

Read `src/Services/Audio/SaraconService.cs` lines 120–145 to confirm the exact block before editing.

- [ ] **Step 2: Add size validation**

After the `if (expectedOutput is null)` guard, add:

```csharp
var outputSize = new FileInfo(expectedOutput).Length;
var expectedPcmBytes = EstimateExpectedPcmBytes(inputDff, sampleRate, bitDepth);
if (outputSize < expectedPcmBytes / 2)
{
    Telemetry.Warn("Saracon.OutputTooSmall output={Output} size={Size}MB expected~{Expected}MB",
        Path.GetFileName(expectedOutput), outputSize / 1_048_576.0, expectedPcmBytes / 1_048_576.0);
    return Errors.Audio.ConversionFailed(inputDff,
        $"saracon output {Path.GetFileName(expectedOutput)} is {outputSize} bytes — expected ~{expectedPcmBytes} (truncated conversion)");
}
```

Add helper at bottom of class:

```csharp
private static long EstimateExpectedPcmBytes(string dffPath, int sampleRate, int bitDepth)
{
    try
    {
        using var stream = File.OpenRead(dffPath);
        var magic = new byte[4];
        stream.ReadExactly(magic, 0, 4);
        if (System.Text.Encoding.ASCII.GetString(magic) != "FRM8")
            return 0;

        stream.Seek(12, SeekOrigin.Begin);
        long dsdBytes = 0;
        var channels = 2;
        while (stream.Position < stream.Length - 12)
        {
            var idBuf = new byte[4];
            stream.ReadExactly(idBuf, 0, 4);
            var id = System.Text.Encoding.ASCII.GetString(idBuf);
            var sizeBuf = new byte[8];
            stream.ReadExactly(sizeBuf, 0, 8);
            var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(sizeBuf);

            if (id == "DSD ")
                dsdBytes = (long)size;

            var skip = (long)size;
            if (skip % 2 != 0) skip++;
            stream.Seek(skip, SeekOrigin.Current);
        }

        if (dsdBytes == 0) return 0;
        var durationSeconds = dsdBytes / (2822400.0 / 8.0 * channels);
        return (long)(durationSeconds * sampleRate * channels * (bitDepth / 8.0));
    }
    catch
    {
        return 0;
    }
}
```

- [ ] **Step 3: Build verify**

Run: `dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx`
Expected: exit 0.

- [ ] **Step 4: Probe re-run (regression)**

Run: `dotnet run --project C:\Users\Lance\Dev\Toolbox\tools\SacdProbe`
Expected: still all PASS (size check is a safety net, not behavior change on healthy runs).

- [ ] **Step 5: Commit**

```bash
git add src/Services/Audio/SaraconService.cs
git commit -m "fix(audio): fail on truncated saracon output (size sanity check)"
```

---

### Task 7: Fix A2 — HasId3Chunk exception logging

**Files:**
- Modify: `src/Services/Audio/DffMetadataStripper.cs` (catch block, lines 51–54)

- [ ] **Step 1: Read the catch block**

Read `src/Services/Audio/DffMetadataStripper.cs` lines 17–58.

- [ ] **Step 2: Log the exception instead of silent false**

Replace:
```csharp
catch (Exception ex)
{
    Telemetry.Warn("DffMetadataStripper.HasId3Chunk failed for {File}: {Error}", dffPath, ex.Message);
    return false;
}
```
with:
```csharp
catch (Exception ex)
{
    Telemetry.Error("DffMetadataStripper.HasId3Chunk failed for {File}: {Error}", dffPath, ex.Message);
    throw;
}
```
Rationale: a chunk-walk failure is a corrupt/miscoded file — the caller (`SaraconService.RunConversionWithRetryAsync`) must know the strip never ran, not silently convert an ID3-contaminated DFF. Also bound the walk by remaining bytes: change the loop guard `while (stream.Position < stream.Length - 12)` to also break when `skip <= 0` (zero-size chunk = malformed tail):

```csharp
var skip = (long)chunkSize;
if (skip <= 0) break; // malformed: zero-size chunk mid-walk
if (skip % 2 != 0) skip++;
if (stream.Position + skip > stream.Length) break; // miscoded size: bound by EOF
stream.Seek(skip, SeekOrigin.Current);
```

- [ ] **Step 3: Build verify**

Run: `dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx`
Expected: exit 0. (Note: `Telemetry.Error` — verify it exists in `Core/Telemetry.cs`; if not, use `Telemetry.Warn` with the existing signature and keep `throw;`.)

- [ ] **Step 4: Probe re-run (regression)**

Run: `dotnet run --project C:\Users\Lance\Dev\Toolbox\tools\SacdProbe`
Expected: all PASS; `Id3CorruptSize` fixture now either throws (logged, FAIL-expected by design) or walks past the corrupt ID3 to the end safely.

- [ ] **Step 5: Commit**

```bash
git add src/Services/Audio/DffMetadataStripper.cs
git commit -m "fix(audio): HasId3Chunk logs walk failures and bounds by EOF instead of silent false"
```

---

### Task 8: Real Disc 10 final gate + noise prune + journal close

**Files:** none new (deletions + journal)

- [ ] **Step 1: Real pipeline run**

Run: `dotnet run --project C:\Users\Lance\Dev\Toolbox\src\App\ -- audio sacd-convert 'C:\Users\Lance\Downloads\Herbert von Karajan - Live in Berlin 1970-1979 - Berliner Philharmoniker, Herbert von Karajan (2026) [SACD]\' --debug`
Expected: pipeline completes, Disc 10 output WAV size ≈ 2.13+ GB (full), no charset error, no "file in use" lock.

- [ ] **Step 2: Prune noise (user-mandated, spec §9)**

Delete from repo root: `dff-inspect.csx`, `inspect-dff.ps1`, `debug-sacd-disc10.ps1`, `test-saracon-simple.ps1`, `extract-and-test.ps1`, `test-saracon-now.ps1`, `diagnose-saracon-complete.ps1`, `strip-dff-metadata.ps1`, `test-saracon-gui-popup.ps1`, `SACD-Saracon-Analysis-Summary.md`, `docs/SACD-SARACON-ISSUE.md`.
Delete temp: `C:\Temp\saracon-probe`, `C:\Temp\saracon_test`, `C:\Temp\saracon_diagnostics`, `C:\Temp\saracon_popup_test`, `C:\Temp\check-file-size.ps1`, `C:\Temp\run-strip.ps1`.
Keep: probe harness, journal, spec, `SACD errors.md`.

- [ ] **Step 3: Close journal**

Append to `## Findings`: `Final gate: real Disc 10 run <PASS/FAIL> — <size> bytes, <errors>. Death loop closed. Noise pruned.`

- [ ] **Step 4: Final build + commit**

Run: `dotnet build C:\Users\Lance\Dev\Toolbox\Toolbox.slnx` — expected exit 0.
```bash
git add -A tools/SacdProbe .superpowers/audit/sacd-probe-journal.md docs/superpowers/specs/2026-08-08-sacd-death-loop-repro-design.md src/Services/Audio
git commit -m "feat(audio): SACD death-loop resolved — repro harness, size check, stripper hardening; noise pruned"
```
