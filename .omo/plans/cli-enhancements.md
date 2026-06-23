# CLI Enhancements + Code Cleanup Plan

## Pre-Conditions
- Build must pass before starting
- Run `dotnet build AzureAI.slnx` after EVERY step

## Phase 1: Critical Bug Fixes

### 1.1 Fix SpeechSttService WAV Extension Bug
**File:** `src/Services/Azure/SpeechSttService.cs`
**Line 60:** `if (ext is not ".wav") throw new InvalidOperationException("Extension is not WAV!");`
**Problem:** Throws AFTER ffmpeg conversion. Non-WAV files always fail.
**Fix:** Remove the throw or check `wavPath` instead of `ext`.

### 1.2 Fix AzureSetup GC'd Event Listener
**File:** `src/Services/Azure/AzureSetup.cs`
**Line 19:** `_ = new AzureEventSourceListener(...)`
**Problem:** Assigned to discard, immediately eligible for GC.
**Fix:** Store in a static field or remove.

### 1.3 Fix SpeechSttService Discarded Return Value
**File:** `src/Services/Azure/SpeechSttService.cs`
**Line 24:** `FileHelpers.ReadChecked(path, MaxBytes, "Speech");`
**Problem:** Return value (byte[]) discarded. Only validates size.
**Fix:** Use the bytes or replace with pure size check.

## Phase 2: Remove Dead Code

### 2.1 Remove Telemetry.ForService()
**File:** `src/Core/Telemetry.cs`
**Reason:** Zero callers in entire codebase.

### 2.2 Remove Telemetry.Warn()
**File:** `src/Core/Telemetry.cs`
**Reason:** Zero callers in entire codebase.

### 2.3 Make Telemetry.LevelSwitch Private
**File:** `src/Core/Telemetry.cs`
**Reason:** Only used internally in Configure().

### 2.4 Remove DocIntelService.Models
**File:** `src/Services/Azure/DocIntelService.cs`
**Reason:** Identity map ("prebuilt-read" → "prebuilt-read"), zero callers.

### 2.5 Remove ManualIntegrationTest.cs
**File:** `src/App/ManualIntegrationTest.cs`
**Reason:** 7/8 tests commented out. Dead code.
**Also:** Remove `--test` flag from Program.cs.

## Phase 3: CLI Options (Tier 1 — HIGH priority + QUICK effort)

### 3.1 Azure OpenAI — `chat`
**File:** `src/CLI/Azure/ChatCommand.cs`, `src/Services/Azure/OpenAiService.cs`
**Add options:**
- `--system` (system prompt) — prepend to messages
- `--temperature` (float 0-2) — controls randomness
- `--max-tokens` (int) — controls response length

### 3.2 Text Analytics — `sentiment`
**File:** `src/CLI/Azure/SentimentCommand.cs`, `src/Services/Azure/TextAnalyticsService.cs`
**Add option:**
- `--opinion-mining` (bool) — aspect-level sentiment

### 3.3 Text Analytics — `pii`
**File:** `src/CLI/Azure/PiiCommand.cs`, `src/Services/Azure/TextAnalyticsService.cs`
**Add option:**
- `--domain` (string) — PHI filter for healthcare

### 3.4 Translator — `translate`
**File:** `src/CLI/Azure/TranslateCommand.cs`, `src/Services/Azure/TranslateService.cs`
**Add options:**
- `--profanity` (enum: none/delete/mark) — profanity handling
- `--text-type` (enum: plain/html) — HTML-aware translation

### 3.5 Vision — `vision`
**File:** `src/CLI/Azure/VisionCommand.cs`, `src/Services/Azure/VisionService.cs`
**Add features to switch:**
- `caption` — AI-generated caption
- `densecaptions` — multiple detailed captions
- `people` — detected people

### 3.6 Document Intelligence — `docintel`
**File:** `src/CLI/Azure/DocIntelCommand.cs`, `src/Services/Azure/DocIntelService.cs`
**Add options:**
- `--pages` (string) — page range (e.g., "1-5")
- `--locale` (string) — document locale

### 3.7 Speech TTS — `tts`
**File:** `src/CLI/Azure/SpeechTtsCommand.cs`, `src/Services/Azure/SpeechTtsService.cs`
**Add option:**
- `--format` (enum: wav/mp3/webm/ogg/flac) — output format

### 3.8 Speech STT — `stt`
**File:** `src/CLI/Azure/SpeechSttCommand.cs`, `src/Services/Azure/SpeechSttService.cs`
**Add option:**
- `--profanity` (enum: raw/removed/masked) — profanity handling

## Phase 4: CLI Options (Tier 2 — HIGH priority + SHORT effort)

### 4.1 Azure OpenAI — `chat`
**Add options:**
- `--top-p` (float 0-1) — nucleus sampling
- `--frequency-penalty` (float -2 to 2) — reduce repetition
- `--presence-penalty` (float -2 to 2) — encourage diversity

### 4.2 Translator — `translate`
**Add option:**
- `--to` (repeatable) — multiple target languages

### 4.3 Vision — `vision`
**Enhancement:**
- Allow multiple `--feature` flags (combine with `|`)

### 4.4 Document Intelligence — `docintel`
**Add option:**
- `--format` (enum: text/markdown) — output format

### 4.5 Speech TTS — `tts`
**Add options:**
- `--rate` (string) — prosody rate (e.g., "+20%")
- `--pitch` (string) — prosody pitch
- `--volume` (string) — prosody volume
- `--style` (string) — voice style (cheerful, sad, etc.)

### 4.6 Text Analytics — `pii`
**Add option:**
- `--categories` (comma-separated) — PII categories filter

### 4.7 Text Analytics — `ner`
**Add option:**
- `--categories` (comma-separated) — entity categories filter

### 4.8 Language Detection — `language`
**Rename:**
- `--lang` → `--hint` (it's a country hint, not a language)

## Phase 5: Brittleness Fixes (from Metis analysis)

### 5.1 String-Based Feature Switch
**File:** `src/Services/Azure/VisionService.cs`
**Current:** `feature switch { "tags" => ..., "objects" => ..., "read" => ... }`
**Fix:** Use enum or constants instead of magic strings.

### 5.2 String-Based Model IDs
**File:** `src/Services/Azure/DocIntelService.cs`
**Current:** `["prebuilt-read"] = "prebuilt-read"` (identity map)
**Fix:** Remove dead Models property, use string constants if needed.

### 5.3 String-Based Language Codes
**All services:** Hardcoded `"en"`, `"en-US"`, etc.
**Assessment:** Fine for CLI tool — language codes are standardized strings. Don't over-engineer.

## Post-Conditions
- `dotnet build AzureAI.slnx` passes with zero errors
- All Tier 1 options working
- All dead code removed
- All critical bugs fixed

## Effort Summary
| Phase | Items | Effort |
|-------|-------|--------|
| Phase 1: Bug fixes | 3 | Quick |
| Phase 2: Dead code | 5 | Quick |
| Phase 3: Tier 1 options | 14 | Short |
| Phase 4: Tier 2 options | 8 | Short |
| Phase 5: Brittleness | 3 | Quick |
| **Total** | **33 items** | **~2-3 hours** |
