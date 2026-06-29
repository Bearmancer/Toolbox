using ErrorOr;

namespace Core;

/// <summary>
/// Central taxonomy for all application errors.
/// Prevents "magic strings" and ensures consistent error mapping across services.
/// </summary>
public static class Errors
{
    public static class General
    {
        public static Error Unexpected => Error.Failure(code: "General.Unexpected", description: "An unexpected error occurred.");
        public static Error Internal => Error.Failure(code: "General.Internal", description: "An internal system error occurred.");
    }

    public static class Validation
    {
        public static Error InvalidInput(string field, string reason) => 
            Error.Validation(code: $"Validation.{field}", description: reason);
        
        public static Error RequiredField(string field) => 
            Error.Validation(code: $"Validation.{field}Required", description: $"{field} is required.");
    }

    public static class YouTube
    {
        public static Error PlaylistNotFound(string id) => 
            Error.NotFound(code: "YT.PlaylistNotFound", description: $"Playlist {id} was not found on YouTube.");
        
        public static Error VideoNotFound(string id) => 
            Error.NotFound(code: "YT.VideoNotFound", description: $"Video {id} was not found on YouTube.");
        
        public static Error RateLimitExceeded => 
            Error.Failure(code: "YT.RateLimit", description: "YouTube API rate limit exceeded. Retrying...");
        
        public static Error ApiError(string message) => 
            Error.Failure(code: "YT.ApiError", description: message);
    }

    public static class Azure
    {
        public static Error ServiceUnavailable(string service) => 
            Error.Failure(code: $"Azure.{service}Unavailable", description: $"{service} is currently unavailable.");
        
        public static Error AuthenticationFailed => 
            Error.Unauthorized(code: "Azure.AuthFailed", description: "Azure authentication failed.");
        
        public static Error RateLimitExceeded => 
            Error.Failure(code: "Azure.RateLimit", description: "Azure API rate limit exceeded.");
    }

    public static class LastFm
    {
        public static Error UserNotFound(string user) => 
            Error.NotFound(code: "Lfm.UserNotFound", description: $"Last.fm user {user} not found.");
        
        public static Error ApiError(string message) => 
            Error.Failure(code: "Lfm.ApiError", description: message);
        
        public static Error RateLimitExceeded => 
            Error.Failure(code: "Lfm.RateLimit", description: "Last.fm API rate limit exceeded.");
        
        public static Error MalformedResponse => 
            Error.Failure(code: "Lfm.MalformedResponse", description: "The API response is missing expected structure.");
    }

    public static class DocIntel
    {
        public static Error ApiError(string message) =>
            Error.Failure(code: "DocIntel.ApiError", description: message);
    }

    public static class Speech
    {
        public static Error ApiError(string message) =>
            Error.Failure(code: "Speech.ApiError", description: message);
    }

    public static class Vision
    {
        public static Error ApiError(string message) =>
            Error.Failure(code: "Vision.ApiError", description: message);
    }

    public static class OpenAI
    {
        public static Error ApiError(string message) =>
            Error.Failure(code: "OpenAI.ApiError", description: message);
    }

    public static class Translate
    {
        public static Error ApiError(string message) =>
            Error.Failure(code: "Translate.ApiError", description: message);
    }
}
