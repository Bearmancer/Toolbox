namespace Services.Google.Dashboard;

public static class OciConfig
{
	public static string Host => Environment.GetEnvironmentVariable("OCI_HOST") ?? "100.68.154.15";
	public static string User => Environment.GetEnvironmentVariable("OCI_USER") ?? "ubuntu";
	public static string KeyPath =>
		Environment.GetEnvironmentVariable("OCI_KEY_PATH")
		?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".ssh",
			"oci",
			"id_ed25519"
		);
}
