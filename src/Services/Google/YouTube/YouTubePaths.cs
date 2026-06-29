using Core;

namespace Services.Google.YouTube;

internal static class YouTubePaths
{
    public static readonly string StateRoot = Path.Combine(
        PathResolver.RepoRoot,
        "state",
        "youtube"
    );
    public static readonly string RawDir = Path.Combine(StateRoot, "raw");
    public static readonly string ProcessedDir = Path.Combine(StateRoot, "processed");
    public static readonly string DeletedDir = Path.Combine(StateRoot, "deleted");
}
