using System.ComponentModel;
using Core;
using Services.Azure;
using Spectre.Console.Cli;

namespace CLI.Azure;

[Description(
    "Analyze images using Azure Computer Vision. "
    + "Extract tags, detect objects, read printed text (OCR), or generate image captions. "
    + "For dense document OCR prefer 'docintel' instead — it handles layout, tables, and large documents better."
)]
public class VisionCommand(VisionService service) : AsyncCommand<VisionCommand.Settings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext ctx,
        Settings s,
        CancellationToken ct
    )
    {
        var result = await service.AnalyzeAsync(s.File, s.Feature ?? "tags", s.Lang ?? "en", ct);
        Telemetry.Info("{Result}", result);
        return 0;
    }

    public sealed class Settings : CommandSettings
    {
        [Description(
            "Path to the image file to analyze. "
            + "Supports JPEG, PNG, GIF, BMP, and WEBP formats."
        )]
        [CommandArgument(0, "<file>")]
        public required string File { get; init; }

        [Description(
            "Vision feature to extract. "
            + "'tags' — content tags describing objects, actions, and scenes. "
            + "'objects' — individual detected objects with bounding boxes. "
            + "'read' — printed and handwritten text OCR. "
            + "'caption' — a single sentence describing the image. "
            + "'denseCaptions' — captions for every region of the image. "
            + "'people' — detected people with bounding boxes. "
            + "(default: tags)"
        )]
        [CommandOption("--feature <FEATURE>")]
        public string? Feature { get; init; }

        [Description(
            "Language hint for feature extraction (BCP-47 format, e.g. 'en', 'ja'). "
            + "Affects tag language and OCR recognition. (default: en)"
        )]
        [CommandOption("--lang <LANG>")]
        public string? Lang { get; init; }
    }
}
