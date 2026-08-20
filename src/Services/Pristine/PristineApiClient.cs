using System.Text;
using System.Text.Json;
using Core;
using ErrorOr;

namespace Services.Pristine;

public sealed class PristineApiClient
{
	private const string BaseUrl = "https://pristinestreaming.com";
	private const string UserAgent =
		"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
	private const string ApiKeyHeader = "x-pristine-player-api-key";

	public async Task<ErrorOr<string>> AuthenticateAsync(HttpClient http, CancellationToken ct)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		ErrorOr<string> cookieOr = await LoadCookieHeaderAsync(ct);
		if (cookieOr.IsError)
			return cookieOr.Errors;

		try
		{
			using HttpRequestMessage req = NewRequest(
				HttpMethod.Post,
				"/api/v1/authenticate",
				cookieOr.Value,
				null
			);
			req.Content = new StringContent(
				"{\"mode\":\"anon\"}",
				Encoding.UTF8,
				"application/json"
			);
			using HttpResponseMessage resp = await http.SendAsync(req, ct);
			var body = await resp.Content.ReadAsStringAsync(ct);
			if (resp.IsSuccessStatusCode is false)
			{
				if ((int)resp.StatusCode is 401 or 403)
				{
					Telemetry.Error(
						"Pristine.Api.CookieExpired status={Status} body={Body} — session cookie no longer works, run `pristine login`",
						(int)resp.StatusCode,
						body.Length > 200 ? body[..200] : body
					);
					return Errors.Pristine.CookieExpired((int)resp.StatusCode);
				}

				Telemetry.Warn(
					"Pristine.Api.AuthFailed status={Status} body={Body}",
					(int)resp.StatusCode,
					body.Length > 200 ? body[..200] : body
				);
				return Errors.Pristine.ApiAuthFailed($"HTTP {(int)resp.StatusCode}");
			}

			using JsonDocument doc = JsonDocument.Parse(body);
			if (
				doc.RootElement.TryGetProperty("api_key", out JsonElement keyEl) is false
				|| keyEl.GetString() is not string apiKey
				|| string.IsNullOrEmpty(apiKey)
			)
			{
				Telemetry.Warn(
					"Pristine.Api.AuthNoKey body={Body}",
					body.Length > 200 ? body[..200] : body
				);
				return Errors.Pristine.ApiAuthFailed("response had no api_key");
			}

			Telemetry.Debug("Pristine.Api.AuthOk");
			return apiKey;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Api.AuthException: {Error}", ex.Message);
			return Errors.Pristine.ApiAuthFailed(ex.Message);
		}
	}

	public async Task<ErrorOr<PristineApiAlbum>> ResolveAlbumAsync(
		HttpClient http,
		string apiKey,
		string code,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		ErrorOr<string> cookieOr = await LoadCookieHeaderAsync(ct);
		if (cookieOr.IsError)
			return cookieOr.Errors;

		try
		{
			var url = $"/api/v1/search?term={Uri.EscapeDataString(code)}&type=all&limit=24";
			using HttpRequestMessage req = NewRequest(HttpMethod.Get, url, cookieOr.Value, apiKey);
			using HttpResponseMessage resp = await http.SendAsync(req, ct);
			var body = await resp.Content.ReadAsStringAsync(ct);
			if (resp.IsSuccessStatusCode is false)
			{
				Telemetry.Warn(
					"Pristine.Api.SearchFailed code={Code} status={Status}",
					code,
					(int)resp.StatusCode
				);
				return Errors.Pristine.ApiRequestFailed($"search HTTP {(int)resp.StatusCode}");
			}

			using JsonDocument doc = JsonDocument.Parse(body);
			if (
				doc.RootElement.TryGetProperty("results", out JsonElement resultsEl) is false
				|| resultsEl.TryGetProperty("albums", out JsonElement albumsEl) is false
			)
			{
				Telemetry.Warn("Pristine.Api.SearchShapeUnexpected code={Code}", code);
				return Errors.Pristine.ApiRequestFailed("unexpected search response shape");
			}

			foreach (JsonElement a in albumsEl.EnumerateArray())
			{
				var albumCode = a.TryGetProperty("code", out JsonElement codeEl)
					? codeEl.GetString()
					: null;
				if (string.Equals(albumCode, code, StringComparison.OrdinalIgnoreCase) is false)
					continue;

				var id = a.GetProperty("id").GetInt64();
				var title = a.TryGetProperty("title", out JsonElement titleEl)
					? titleEl.GetString() ?? code
					: code;
				var artwork =
					a.TryGetProperty("artwork", out JsonElement artEl)
					&& artEl.ValueKind is JsonValueKind.String
						? artEl.GetString()
						: null;
				Telemetry.Info("{Code}: resolved \"{Title}\"", code, title);
				Telemetry.Debug(
					"Pristine.Api.Resolved code={Code} id={Id} title={Title}",
					code,
					id,
					title
				);
				return new PristineApiAlbum
				{
					Id = id,
					Code = code,
					Title = title,
					ArtworkPath = artwork,
				};
			}

			Telemetry.Warn(
				"Pristine.Api.AlbumNotFound code={Code} — no album matches this code; it may not exist, be misspelled, or not be in this account's catalogue",
				code
			);
			return Errors.Pristine.ResolveFailed(code);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Api.SearchException code={Code}: {Error}", code, ex.Message);
			return Errors.Pristine.ApiRequestFailed(ex.Message);
		}
	}

	public async Task<ErrorOr<List<PristineApiTrack>>> GetAlbumTracksAsync(
		HttpClient http,
		string apiKey,
		long albumId,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		ErrorOr<string> cookieOr = await LoadCookieHeaderAsync(ct);
		if (cookieOr.IsError)
			return cookieOr.Errors;

		try
		{
			var url = $"/api/v1/albums/{albumId}?with[]=tracks";
			using HttpRequestMessage req = NewRequest(HttpMethod.Get, url, cookieOr.Value, apiKey);
			using HttpResponseMessage resp = await http.SendAsync(req, ct);
			var body = await resp.Content.ReadAsStringAsync(ct);
			if (resp.IsSuccessStatusCode is false)
			{
				Telemetry.Warn(
					"Pristine.Api.AlbumFailed id={Id} status={Status}",
					albumId,
					(int)resp.StatusCode
				);
				return Errors.Pristine.ApiRequestFailed($"album HTTP {(int)resp.StatusCode}");
			}

			using JsonDocument doc = JsonDocument.Parse(body);
			if (
				doc.RootElement.TryGetProperty("album", out JsonElement albumEl) is false
				|| albumEl.TryGetProperty("tracks", out JsonElement tracksEl) is false
			)
			{
				Telemetry.Warn("Pristine.Api.AlbumShapeUnexpected id={Id}", albumId);
				return Errors.Pristine.ApiRequestFailed("unexpected album response shape");
			}

			List<PristineApiTrack> tracks = [];
			foreach (JsonElement t in tracksEl.EnumerateArray())
			{
				var id = t.GetProperty("id").GetInt64();
				var title = t.TryGetProperty("title", out JsonElement titleEl)
					? titleEl.GetString() ?? $"Track {id}"
					: $"Track {id}";
				var position = t.TryGetProperty("position", out JsonElement posEl)
					? posEl.GetInt32()
					: tracks.Count + 1;
				var available =
					t.TryGetProperty("available", out JsonElement availEl) && availEl.GetBoolean();
				tracks.Add(
					new PristineApiTrack
					{
						Id = id,
						Title = title,
						Position = position,
						Available = available,
					}
				);
			}

			Telemetry.Debug(
				"Pristine.Api.AlbumTracks id={Id} count={Count}",
				albumId,
				tracks.Count
			);
			if (tracks.Count == 0)
				Telemetry.Warn(
					"Pristine.Api.NoAudioStreams id={Id} — album exists but the server returned zero tracks (release not yet digitized for streaming)",
					albumId
				);
			return tracks;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Api.AlbumException id={Id}: {Error}", albumId, ex.Message);
			return Errors.Pristine.ApiRequestFailed(ex.Message);
		}
	}

	public async Task<ErrorOr<PristineApiListenSources>> GetListenSourcesAsync(
		HttpClient http,
		string apiKey,
		long trackId,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		ErrorOr<string> cookieOr = await LoadCookieHeaderAsync(ct);
		if (cookieOr.IsError)
			return cookieOr.Errors;

		try
		{
			var url = $"/api/v1/listen/{trackId}";
			using HttpRequestMessage req = NewRequest(HttpMethod.Get, url, cookieOr.Value, apiKey);
			using HttpResponseMessage resp = await http.SendAsync(req, ct);
			var body = await resp.Content.ReadAsStringAsync(ct);
			if (resp.IsSuccessStatusCode is false)
			{
				Telemetry.Warn(
					"Pristine.Api.ListenFailed trackId={TrackId} status={Status}",
					trackId,
					(int)resp.StatusCode
				);
				return Errors.Pristine.ApiRequestFailed($"listen HTTP {(int)resp.StatusCode}");
			}

			using JsonDocument doc = JsonDocument.Parse(body);
			if (doc.RootElement.TryGetProperty("sources", out JsonElement sourcesEl) is false)
			{
				Telemetry.Warn("Pristine.Api.ListenShapeUnexpected trackId={TrackId}", trackId);
				return Errors.Pristine.ApiRequestFailed("unexpected listen response shape");
			}

			var flac =
				sourcesEl.TryGetProperty("flac", out JsonElement flacEl)
				&& flacEl.ValueKind is JsonValueKind.String
					? flacEl.GetString()
					: null;
			var mp3 =
				sourcesEl.TryGetProperty("mp3", out JsonElement mp3El)
				&& mp3El.ValueKind is JsonValueKind.String
					? mp3El.GetString()
					: null;
			if (flac is null && mp3 is null)
				Telemetry.Warn(
					"Pristine.Api.NoAudioStreams trackId={TrackId} — listen response had no flac or mp3 source, cannot extract audio",
					trackId
				);
			return new PristineApiListenSources { Flac = flac, Mp3 = mp3 };
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Warn(
				"Pristine.Api.ListenException trackId={TrackId}: {Error}",
				trackId,
				ex.Message
			);
			return Errors.Pristine.ApiRequestFailed(ex.Message);
		}
	}

	private static async Task<ErrorOr<string>> LoadCookieHeaderAsync(CancellationToken ct)
	{
		if (File.Exists(PristinePaths.AuthPath) is false)
			return Errors.Pristine.AuthMissing;

		try
		{
			var json = await File.ReadAllTextAsync(PristinePaths.AuthPath, ct);
			using JsonDocument doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("cookies", out JsonElement cookiesEl) is false)
				return Errors.Pristine.AuthMissing;

			List<string> pairs = [];
			foreach (JsonElement c in cookiesEl.EnumerateArray())
			{
				if (
					c.TryGetProperty("name", out JsonElement nameEl) is false
					|| nameEl.GetString() is not string name
				)
					continue;
				if (
					c.TryGetProperty("value", out JsonElement valueEl) is false
					|| valueEl.GetString() is not string value
				)
					continue;
				var domain =
					c.TryGetProperty("domain", out JsonElement dEl)
					&& dEl.ValueKind is JsonValueKind.String
						? dEl.GetString()
						: null;
				if (
					domain is not null
					&& domain.Contains("pristinestreaming.com", StringComparison.OrdinalIgnoreCase)
						is false
				)
					continue;
				pairs.Add($"{name}={value}");
			}

			if (pairs.Count == 0)
				return Errors.Pristine.AuthMissing;

			return string.Join("; ", pairs);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Warn("Pristine.Api.CookieLoadFailed: {Error}", ex.Message);
			return Errors.Pristine.AuthMissing;
		}
	}

	private static HttpRequestMessage NewRequest(
		HttpMethod method,
		string relativeUrl,
		string cookieHeader,
		string? apiKey
	)
	{
		HttpRequestMessage req = new(method, $"{BaseUrl}{relativeUrl}");
		req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
		req.Headers.TryAddWithoutValidation("Accept", "application/json");
		req.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
		req.Headers.TryAddWithoutValidation("Origin", BaseUrl);
		req.Headers.TryAddWithoutValidation("Referer", $"{BaseUrl}/app/browse");
		if (apiKey is not null)
			req.Headers.TryAddWithoutValidation(ApiKeyHeader, apiKey);
		return req;
	}
}
