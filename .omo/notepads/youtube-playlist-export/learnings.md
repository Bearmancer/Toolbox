# Learnings — youtube-playlist-export

## 2026-06-24 Planning Phase
- TranslateService takes 4 params: (text, toLang, fromLang, ct). Plan said 3. User wants fromLang default to 'auto', toLang default to 'en'.
- Pre-existing IDE0046 in InputFile.cs — suppressed via .editorconfig (ternary for throw reduces readability).
- Old YouTubeVideo uses DetectedLanguage.EqualsIgnoreCase('eng') — new should use 'en' (ISO 639-1).
- Old PlaylistSnapshot stores VideoIds — new one does NOT (lightweight summaries only).
- TranslateService returns 'xx -> en: text' prefix — strip before storing.
- Google credentials are global env vars, not in .env.
## 2026-06-24 YouTubeVideo DTO cleanup
- YouTubeVideo is now a pure data record: 8 init-only properties, zero methods, zero computed properties.
- `FromPlaylistItem()` inlined at caller in `YouTubePlaylistOrchestrator.cs` (line 163).
- `YouTubeTranslationService.cs` already used `with { }` syntax — no changes needed there.
- Removed: `DetectedLanguage`, `TranslatedAt`, `VideoUrl`, `ChannelUrl`, `FormattedDuration`, `DisplayTitle`, `DisplayDescription`, `NeedsTranslation`, `WithTranslation()`, `WithoutTranslation()`, `FromPlaylistItem()`.
