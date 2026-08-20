---
concern: Azure
status: active
ref: github.com/Bearmancer/Toolbox @ fe6e322d (master)
source_docs: [god-audit-spec.md, audio-cli-spec.md, erroror_migration_assessment.md]
---

# Azure Services — Plan

## 1. Scope

Vision, Translate, DocIntel, Speech (STT/TTS), TextAnalytics (Sentiment/PII/KeyPhrases/NER), OpenAI. Seven Spectre CLI commands exist: `translate`, `docintel`, `vision`, `stt`, `tts`, `ner`, `phrases`. `SentimentAsync`, `PiiAsync`, and the entire `OpenAiService` class have no command and no other caller — genuinely unreachable, confirmed by grep against real source, not inherited from a prior pass.

## 2. Findings

### F-1 — Three unreachable service surfaces `[LOW] [HIGH]`

`TextAnalyticsService.SentimentAsync`, `TextAnalyticsService.PiiAsync`, and `OpenAiService` (entire class — two total references: its own declaration and one `AddSingleton<OpenAiService>()` in DI) are complete, working code with no path to invoke them. This is a decision point, not an obvious deletion — see §3.

### F-2 — `TextAnalyticsService` is five duplicated guard/catch blocks `[LOW] [MEDIUM]`

Five public methods (Sentiment, Pii, KeyPhrases, and two more) share near-identical validation and exception handling. Genuine duplication, not justified SRP.

### F-3 — `TranslateService` catches generic exceptions instead of mapping them `[MEDIUM] [HIGH]`

No typed mapper for `RequestFailedException`/`HttpRequestException` status codes (429 → rate-limited, 401/403 → auth-failed). Everything collapses to a generic `ApiError`, which means the caller-side rate-limit handling in other services can't distinguish an Azure throttle from any other failure.

## 3. Decision register

**Open, not decided here:** F-1's disposition. Two defensible paths — delete the three unreachable members, or spend ~20 minutes wiring `sentiment`/`pii` CLI commands and an `ask` command for OpenAI. The corpus's own prior verification (`ponytail_audit_verified.md`) already rejected the more aggressive proposal to delete `Core.Errors` or `ErrorOr` wholesale as **WRONG** — worth remembering before treating every "unreachable = delete" instinct as automatically correct here too. Z2 below makes this decision explicit rather than defaulting silently.

## 4. CPM network

**Project duration: 3.5 h.**

| ID | Task | Dur | Deps | ES | EF | LS | LF | Float |
|---|---|---:|---|---:|---:|---:|---:|---:|
| Z1 | Confirm call sites: Sentiment/Pii/OpenAI unreachable (re-verify, don't inherit) | 0.5 | — | 0.0 | 0.5 | 0.0 | 0.5 | **0** |
| Z2 | Decide: wire behind CLI commands, or delete | 1.0 | Z1 | 0.5 | 1.5 | 0.5 | 1.5 | **0** |
| Z4 | Extract shared TextAnalytics guard/runner, collapse 5 clones | 2.0 | Z1 | 0.5 | 2.5 | 1.0 | 3.0 | 0.5 |
| Z5 | Typed exception mapper: `TranslateService` 429/401/403 | 1.0 | Z1 | 0.5 | 1.5 | 2.0 | 3.0 | 1.5 |
| Z3 | Execute Z2's decision | 1.5 | Z2 | 1.5 | 3.0 | 1.5 | 3.0 | **0** |
| Z6 | Build gate | 0.5 | Z3,Z4,Z5 | 3.0 | 3.5 | 3.0 | 3.5 | **0** |

Critical path: `Z1 → Z2 → Z3 → Z6`.

## 5. Out of scope

The three `EventListener` SDK adapters (Azure Core, ClientModel, Speech) — verified genuine, incompatible SDKs require the separate wrappers; not duplication.
