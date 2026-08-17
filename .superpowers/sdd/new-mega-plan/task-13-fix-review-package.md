# Review package: 703609a..e9f1590

## Commits
e9f1590 docs(audio): correct P2.1 bounds evidence

## Files changed
 .superpowers/sdd/new-mega-plan/task-13-report.md | 20 ++++++++++----------
 1 file changed, 10 insertions(+), 10 deletions(-)

## Diff
diff --git a/.superpowers/sdd/new-mega-plan/task-13-report.md b/.superpowers/sdd/new-mega-plan/task-13-report.md
index 8e979da..68d89f1 100644
--- a/.superpowers/sdd/new-mega-plan/task-13-report.md
+++ b/.superpowers/sdd/new-mega-plan/task-13-report.md
@@ -1,16 +1,16 @@
 # P2.1 Target Report: ProbeDsdAsync Hardening
 
 **Branch:** `sacd-completion-v2`
-**Source commit:** `76b6d1e` (fix(audio): harden DSDIFF probing) + working-tree diff (P2.1 FORM-bound fixes)
-**Diff base:** `9a5ac16..HEAD` ΓÇö committed +61 / -35 lines; working-tree adds 15 lines
-**Working-tree status:** 4 files modified (task-13-report.md, progress.md, new-mega-plan.md, DsdConvertService.cs). Source changes are uncommitted FORM-bound fixes.
+**Source commits:** `76b6d1e` (probe hardening) and `703609a` (FORM-bound fixes)
+**Diff base:** `9a5ac16..703609a` ΓÇö source changes committed; report commits follow separately.
+**Working-tree status:** target source clean after `703609a`; plan/ledger/checks remain unrelated working-tree artifacts.
 
 ---
 
 ## Subtask 1: ReadBytes + ASCII replacement
 
 **Goal:** Replace every `ReadChars` with `ReadExactBytes` + `Encoding.ASCII.GetString`.
 
 **Source evidence (DsdConvertService.cs):**
 
 ```csharp
@@ -70,25 +70,25 @@ All size values flow through `checked((long)...)` before arithmetic. No narrowin
 
 ---
 
 ## Subtask 3: Bounded seeks (corrupt size cannot pass EOF)
 
 **Goal:** Bound seeks so corrupt size cannot pass EOF.
 
 **Source evidence:**
 
 ```csharp
-// Line 61-62 ΓÇö FORM bounds check
+// Lines 57-62 ΓÇö FORM bounds check
 if (formEnd > stream.Length)
     throw new InvalidDataException("DSDIFF FORM exceeds stream bounds");
 
-// Lines 171-176 ΓÇö SeekChecked enforces [0, stream.Length] bounds
+// Lines 163-176 ΓÇö SeekChecked enforces [0, stream.Length] bounds
 private static void SeekChecked(FileStream stream, long target)
 {
     if (target < 0 || target > stream.Length)
         throw new InvalidDataException("DSDIFF seek exceeds stream bounds");
     stream.Seek(target, SeekOrigin.Begin);
 }
 ```
 
 Every `SeekChecked` call (lines 116, 122, 128, 134) passes through the bounds guard. Chunk/padding/subchunk seeks all validated. **PASS**
 
@@ -173,37 +173,37 @@ All 7 projects compiled clean. **PASS**
 | PROP walk break | PASS | Line 137-138: `break` after FS+CHNL found, unchanged logic; outer loop bound to `formEnd` (L70) |
 | Parser reuse rationale | PASS | Separate concerns: probe (read-only metadata) vs stripper (copy/repair). No merge. |
 | Fix round 1: FORM bounds | PASS | 7 findings applied: outer loop bound to formEnd, formSize<4 reject, chunk/padding EOF bounds, subchunk PROP bounds, ReadExactBytes short-read guard |
 | Build | PASS | 0 warnings, 0 errors |
 | Runtime acceptance | BLOCKED | No DFF tooling/media in worktree. Owner: P3.4/P4 |
 
 ---
 
 ## Fix Round 1: FORM-bound hardening (review findings applied)
 
-**Source commit:** `76b6d1e` (fix(audio): harden DSDIFF probing) + working-tree diff (P2.1 FORM-bound fixes)
-**Diff:** `9a5ac16..HEAD` + working-tree ΓÇö committed +61 / -35 lines; working-tree adds 15 lines in `DsdConvertService.cs`
-**Working-tree status:** Source changes uncommitted (FORM-bound hardening). Report committed separately.
+**Source commits:** `76b6d1e` and `703609a` (FORM-bound hardening)
+**Diff:** `9a5ac16..703609a` ΓÇö source changes committed; report committed separately.
+**Working-tree status:** Source clean after `703609a`.
 
 ### Findings addressed
 
 | # | Review finding | Fix applied | Source line(s) |
 |---|----------------|-------------|----------------|
 | 1 | Outer `while` loop used `stream.Length - 12` as bound; leftover bytes after last chunk could skip bounds check | Loop condition changed to `stream.Position < formEnd`; inner guard `if (formEnd - stream.Position < 12)` catches truncated headers | L70, L72-73 |
 | 2 | `formSize < 4` not checked; degenerate FORM with zero/minimal body passed through | Added `if (formSize < 4) throw InvalidDataException("DSDIFF FORM is too small")` after `ReadUInt64BigEndian` | L58-59 |
 | 3 | Chunk seek used `(int)subSize` narrowing cast; corrupt oversized `chunkSize` (>2^31) could wrap negative | All size reads promoted to `ulong` via `BinaryPrimitives.ReadUInt64BigEndian`; seeks use `checked((long)...)` with `chunkEnd`/`subEnd` variables | L57, L60-61, L76-77, L96-97 |
 | 4 | Chunk/padding seeks not bounded to FORM or stream | `chunkEnd` validated against `formEnd` and `stream.Length` (L78); padding `paddedChunkEnd` also checked against both (L132) | L78, L132-133 |
 | 5 | PROP subchunk seeks not bounded to PROP chunk | `subEnd` validated against `propEnd` (L98); subchunk padding `paddedSubEnd` checked against `propEnd` (L120-121) | L98, L120-121 |
 | 6 | `ReadChars` encoding not guaranteed on binary data | All `ReadChars` replaced with `ReadExactBytes` + `Encoding.ASCII.GetString` (see Subtask 1) | L53, L63, L75, L87, L95 |
 | 7 | `ReadBytes` short-read not detected | `ReadExactBytes` helper added; throws `EndOfStreamException` if `bytes.Length != count` | L163-169 |
 
-### Exact current source lines (commit 76b6d1e)
+### Exact current source lines (commit 703609a)
 
 ```
 L57-62: FORM size + bounds
   var formSize = BinaryPrimitives.ReadUInt64BigEndian(ReadExactBytes(reader, 8));
   if (formSize < 4)
       throw new InvalidDataException("DSDIFF FORM is too small");
   var formEnd = checked(12 + checked((long)formSize));
   if (formEnd > stream.Length)
       throw new InvalidDataException("DSDIFF FORM exceeds stream bounds");
 
@@ -280,11 +280,11 @@ All 7 projects compiled clean. **PASS**
 ### Runtime acceptance
 
 **Status: BLOCKED**
 
 No real DFF tooling or Disc 3 ISO available in the worktree environment. No synthetic runtime tests exist in this project (rule: no test NuGet packages; standalone `.cs` with `Main()` only). Runtime probe behavior for Disc 3 and corrupt oversized files cannot be verified.
 
 **Owner:** P3.4 (runtime harness) or P4 (integration with real Disc 3 media).
 
 ---
 
-**Commit:** Report only. No source edits.
+**Commit:** Report only after source commit `703609a`; no source edits in this report fix.
