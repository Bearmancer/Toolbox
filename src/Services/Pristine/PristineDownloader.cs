using Core;
using ErrorOr;

namespace Services.Pristine;

public sealed class PristineDownloader
{
	private const int MaxAttempts = 3;
	private const int RetryBaseS = 2;

	public async Task<ErrorOr<Success>> DownloadAsync(
		string url,
		string dest,
		HttpClient http,
		CancellationToken ct
	)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		var part = dest + ".part";
		for (var attempt = 1; attempt <= MaxAttempts; attempt++)
		{
			try
			{
				Telemetry.Debug(
					"Pristine.Dl.Attempt url={Url} dest={Dest} attempt={Attempt}/{Max}",
					url[..Math.Min(80, url.Length)],
					Path.GetFileName(dest),
					attempt,
					MaxAttempts
				);
				using HttpResponseMessage r = await http.GetAsync(
					url,
					HttpCompletionOption.ResponseHeadersRead,
					ct
				);
				if (!r.IsSuccessStatusCode)
				{
					Telemetry.Warn(
						"Pristine.Dl.HttpFailed url={Url} status={Status} attempt={Attempt}",
						url[..Math.Min(80, url.Length)],
						(int)r.StatusCode,
						attempt
					);
					if ((int)r.StatusCode >= 500 && attempt < MaxAttempts)
					{
						Telemetry.Debug(
							"Pristine.Dl.Retry5xx url={Url} attempt={Attempt}",
							url[..Math.Min(80, url.Length)],
							attempt
						);
						await Task.Delay(RetryBaseS * (1 << (attempt - 1)) * 1000, ct);
						continue;
					}

					return Errors.Pristine.DownloadFailed(dest);
				}

				await using FileStream fs = File.Create(part);
				Stream s = await r.Content.ReadAsStreamAsync(ct);
				await s.CopyToAsync(fs, ct);
				await fs.FlushAsync(ct);
				fs.Close();
				File.Move(part, dest, overwrite: true);
				Telemetry.Debug(
					"Pristine.Dl.Success dest={Dest} bytes={Bytes}",
					Path.GetFileName(dest),
					new FileInfo(dest).Length
				);
				return Result.Success;
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				Telemetry.Debug("Pristine.Dl.Cancelled dest={Dest}", Path.GetFileName(dest));
				if (File.Exists(part))
				{
					try
					{
						File.Delete(part);
					}
					catch (Exception delEx)
					{
						Telemetry.Debug(
							"Pristine.Dl.CleanupFailed part={Part}: {Error}",
							Path.GetFileName(part),
							delEx.Message
						);
					}
				}

				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Warn(
					"Pristine.Dl.AttemptFailed dest={Dest} attempt={Attempt}: {Error}",
					Path.GetFileName(dest),
					attempt,
					ex.Message
				);
				if (File.Exists(part))
				{
					try
					{
						File.Delete(part);
					}
					catch (Exception delEx)
					{
						Telemetry.Debug(
							"Pristine.Dl.CleanupFailed part={Part}: {Error}",
							Path.GetFileName(part),
							delEx.Message
						);
					}
				}

				if (attempt < MaxAttempts)
				{
					var delayMs = RetryBaseS * (1 << (attempt - 1)) * 1000;
					Telemetry.Debug(
						"Pristine.Dl.RetryDelay dest={Dest} delay={Delay}ms",
						Path.GetFileName(dest),
						delayMs
					);
					await Task.Delay(delayMs, ct);
				}
			}
		}

		Telemetry.Error(
			"Pristine.Dl.AllAttemptsFailed dest={Dest} url={Url}",
			Path.GetFileName(dest),
			url[..Math.Min(80, url.Length)]
		);
		return Errors.Pristine.DownloadFailed(dest);
	}
}
