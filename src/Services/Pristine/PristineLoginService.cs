using Core;
using Microsoft.Playwright;

namespace Services.Pristine;

public sealed class PristineLoginService(PristineBrowser browser)
{
	private static readonly string PristineApp = "https://pristinestreaming.com/app/browse";
	private static readonly string SubscribeUrl = "https://pristineclassical.com/pages/player-subscribe";

	public async Task<bool> LoginAsync(CancellationToken ct = default)
	{
		IBrowserContext ctx = await browser.CreateAsync(headless: false, ct);
		try
		{
			IPage page = ctx.Pages.Count > 0 ? ctx.Pages[0] : await ctx.NewPageAsync();
			await page.GotoAsync(PristineApp, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

			var alreadyIn = false;
			try
			{
				var url = page.Url;
				var hasBrowse = url.Contains("browse", StringComparison.OrdinalIgnoreCase);
				var hasLogin = url.Contains("login", StringComparison.OrdinalIgnoreCase);
				var isGuest = await page.Locator("text=Browsing as guest").IsVisibleAsync();
				alreadyIn = !hasLogin && hasBrowse && !isGuest;
			}
			catch
			{
			}

			if (!alreadyIn)
			{
				await page.GotoAsync(SubscribeUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
			}

			var ok = alreadyIn || await WaitForLoginAsync(page, 300, ct);
			if (!ok)
			{
				return false;
			}

			var dir = Path.GetDirectoryName(PristinePaths.AuthPath) ?? "";
			Directory.CreateDirectory(dir);
			await ctx.StorageStateAsync(new BrowserContextStorageStateOptions { Path = PristinePaths.AuthPath });
			return true;
		}
		finally
		{
			await ctx.CloseAsync();
		}
	}

	private static async Task<bool> WaitForLoginAsync(IPage page, int timeoutS, CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
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
			await page.WaitForURLAsync("**pristinestreaming.com/app/browse**", new PageWaitForURLOptions { Timeout = timeoutS * 1000 });
			return true;
		}
		catch
		{
			return false;
		}
	}
}
