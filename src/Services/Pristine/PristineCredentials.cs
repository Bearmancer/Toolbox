namespace Services.Pristine;

public sealed class PristineCredentials
{
	public required string BaseOutDir { get; init; }

	public static PristineCredentials Read() =>
		new()
		{
			BaseOutDir =
				Environment.GetEnvironmentVariable("PRISTINE_BASE_OUT_DIR")
				?? throw new InvalidOperationException("Missing: PRISTINE_BASE_OUT_DIR"),
		};
}
