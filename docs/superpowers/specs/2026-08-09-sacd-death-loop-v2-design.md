# SACD Saracon Death-Loop: Root-Cause Verification & Fix — Design v2

**Date:** 2026-08-09
**Status:** Draft — amends and partially supersedes `docs/superpowers/specs/2026-08-08-sacd-death-loop-repro-design.md`
**Branch:** builds on `sacd-deathloop-repro` (Tasks 1–3, 6, 7 commits kept as-is; Task 4/5 *results* are invalidated — see §1)
**Trigger:** process audit of the 2026-08-08 session found the root-cause claim was asserted before the evidence that was supposed to establish it. This spec is the fix, not a restart.

---

## §1. Why this is a revision, not a continuation

| # | v1 defect | Evidence | v2 fix |
|---|---|---|---|
| 1 | Root cause marked "confirmed, HIGH confidence" (v1 spec §7 fix 0; commit `5914e9d`) before any probe had run | Same plan's Global Constraints separately state "No root-cause claim without journal evidence" — self-contradicting within one document | §3: status downgraded to CANDIDATE until §4's precondition clears and a run actually reaches the charset code path |
| 2 | `ProbeRunner.RunCase` verdict logic: any `SaraconService` error → `FAIL-expected`, regardless of cause | Task 4: 12/12 `FAIL-expected` from an unrelated registry error; console still printed `PROBE PASS` | §7: verdicts are signature-matched; an unmatched failure is `FAIL-unexpected` and aborts the run instead of passing quietly |
| 3 | Task 4 (UTF-8 ON) and Task 5 (UTF-8 OFF, ACP verified =1252) produced the byte-identical error both times | Same `HKCU\...\Saracon (error 5: access is denied)` in both runs — the tested variable never reached the code path in either | §4: precondition must clear *before* the A/B is meaningful; §9 re-sequences it first |
| 4 | Status table marked Task 5 ✅ from "user completed the reboot," not from the plan's own stated pass criterion | Stated criterion was "all cases PASS"; actual was 0/12 PASS both runs | §12: every criterion states its measurement, so a proxy action can't satisfy it |
| 5 | Librarian's MEDIUM-confidence claims (race-condition explanation, "Good bye" behavior) restated as unqualified fact downstream | Findings tagged `Confidence: MEDIUM — no direct source` at origin; tag dropped in the journal/spec rewrite | §8: confidence tags are mandatory and must carry through verbatim |

## §2. Problem (status column added)

Three original failure modes — **none reproduced through the real code path yet**:

| Mode | Description | Reproduced? |
|---|---|---|
| Charset hang | `"Cannot convert from the charset 'Unknown encoding (-1)'!"` | No |
| Silent truncation | 6.5MB / 11.67MB output instead of ~2.3GB | No |
| Zero bytes | No output file | No |

New blocking precondition, not present in v1, surfaced by Task 4/5: every agent-context Saracon invocation fails before reaching any of the above:

```
Error: Can't open registry key 'HKCU\Software\Weiss Engineering\Saracon' (error 5: access is denied.)
Error: Cannot initialize OLE
Error: Module "wxIdleWakeUpModule" initialization failed
Error: Initialization failed in post init, aborting.
```

Reproduced 2/2 (ACP=65001 and ACP=1252) — confidence **HIGH** this is real and distinct. Cause unresolved; see §4.

## §3. Root-cause status — honest accounting, no promotion without evidence

| Hypothesis | Status | Confidence | Basis |
|---|---|---|---|
| UTF-8 beta / ACP=65001 → charset error | Candidate, untested against this pipeline | MEDIUM | Librarian findings #1–#3 (wx-users thread, wxWidgets PR #1570): HIGH confidence on the *general* wx/codepage mechanism; zero local reproductions |
| Nondeterminism = audio-thread / locale-init race | Speculative | LOW–MEDIUM | Librarian's own tag: "no direct Saracon source" — do not promote further |
| "Good bye" + exit 0 fires regardless of outcome | Plausible | MEDIUM | Inferred from manual text, not directly confirmed |
| Registry/OLE init failure blocks all agent-context runs | Confirmed | HIGH | Reproduced identically twice, independent of ACP |

No task in §9 may cite the UTF-8 hypothesis as confirmed until a probe case in §7 shows a run that (a) clears the §6 precondition canary and (b) exercises the charset code path.

## §4. Precondition: registry/OLE init failure

`reg query HKCU\Software\Weiss Engineering\Saracon` succeeds from the agent shell and shows existing `winpos` values, under identity `LANCE\Lance` — the same identity `whoami` reports for the agent process. Matching usernames rule out "wrong account" as the full explanation. Two sub-hypotheses remain open:

**A — ACL excludes the executing token.** The key (or parent `HKCU\Software\Weiss Engineering`) permits read (satisfies `reg query`) but not the write/all-access mode Saracon requests on open.

**B — Non-interactive logon session, no attached desktop.** `"Cannot initialize OLE"` and `"wxIdleWakeUpModule" initialization failed` are consistent with a GUI toolkit failing outside an interactive Window Station/Desktop (`WinSta0\Default`) — typical when a coding-agent tool spawns children from a service/background logon session rather than the interactive one, even under a matching username. This would also explain why toggling ACP changed nothing: the failure sits upstream of any codepage-dependent code.

**Decisive test — run this before anything else in this spec:**

```powershell
[Security.Principal.WindowsIdentity]::GetCurrent().Name; query session; (Get-Process -Id $PID).SessionId
```

Run once from the agent context, once from the user's interactive terminal. Compare `SessionId` and session name/state. A mismatch (or a non-`Active`/non-`Console` session for the agent) confirms **B** and closes **A** without further testing.

| Outcome | Required fix |
|---|---|
| A confirmed | Grant `FullControl` on `HKCU\Software\Weiss Engineering` (and subkeys) to the executing SID, from an elevated interactive session |
| B confirmed | Saracon cannot be invoked from the agent process, period. Route every Saracon-calling probe step to the user's interactive terminal, and have the harness detect and *report* this rather than silently absorbing the failure (§7) |

## §5. Architecture — kept vs. changed from v1

**Kept as committed, no further changes:**
- `tools/SacdProbe/SacdProbe.csproj`, `DffFixtureFactory.cs` (corrected FRM8/FVER header; `ExpectedPcmBytes()` fixed to `264600` — decimation by 32, not 8)
- `src/Services/Audio/SaraconService.cs` output-size sanity check (Task 6)
- `src/Services/Audio/DffMetadataStripper.cs` exception logging + EOF-bounded chunk walk (Task 7)
- 6-case fixture matrix; journal path and append-only convention

**Changed in this revision:**
- `ProbeRunner.cs` verdict classification — §7
- Execution sequencing — §9
- `Program.cs` gains a precondition canary, run and gated before the matrix — §6

## §6. Precondition canary (new — runs before the matrix, every time)

```csharp
var canary = RunCase(FixtureCase.Baseline, DffFixtureFactory.Build(FixtureCase.Baseline), stripped: false);
if (ClassifySignature(canary.ErrorText) == FailureSignature.RegistryOleInit)
{
    Console.WriteLine("PRECONDITION FAILED: registry/OLE init error — see spec §4. Aborting matrix; results would be uninformative.");
    return 2; // distinct exit code: environment failure, not a hypothesis result
}
```

Exit code `2` is reserved for this case specifically so a wrapping CI/status check can't confuse "environment broken" with "PROBE PASS" or "PROBE FAIL (hypothesis)."

## §7. Verdict logic fix — signature-matched classification

Replace blanket `IsError → FAIL-expected` with a signature match against the case's *declared* expectation:

```csharp
public enum FailureSignature
{
    None,
    RegistryOleInit,   // "Can't open registry key" / "Cannot initialize OLE"
    CharsetEncoding,   // "Unknown encoding (-1)" / "Cannot convert from the charset"
    Truncation,        // exit 0, output bytes < 50% expected
    ZeroBytes,         // exit 0, no output file
    Other,             // anything unclassified — always FAIL-unexpected
}

private static FailureSignature ClassifySignature(string errorText) => errorText switch
{
    var s when s.Contains("Cannot initialize OLE") || s.Contains("Can't open registry key") => FailureSignature.RegistryOleInit,
    var s when s.Contains("Unknown encoding") || s.Contains("charset") => FailureSignature.CharsetEncoding,
    _ => FailureSignature.Other,
};
```

| Case | Declared expected signature (raw variant) |
|---|---|
| Baseline | `None` (must PASS) |
| Id3Valid | `None` |
| Id3CorruptSize | `Other` (A2 bug — walker throws; now logged, not silently masked) |
| ComtNonAscii | `CharsetEncoding` if UTF-8 beta ON, else `None` |
| BracketedName | `None` (A5 filename hypothesis — if this instead reproduces `CharsetEncoding`, that is new information to journal, not a shrug) |
| Id3CorruptPlusBracketed | `Other` |

Verdict rule: `PASS` on no error; `FAIL-expected` **only** when the classified signature matches the case's declared expectation; anything else is `FAIL-unexpected`, which aborts the matrix instead of folding into a misleadingly green `PROBE PASS`.

## §8. Journal discipline (new process rule)

Every line added to `## Findings` carries its source's confidence tag verbatim (`HIGH` / `MEDIUM` / `LOW`, one-line basis). Spec text, plan text, and commit messages may not drop or upgrade a tag when restating a finding. An entry with no tag is invalid and must be corrected before it's used to justify a fix.

## §9. Fix plan (re-sequenced, evidence-gated)

0. Run §4's decisive session/ACL test. Do not proceed until the outcome is logged.
1. Apply the matching §4 fix (ACL grant, or route Saracon calls to the user's terminal).
2. **Probe run #1** (UTF-8 still ON) using the corrected `ProbeRunner` (§6, §7) — the first run whose result is actually informative.
3. User checkpoint: disable UTF-8 beta, reboot, verify `ACP != 65001`.
4. **Probe run #2** (UTF-8 OFF), same execution context/session type as run #1 — the variable v1 failed to control for. Compare case-by-case.
5. Size-check and stripper fixes — already committed (Tasks 6–7). No action; carried forward.
6. Real Disc-10 gate — has its own precondition, §10.
7. Noise prune — unchanged, §11.

## §10. Real-pipeline gate precondition (new — not in v1)

`dotnet run -- audio sacd-convert ...` fails independently of Saracon: `App.Program.cs` unconditionally initializes Google/YouTube OAuth (`GoogleSetup.BuildYouTubeServiceAsync`, `src/Services/Google/GoogleSetup.cs:34`, called from `src/App/Program.cs:60`), which requires an interactive OAuth flow and throws `HttpListenerException` in any non-interactive context. This blocks Task 8 regardless of the Saracon/UTF-8 outcome.

**Fix:** make Google service registration conditional on the invoked subcommand — lazy DI registration, or a skip in `Program.cs` when the subcommand is `audio` — so the audio gate doesn't require an unrelated auth flow. Track as its own task; do not let it silently sit inside the SACD work as an unstated blocker.

## §11. Cleanup phase (unchanged from v1 §9)

Delete once §9 closes: the 9 debug scripts (`dff-inspect.csx`, `inspect-dff.ps1`, `debug-sacd-disc10.ps1`, `test-saracon-simple.ps1`, `extract-and-test.ps1`, `test-saracon-now.ps1`, `diagnose-saracon-complete.ps1`, `strip-dff-metadata.ps1`, `test-saracon-gui-popup.ps1`), `SACD-Saracon-Analysis-Summary.md`, `docs/SACD-SARACON-ISSUE.md`, and the named `C:\Temp\saracon*` fixture directories. Keep: probe harness, journal, this spec, `SACD errors.md`.

## §12. Success criteria — each with its measurement, no proxy checkmarks

| Criterion | Measured by |
|---|---|
| Precondition cleared | §4 test result logged; fix applied; canary case (§6) exits past `RegistryOleInit` |
| UTF-8 hypothesis actually tested | Both probe runs (#1, #2) classify every case as something other than `RegistryOleInit` |
| Root cause established | ≥1 case flips `FAIL-expected(CharsetEncoding)` → `PASS` between run #1 and run #2, with no other variable changed |
| Defense fixes still hold | Re-run probe post-precondition-fix; still PASS |
| Real pipeline gate | §10 fix applied; full run completes; output size within 5% of source PCM estimate |
| Noise pruned | §11 file list absent from `git status` |
| Journal discipline held | Spot-check: every `## Findings` entry carries a confidence tag |

## §13. Rules (carried forward + new)

- Phase 1 changes nothing outside `tools/SacdProbe` until §9 step 1's precondition fix is applied and logged.
- No new NuGet packages, no test frameworks, no ps1 scripts, no shell for FS ops.
- Build-verify after every edit; commit after each task, 1–3 files, atomic.
- No root-cause claim without journal evidence that carries its confidence tag (§8) — this is the rule v1 stated and violated; it is now enforced structurally by §6/§7, not just declared.
