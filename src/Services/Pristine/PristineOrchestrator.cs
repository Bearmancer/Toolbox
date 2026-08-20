using Core;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Pristine;

public sealed class PristineOrchestrator(PristineBrowser browser, PristinePollService pollService, IHttpClientFactory httpFactory)
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
		bool singleTrack = false,
		CancellationToken ct = default
	)
	{
		var dest = PristineCredentials.ResolveOutDir(outDir);

		Directory.CreateDirectory(dest);

		var effective = codes is { Length: > 0 } ? codes : Releases;
		Telemetry.Info("Pristine.Orchestrator.Start dest={Dest} codes={Codes} headless={Headless} single={Single}", dest, string.Join(",", effective), headless, singleTrack);

		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);

		Microsoft.Playwright.IBrowserContext ctx;
		try
		{
			ctx = await browser.CreateAsync(headless, ct);
		}
		catch (OperationCanceledException)
		{
			Telemetry.Debug("Pristine.Orchestrator.BrowserCancelled");
			throw;
		}
		catch (Exception ex)
		{
			Telemetry.Error("Pristine.Orchestrator.BrowserFailed: {Error}", ex.Message);
			return Errors.Pristine.BrowserFailed(ex.Message);
		}

		await using (ctx)
		{
			Microsoft.Playwright.IPage seed = await ctx.NewPageAsync().WaitAsync(ct);
			try
			{
				await seed.GotoAsync("https://pristinestreaming.com/app/browse", new Microsoft.Playwright.PageGotoOptions { WaitUntil = Microsoft.Playwright.WaitUntilState.DOMContentLoaded }).WaitAsync(ct);
				Telemetry.Debug("Pristine.Orchestrator.SeedGotoOk");
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Telemetry.Warn("Pristine.Orchestrator.SeedGotoFailed: {Error}", ex.Message);
			}

			var loggedIn = await WaitForLoginAsync(seed, ct, 180);
			Telemetry.Info("Pristine.Orchestrator.LoginCheck loggedIn={LoggedIn}", loggedIn);
			try
			{
				await seed.CloseAsync().WaitAsync(ct);
			}
			catch (OperationCanceledException)
			{
				Telemetry.Debug("Pristine.Orchestrator.SeedCloseCancelled");
			}
			catch (Exception ex)
			{
				Telemetry.Debug("Pristine.Orchestrator.SeedCloseFailed: {Error}", ex.Message);
			}

			if (loggedIn is false)
			{
				Telemetry.Warn("Pristine.Orchestrator.NotLoggedIn");
				return Errors.Pristine.LoginTimeout;
			}

			if (File.Exists(PristinePaths.AuthPath) is false)
			{
				Telemetry.Warn("Pristine.Orchestrator.AuthMissing path={Path}", PristinePaths.AuthPath);
				return Errors.Pristine.AuthMissing;
			}

			using HttpClient http = httpFactory.CreateClient();
			List<PristineAlbumResult> results = [];
			foreach (var code in effective)
			{
				ct.ThrowIfCancellationRequested();
				Telemetry.Info("Pristine.Orchestrator.AlbumStart code={Code} {Index}/{Total}", code, results.Count + 1, effective.Length);
				try
				{
					PristineAlbumResult r = await pollService.DownloadSingleAlbumAsync(ctx, code, dest, http, singleTrack, ct);
					results.Add(r);
					Telemetry.Info("Pristine.Orchestrator.AlbumDone code={Code} title={Title} {Downloaded}/{Expected} -> {Out}", r.Code, r.Title, r.Downloaded, r.Expected, r.OutPath);
				}
				catch (OperationCanceledException)
				{
					Telemetry.Debug("Pristine.Orchestrator.Cancelled code={Code}", code);
					throw;
				}
				catch (Exception ex)
				{
					Telemetry.Error("Pristine.Orchestrator.AlbumFailed code={Code}: {Error}", code, ex.Message);
					results.Add(new PristineAlbumResult { Code = code, Title = "error", OutPath = dest, Expected = 0, Downloaded = 0 });
				}

				if (effective.Length > 1)
				{
					try
					{
						await Task.Delay(3000, ct);
					}
					catch (OperationCanceledException)
					{
						throw;
					}
				}
			}

			Telemetry.Info("Pristine.Orchestrator.Done total={Total}", results.Count);
			return results;
		}
	}

	private static async Task<bool> WaitForLoginAsync(Microsoft.Playwright.IPage page, CancellationToken ct, int timeoutS = 180)
	{
		try
		{
			var url = page.Url;
			if (url.Contains("browse", StringComparison.OrdinalIgnoreCase) && url.Contains("login", StringComparison.OrdinalIgnoreCase) is false)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Orchestrator.WaitLoginUrlCheckFailed: {Error}", ex.Message);
		}

		try
		{
			await page.WaitForURLAsync("**pristinestreaming.com/app/browse**", new Microsoft.Playwright.PageWaitForURLOptions { Timeout = timeoutS * 1000 }).WaitAsync(ct);
			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (TimeoutException ex)
		{
			Telemetry.Warn("Pristine.Orchestrator.WaitLoginTimeout {Timeout}s: {Error}", timeoutS, ex.Message);
			return false;
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Orchestrator.WaitLoginError: {Error}", ex.Message);
			return false;
		}
	}
}
