namespace Core;

public static class PathResolver
{
    private static readonly Lazy<string> LazyRepoRoot = new(() =>
    {
        var dir = AppContext.BaseDirectory;

        for (var i = 0; i < 10; i++)
        {
            if (
                Directory.EnumerateFileSystemEntries(dir, ".git").Any()
                || File.Exists(Path.Combine(dir, ".env"))
            )
                return dir;

            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;

            dir = parent.FullName;
        }

        return Directory.GetCurrentDirectory();
    });

    public static string RepoRoot => LazyRepoRoot.Value;
}
