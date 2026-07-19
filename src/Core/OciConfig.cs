namespace Core;

public static class OciConfig
{
	public const string Host = "100.68.154.15";
	public const string User = "ubuntu";
	public static readonly string KeyPath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		".ssh",
		"oci",
		"id_ed25519"
	);
}
