using Core;
using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristineLoginService(PristineBrowser browser)
{
	private static readonly string PristineApp = "https://pristinestreaming.com/app/browse";
	private static readonly string SubscribeUrl =
		"https://pristineclassical.com/pages/player-subscribe";

	public async Task<bool> LoginAsync(CancellationToken ct = default)
	{
		using IDisposable _ = Telemetry.ForService(ServiceName.Pristine);
		Telemetry.Info("Opening browser to log in to Pristine Classical…");
		IBrowserContext ctx = await browser.CreateAsync(headless: false, ct);
		try
		{
			IPage page =
				ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync().WaitAsync(ct);
			try
			{
				await page.GotoAsync(
						PristineApp,
						new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }
					)
					.WaitAsync(ct);
				Telemetry.Debug("Pristine.Login.GotoBrowseOk url={Url}", page.Url);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Telemetry.Warn("Pristine.Login.GotoBrowseFailed: {Error}", ex.Message);
			}

			var alreadyIn = false;
			try
			{
				var url = page.Url;
				var hasBrowse = url.Contains("browse", StringComparison.OrdinalIgnoreCase);
				var hasLogin = url.Contains("login", StringComparison.OrdinalIgnoreCase);
				var isGuest = await page.Locator("text=Browsing as guest")
					.IsVisibleAsync()
					.WaitAsync(ct);
				alreadyIn = hasLogin is false && hasBrowse && isGuest is false;
				Telemetry.Debug(
					"Pristine.Login.Check url={Url} browse={Browse} login={Login} guest={Guest} alreadyIn={Already}",
					url,
					hasBrowse,
					hasLogin,
					isGuest,
					alreadyIn
				);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Telemetry.Debug("Pristine.Login.CheckFailed: {Error}", ex.Message);
			}

			if (alreadyIn is false)
			{
				try
				{
					await page.GotoAsync(
							SubscribeUrl,
							new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }
						)
						.WaitAsync(ct);
					Telemetry.Debug("Pristine.Login.GotoSubscribeOk url={Url}", page.Url);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Telemetry.Warn("Pristine.Login.GotoSubscribeFailed: {Error}", ex.Message);
				}
			}

			var ok = alreadyIn || await WaitForLoginAsync(page, 300, ct);
			Telemetry.Debug("Pristine.Login.WaitResult ok={Ok} alreadyIn={Already}", ok, alreadyIn);
			if (ok is false)
			{
				Telemetry.Warn("Pristine.Login.Timeout");
				return false;
			}

			var dir = Path.GetDirectoryName(PristinePaths.AuthPath);
			if (string.IsNullOrEmpty(dir) is false)
				Directory.CreateDirectory(dir);
			Telemetry.Debug("Pristine.Login.SavingAuth path={Path}", PristinePaths.AuthPath);
			try
			{
				await ctx.StorageStateAsync(
						new BrowserContextStorageStateOptions { Path = PristinePaths.AuthPath }
					)
					.WaitAsync(ct);
				Telemetry.Info("Logged in — session saved to {Path}", PristinePaths.AuthPath);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Telemetry.Error("Pristine.Login.SaveFailed: {Error}", ex.Message);
				return false;
			}

			return true;
		}
		finally
		{
			try
			{
				await ctx.CloseAsync().WaitAsync(ct);
				Telemetry.Debug("Pristine.Login.ContextClosed");
			}
			catch (OperationCanceledException)
			{
				Telemetry.Debug("Pristine.Login.CloseCancelled");
			}
			catch (Exception ex)
			{
				Telemetry.Debug("Pristine.Login.CloseFailed: {Error}", ex.Message);
			}
		}
	}

	private static async Task<bool> WaitForLoginAsync(
		IPage page,
		int timeoutS,
		CancellationToken ct = default
	)
	{
		ct.ThrowIfCancellationRequested();
		try
		{
			var url = page.Url;
			if (
				url.Contains("browse", StringComparison.OrdinalIgnoreCase)
				&& url.Contains("login", StringComparison.OrdinalIgnoreCase) is false
			)
			{
				Telemetry.Debug("Pristine.Login.AlreadyAtBrowse url={Url}", url);
				return true;
			}
		}
		catch (Exception ex)
		{
			Telemetry.Debug("Pristine.Login.UrlCheckFailed: {Error}", ex.Message);
		}

		Telemetry.Debug("Pristine.Login.WaitForBrowse timeout={Timeout}", timeoutS);
		try
		{
			await page.WaitForURLAsync(
					"**pristinestreaming.com/app/browse**",
					new PageWaitForURLOptions { Timeout = timeoutS * 1000 }
				)
				.WaitAsync(ct);
			Telemetry.Debug("Pristine.Login.WaitForBrowseOk url={Url}", page.Url);
			return true;
		}
		catch (TimeoutException ex)
		{
			Telemetry.Warn("Pristine.Login.WaitTimeout {Timeout}s: {Error}", timeoutS, ex.Message);
			return false;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			Telemetry.Debug("Pristine.Login.WaitError: {Error}", ex.Message);
			return false;
		}
	}
}
