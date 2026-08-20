using Core;

namespace Services.Pristine;

public static class PristinePaths
{
	public static string UserDataDir =>
		Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"pristine-playwright-profile"
		);

	public static string AuthPath =>
		Path.Combine(PathResolver.RepoRoot, "state", "auth", "pristine", "auth.json");
}
