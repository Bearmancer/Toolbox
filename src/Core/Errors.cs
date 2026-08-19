using ErrorOr;

namespace Core;

public static class Errors
{
	public static class General
	{
		public static Error Unexpected =>
			Error.Failure("General.Unexpected", "An unexpected error occurred.");

		public static Error Internal =>
			Error.Failure("General.Internal", "An internal system error occurred.");
	}

	public static class Validation
	{
		public static Error InvalidInput(string field, string reason) =>
			Error.Validation($"Validation.{field}", reason);

		public static Error RequiredField(string field) =>
			Error.Validation($"Validation.{field}Required", $"{field} is required.");
	}

	public static class YouTube
	{
		public static Error RateLimitExceeded =>
			Error.Failure("YT.RateLimit", "YouTube API rate limit exceeded. Retrying...");

		public static Error ApiError(string message) => Error.Failure("YT.ApiError", message);

		public static Error QuotaExceeded(string message) =>
			Error.Failure("YT.QuotaExceeded", message);
	}

	public static class Azure
	{
		public static Error AuthenticationFailed =>
			Error.Unauthorized("Azure.AuthFailed", "Azure authentication failed.");

		public static Error RateLimitExceeded =>
			Error.Failure("Azure.RateLimit", "Azure API rate limit exceeded.");
	}

	public static class LastFm
	{
		public static Error RateLimitExceeded =>
			Error.Failure("Lfm.RateLimit", "Last.fm API rate limit exceeded.");

		public static Error MalformedResponse =>
			Error.Failure(
				"Lfm.MalformedResponse",
				"The API response is missing expected structure."
			);

		public static Error UserNotFound(string user) =>
			Error.NotFound("Lfm.UserNotFound", $"Last.fm user {user} not found.");

		public static Error ApiError(string message) => Error.Failure("Lfm.ApiError", message);
	}

	public static class DocIntel
	{
		public static Error ApiError(string message) => Error.Failure("DocIntel.ApiError", message);
	}

	public static class Speech
	{
		public static Error ApiError(string message) => Error.Failure("Speech.ApiError", message);
	}

	public static class Vision
	{
		public static Error ApiError(string message) => Error.Failure("Vision.ApiError", message);
	}

	public static class OpenAi
	{
		public static Error ApiError(string message) => Error.Failure("OpenAI.ApiError", message);
	}

	public static class Translate
	{
		public static Error ApiError(string message) =>
			Error.Failure("Translate.ApiError", message);
	}

	public static class TextAnalytics
	{
		public static Error ApiError(string message) =>
			Error.Failure("TextAnalytics.ApiError", message);
	}

	public static class Audio
	{
		public static Error BinaryNotFound(string name) =>
			Error.Failure(
				"Audio.BinaryNotFound",
				$"{name} not found on system PATH. Install it and ensure it is available in your system PATH."
			);

		public static Error ExtractionFailed(string iso, string reason) =>
			Error.Failure("Audio.ExtractionFailed", $"SACD extraction failed for {iso}: {reason}");

		public static Error NoDffFound(string directory) =>
			Error.NotFound("Audio.NoDff", $"No .dff file found in {directory}");

		public static Error NoCueFound(string directory) =>
			Error.NotFound("Audio.NoCue", $"No .cue file found in {directory}");

		public static Error GainDetectionFailed(string file, string? reason = null) =>
			Error.Failure(
				"Audio.GainFailed",
				reason is null
					? $"Could not detect peak levels in {file}"
					: $"Could not detect peak levels in {file}: {reason}"
			);

		public static Error ConversionFailed(string file, string reason) =>
			Error.Failure("Audio.ConvertFailed", $"Conversion failed for {file}: {reason}");

		public static Error NoIsoFound(string directory) =>
			Error.NotFound("Audio.NoIso", $"No .iso files found in {directory}");

		public static Error InvalidCueFormat(string file, string reason) =>
			Error.Validation("Audio.InvalidCue", $"Malformed CUE file {file}: {reason}");

		public static Error ProbeFailed(string file, string reason) =>
			Error.Failure("Audio.ProbeFailed", $"DSD probe failed for {file}: {reason}");

		public static Error InsufficientDiskSpace(
			string path,
			long requiredBytes,
			long availableBytes
		) =>
			Error.Failure(
				"Audio.InsufficientDiskSpace",
				$"Insufficient disk space at {path}. Required: {requiredBytes / 1_048_576} MB, Available: {availableBytes / 1_048_576} MB."
			);

		public static Error OutputPathUnwritable(string path) =>
			Error.Failure("Audio.OutputPathUnwritable", $"Output path is not writable: {path}");

		public static Error InvalidInputPath(string path) =>
			Error.Failure(
				"Audio.InvalidInputPath",
				$"Input path does not exist or is not accessible: {path}"
			);

		public static Error ProcessFailed(string binary, string reason) =>
			Error.Failure("Audio.ProcessFailed", $"{binary} process failed: {reason}");

		public static Error PathTooLong(string path, int length) =>
			Error.Failure(
				"Audio.PathTooLong",
				$"Output path exceeds Windows MAX_PATH ({length} chars): {path}"
			);

		public static Error StripFailed(string file, string reason) =>
			Error.Failure("Audio.StripFailed", $"DFF metadata strip failed for {file}: {reason}");
	}

	public static class Pristine
	{
		public static Error MissingBaseOutDir =>
			Error.Validation("Pristine.MissingBaseOutDir", "PRISTINE_BASE_OUT_DIR not set");

		public static Error AuthMissing =>
			Error.NotFound("Pristine.AuthMissing", "No session at state/pristine/auth.json — run pristine login first");

		public static Error BrowserFailed(string reason) =>
			Error.Failure("Pristine.BrowserFailed", $"Browser failed: {reason}");

		public static Error LoginTimeout =>
			Error.Failure("Pristine.LoginTimeout", "Login not completed within timeout");

		public static Error ResolveFailed(string code) =>
			Error.NotFound("Pristine.ResolveFailed", $"Could not resolve {code}");

		public static Error DownloadFailed(string file, string reason) =>
			Error.Failure("Pristine.DownloadFailed", $"Download failed {file}: {reason}");

		public static Error AlbumNotFound(string code) =>
			Error.NotFound("Pristine.AlbumNotFound", $"Album not found: {code}");
	}
}
