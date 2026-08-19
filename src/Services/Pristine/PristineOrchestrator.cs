using Core;
using ErrorOr;

namespace Services.Pristine;

public sealed class PristineOrchestrator(PristineBrowser browser, PristinePollService pollService)
{
	public static readonly string[] ToscaniniBeethoven =
	[
		"PASC552",
		"PASC553",
		"PASC554",
		"PASC555",
		"PASC556",
		"PASC557",
	];

	public static readonly string[] General =
	[
		"PASC762",
		"PASC648",
		"PASC313",
		"PASC003",
		"PASC040",
		"PASC393",
		"PASC626",
		"PASC653",
		"PASC246",
		"PASC619",
		"PASC731",
		"PASC760",
		"PACO180",
		"PASC569",
		"PASC669",
		"PASC633",
		"PASC655",
		"PASC741",
		"PASC736",
		"PASC131",
		"PASC006",
		"PASC759",
		"PASC764",
		"PASC486",
		"PASC450",
		"PASC443",
		"PASC447",
		"PAKM059",
	];

	public static readonly string[] Stokowski =
	[
		"PASC591",
		"PASC596",
		"PASC531",
		"PASC609",
		"PASC625",
		"PASC379",
		"PASC161",
		"PASC133",
		"PASC182",
		"PASC602",
		"PASC536",
		"PASC587",
		"PASC629",
	];

	public static readonly string[] Releases = [.. ToscaniniBeethoven, .. General, .. Stokowski];

	public async Task<ErrorOr<List<PristineAlbumResult>>> DownloadAsync(
		string[]? codes,
		string? outDir,
		bool headless = false,
		CancellationToken ct = default
	)
	{
		string dest;
		try
		{
			dest = !string.IsNullOrWhiteSpace(outDir) ? outDir! : PristineCredentials.Read().BaseOutDir;
		}
		catch (InvalidOperationException)
		{
			return Errors.Pristine.MissingBaseOutDir;
		}

		Directory.CreateDirectory(dest);

		var effective = codes != null && codes.Length > 0 ? codes : Releases;

		using IDisposable _ = Core.Telemetry.ForService(ServiceName.Pristine);

		Microsoft.Playwright.IBrowserContext ctx;
		try
		{
			ctx = await browser.CreateAsync(headless, ct);
		}
		catch (Exception ex)
		{
			return Errors.Pristine.BrowserFailed(ex.Message);
		}

		await using (ctx)
		{
			Microsoft.Playwright.IPage seed = await ctx.NewPageAsync();
			try
			{
				await seed.GotoAsync("https://pristinestreaming.com/app/browse", new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded });
			}
			catch
			{
			}

			var loggedIn = await WaitForLoginAsync(seed, 180);
			await seed.CloseAsync();
			if (!loggedIn)
			{
				return Errors.Pristine.LoginTimeout;
			}

			if (!File.Exists(PristinePaths.AuthPath))
			{
				return Errors.Pristine.AuthMissing;
			}

			HttpClient http = new();
			List<PristineAlbumResult> results = [];
			foreach (var code in effective)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					PristineAlbumResult r = await pollService.DownloadSingleAlbumAsync(ctx, code, dest, http, ct);
					results.Add(r);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch
				{
					results.Add(new PristineAlbumResult { Code = code, Title = "error", OutPath = dest, Expected = 0, Downloaded = 0 });
				}

				await Task.Delay(3000, ct);
			}

			return results;
		}
	}

	private static async Task<bool> WaitForLoginAsync(Microsoft.Playwright.IPage page, int timeoutS)
	{
		try
		{
			if (page.Url.Contains("browse", StringComparison.OrdinalIgnoreCase) && !page.Url.Contains("login", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		catch
		{
		}

		try
		{
			await page.WaitForURLAsync("**pristinestreaming.com/app/browse**", new Microsoft.Playwright.PageWaitForURLOptions { Timeout = timeoutS * 1000 });
			return true;
		}
		catch
		{
			return false;
		}
	}
}
