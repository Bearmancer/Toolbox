namespace Toolbox.Reader;

public record ExtractedContent(
    string Title,
    string? Author,
    string Content,
    string? HtmlContent,
    Uri SourceUrl,
    DateTimeOffset? PublishedAt,
    int WordCount,
    IReadOnlyList<string>? Images
);

public record OcrResult(string Text, int Confidence, IReadOnlyList<OcrBlock>? Blocks);

public record OcrBlock(string Text, OcrBlockType Type, BoundingBox? Bounds);

public enum OcrBlockType
{
    Text,
    Table,
    Image,
    Handwriting
}

public record BoundingBox(int X, int Y, int Width, int Height);