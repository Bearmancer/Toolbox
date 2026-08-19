namespace Services.Pristine;

public sealed class PristineDownloader
{
	private const int MaxAttempts = 3;
	private const int RetryBaseS = 2;

	public async Task<bool> DownloadAsync(
		string url,
		string dest,
		HttpClient http,
		CancellationToken ct
	)
	{
		var part = dest + ".part";
		for (var attempt = 1; attempt <= MaxAttempts; attempt++)
		{
			try
			{
				using HttpResponseMessage r = await http.GetAsync(
					url,
					HttpCompletionOption.ResponseHeadersRead,
					ct
				);
				if (!r.IsSuccessStatusCode)
				{
					return false;
				}

				await using FileStream fs = File.Create(part);
				Stream s = await r.Content.ReadAsStreamAsync(ct);
				await s.CopyToAsync(fs, ct);
				await fs.FlushAsync(ct);
				fs.Close();
				File.Move(part, dest, overwrite: true);
				return true;
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception)
			{
				if (File.Exists(part))
				{
					try
					{
						File.Delete(part);
					}
					catch
					{
					}
				}

				if (attempt < MaxAttempts)
				{
					await Task.Delay(RetryBaseS * (1 << (attempt - 1)) * 1000, ct);
				}
			}
		}

		return false;
	}
}
