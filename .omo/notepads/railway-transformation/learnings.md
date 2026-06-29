# Learnings — Railway Transformation

## Batch 1 — TranslateService ErrorOr Conversion

- Errors.cs has no Errors.Translate class yet. Added one following Errors.YouTube pattern.
- TranslateBatchAsync had two exception paths:
  1. ArgumentOutOfRangeException for oversized texts
  2. TranslateAsync SDK call failures
- Both wrapped into single Errors.Translate.ApiError(message).
- Callers identified: YouTubeTranslationService.ExecuteTranslationBatchesAsync, TranslateCommand (CLI).
- Callers not modified per Batch 2.5 scope boundary.
- TryTranslateAsync in YouTubeTranslationService.cs already catches TranslateBatchAsync by type — will need update in Batch 2.5 since exception no longer thrown.
