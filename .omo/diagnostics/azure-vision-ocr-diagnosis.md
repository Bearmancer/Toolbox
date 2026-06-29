# Azure Vision OCR — Failure Diagnosis

## 1. The Error

```
Cannot read "17827485953743457956452087596549.jpg" (this model does not support image input)
```

**Source:** This error comes from **Azure OpenAI** (the GPT model deployment), NOT from Azure Computer Vision. It occurs when you send image bytes/URL to a GPT deployment that doesn't have vision capabilities enabled.

## 2. Root Cause

The `.env` file configures `OPENAI_DEPLOYMENT=gpt-4o`. When the `OpenAiService.ChatAsync()` is used with image data, the model responds with "this model does not support image input" because:

- The `OpenAiService` only sends **text** (`UserChatMessage(prompt)`) — it has no code path for image content parts
- Even if it did, the model deployment may need `vision` capability enabled
- GPT-4o *can* handle images, but the current `ChatAsync()` implementation constructs `UserChatMessage(prompt)` with a plain string, not a collection of content parts that includes `ImageContentPart`

## 3. Azure Vision OCR — Verified Working

The **Azure Computer Vision** resource (`ai-lance-vision`, S1 tier, eastus) is correctly deployed. Direct API test confirms OCR works:

```
Request:  POST /computervision/imageanalysis:analyze?features=read
Image:    Test PNG with "Hello OCR" text
Response: readResult.blocks[0].lines[0].text = "Hello OCR"
```

## 4. What Should Work

The `VisionService.AnalyzeAsync()` in `src/Services/Azure/VisionService.cs` correctly uses `ImageAnalysisClient` (Azure.AI.Vision.ImageAnalysis SDK v1.0.0) to call the Computer Vision endpoint.

Usage:
```
dotnet run -- azure vision --feature read <image-file>
```

## 5. Current Blockers

- **Build broken:** YouTube service refactoring left 6 compilation errors in `YouTubePlaylistOrchestrator.cs`, `YouTubePlaylistProcessor.cs`, and `YouTubeTranslationService.cs` (part of Railway Transformation, Batch 3-4 work)
- The Azure project (`Azure.csproj`) compiles fine independently — the errors are in `Google.csproj`

## 6. Fix Path

1. Complete the Railway Transformation (Batches 3-4) to fix the build
2. Then run: `dotnet run -- azure vision --feature read <path-to-image>`
