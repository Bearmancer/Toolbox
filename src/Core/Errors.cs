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

		public static Error PlaylistNotFound(string id) =>
			Error.NotFound("YT.PlaylistNotFound", $"Playlist {id} was not found on YouTube.");

		public static Error VideoNotFound(string id) =>
			Error.NotFound("YT.VideoNotFound", $"Video {id} was not found on YouTube.");

		public static Error ApiError(string message) => Error.Failure("YT.ApiError", message);
	}

	public static class Azure
	{
		public static Error AuthenticationFailed =>
			Error.Unauthorized("Azure.AuthFailed", "Azure authentication failed.");

		public static Error RateLimitExceeded =>
			Error.Failure("Azure.RateLimit", "Azure API rate limit exceeded.");

		public static Error ServiceUnavailable(string service) =>
			Error.Failure($"Azure.{service}Unavailable", $"{service} is currently unavailable.");
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
}
